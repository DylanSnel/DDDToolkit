#!/usr/bin/env bash
#
# Builds Examples/DDDToolkit.NugetApi and the projects in build/package-consumers against the packed
# packages instead of the projects.
#
# Why this exists. A consumer gets the generators from analyzers/dotnet/cs, and the MSBuild properties
# they read, DDD_Module for one, from the props file inside the DDDToolkit.Analyzers package. Neither
# path is used inside this repository, where every project reference is a ProjectReference and
# Directory.Build.props imports that props file from Source. So the whole of the packaging contract is
# untested by a green solution build, and it has broken before.
#
# What it proves, in order:
#   1. Examples/DDDToolkit.NugetApi: every generator arrives as a dependency of the package above it
#      and produces what Check.cs names.
#   2. build/package-consumers: DDD_Module reaches the generators wherever they run. With only
#      Abstractions and Analyzers, with only the DDDToolkit package, and in a project that gets the
#      toolkit through a project reference.
#   3. DDD00014: a project that has the generators and not their props file is told so, and a project
#      that has both and sets no DDD_Module is not.
#
# Usage: build/verify-package-consumption.sh [version]
#   version  defaults to 0.0.0-ci, matching what the Build and Test workflow packs.

set -euo pipefail

version="${1:-0.0.0-ci}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
feed="$root/nupkgs"
consumer="$root/Examples/DDDToolkit.NugetApi"
consumers="$root/build/package-consumers"
packages="$root/artifacts/package-verification"

# Under Git Bash the shell's own paths look like /c/Repos/..., and the .NET SDK is a Windows program
# that reads that as C:\c\Repos\... MSYS rewrites paths it passes as arguments but not paths written
# into a file, so the feed path in NuGet.config has to be converted here. cygpath is absent on Linux,
# where the paths are already right.
native_path() {
  if command -v cygpath > /dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}

feed_native="$(native_path "$feed")"
packages_native="$(native_path "$packages")"

# The ids the packages were packed under, asked of the build rather than written down here, so the
# prefix in Directory.Build.props stays the one place that decides them. The tr drops the carriage
# return the SDK ends the line with on Windows. NuGet lowercases the id for the folder it restores a
# package into.
core_id="$(dotnet msbuild "$root/Source/DDDToolkit/DDDToolkit.csproj" -getProperty:PackageId | tr -d '\r')"
prefix="${core_id%DDDToolkit}"
analyzers_id="${prefix}DDDToolkit.Analyzers"

echo "==> Verifying package consumption at version $version, package ids ${prefix}DDDToolkit.*"

if ! compgen -G "$feed/$core_id.$version.nupkg" > /dev/null; then
  echo "No $core_id.$version.nupkg in $feed. Pack first:" >&2
  echo "  for p in \$(find ./Source -name '*.csproj'); do dotnet pack \"\$p\" -c Release -o nupkgs -p:PackageVersion=$version; done" >&2
  exit 1
fi

# A fresh package folder every run. NuGet caches by id and version, so without this a second run at
# the same version would restore the packages from the first one and verify nothing.
rm -rf "$packages"

# Build from a copy, so restoring packages never writes obj/ into the working tree and the consumer
# cannot accidentally pick up this repository's Directory.Build.props or Directory.Packages.props.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp -r "$consumer" "$work/consumer"
rm -rf "$work/consumer/obj" "$work/consumer/bin"

# The feed in the project's NuGet.config is relative to the project, which stops resolving once the
# project has been copied, so the copy gets a config pointing at the real feed. Written out whole
# rather than edited in place: under Git Bash the feed is a Windows path, and every in-place tool here
# treats backslashes in a replacement as escapes. A heredoc does not, and it needs nothing installed.
#
# Overwriting means the copy no longer proves the shipped config clears the package sources, so assert
# that separately. Without clear, a restore could satisfy the packages from nuget.org and verify the
# published ones instead of this build.
if ! grep -q '<clear />' "$consumer/NuGet.config"; then
  echo "FAILED: $consumer/NuGet.config no longer clears the package sources, so a restore could" >&2
  echo "        satisfy $core_id from nuget.org and verify the published package, not this build." >&2
  exit 1
fi

write_nuget_config() {
  cat > "$1/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$feed_native" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
EOF
}

write_nuget_config "$work/consumer"

echo "==> Restoring from $feed_native"
dotnet restore "$work/consumer/DDDToolkit.NugetApi.csproj" \
  --packages "$packages_native" \
  -p:DDDToolkitPackageVersion="$version"

# The code fixes need the Workspaces layer, which the compiler does not load, so they are a separate
# assembly packed next to the generators rather than a package of their own. Nothing in a build uses
# them, so nothing below would notice them missing; only the IDE would, silently.
analyzers_package="$packages/$(tr '[:upper:]' '[:lower:]' <<< "$analyzers_id")/$version"
analyzers_folder="$analyzers_package/analyzers/dotnet/cs"
for assembly in DDDToolkit.Analyzers.dll DDDToolkit.Analyzers.CodeFixes.dll; do
  if [ ! -f "$analyzers_folder/$assembly" ]; then
    echo "FAILED: $assembly is missing from analyzers/dotnet/cs in the $analyzers_id package." >&2
    exit 1
  fi
done

# The props file that declares the properties the generators read, in the package that carries the
# generators. Both folders, and under the package's id, because NuGet imports no other name: build/ for
# a client that knows nothing of buildTransitive/, and buildTransitive/ for every project that does not
# reference the package itself. The consumers below prove it is imported; this says which file was
# missing or misnamed when they fail.
for folder in build buildTransitive; do
  if [ ! -f "$analyzers_package/$folder/$analyzers_id.props" ]; then
    echo "FAILED: $folder/$analyzers_id.props is missing from the $analyzers_id package." >&2
    exit 1
  fi
done

echo "==> Building the consumer"
dotnet build "$work/consumer/DDDToolkit.NugetApi.csproj" \
  --no-restore \
  -c Release \
  -p:DDDToolkitPackageVersion="$version" \
  -p:EmitCompilerGeneratedFiles=true \
  -p:UseSharedCompilation=false \
  -nodeReuse:false

# The build proves the generators ran and produced what Check.cs names. This proves they ran because
# they arrived transitively, rather than because something else defined those types.
generated="$(find "$work/consumer/obj" -path '*generated*' -name '*.g.cs' | sed "s#.*/generated/##" | sort)"
echo "==> Generated by the packaged generators:"
echo "$generated" | sed 's/^/    /'

for expected in \
  "DDDToolkit.Analyzers" \
  "DDDToolkit.EntityFramework.Analyzers" \
  "DDDToolkit.FluentValidation.Analyzers"
do
  if ! grep -q "^$expected/" <<< "$generated"; then
    echo "FAILED: $expected produced nothing. It should have arrived as a dependency of the package above it." >&2
    exit 1
  fi
done

# ---------------------------------------------------------------------------------------------------
# DDD_Module reaches the generators wherever they run.
#
# The project above references DDDToolkit itself and two packages that depend on it, which is one of
# several ways the generators arrive. The projects in build/package-consumers are the others. Each sets
# <DDD_Module>Billing</DDD_Module> and declares one event, so the generator writes {Module}EventNames:
# BillingEventNames when the property reached it, and a class named after the assembly when it did not.
# That fallback compiles, which is why it went unnoticed, and why this reads the generated file instead
# of leaving it to the compiler.
# ---------------------------------------------------------------------------------------------------

cp -r "$consumers" "$work/package-consumers"
find "$work/package-consumers" -type d \( -name obj -o -name bin \) -prune -exec rm -rf {} +
write_nuget_config "$work/package-consumers"

build_log="$work/build.log"

# Restores and builds one consumer from nothing, with the output kept in $build_log. Every build
# starts without obj/, because two of them build the same project with different properties.
build_consumer() {
  local project="$work/package-consumers/$1"
  shift

  rm -rf "$(dirname "$project")/obj" "$(dirname "$project")/bin"

  dotnet build "$project" \
    --packages "$packages_native" \
    -c Release \
    -p:DDDToolkitPackageVersion="$version" \
    -p:DDDPackageIdPrefix="$prefix" \
    -p:EmitCompilerGeneratedFiles=true \
    -p:UseSharedCompilation=false \
    -nodeReuse:false \
    "$@" 2>&1 | tee "$build_log"
}

# The class the packaged generator named after the module must be $2, in the project folder $1.
expect_event_names_class() {
  local file
  file="$(find "$work/package-consumers/$1/obj" -path '*generated*' -name 'EventNames.g.cs')"

  if [ -z "$file" ]; then
    echo "FAILED: $1: the generators wrote no EventNames.g.cs, so they did not run here." >&2
    exit 1
  fi

  local actual
  actual="$(sed -n 's/^public static class \([A-Za-z0-9_]*\).*/\1/p' "$file" | tr -d '\r')"

  if [ "$actual" != "$2" ]; then
    echo "FAILED: $1: the generated class is $actual, expected $2." >&2
    exit 1
  fi

  echo "    $1: $actual"
}

expect_no_missing_properties_warning() {
  if grep -q 'DDD00014' "$build_log"; then
    echo "FAILED: $1: DDD00014 was reported, so the props file of $analyzers_id was not imported." >&2
    exit 1
  fi
}

echo "==> Only Abstractions and Analyzers, the way a contracts project references the toolkit"
build_consumer ContractsOnly/Acme.Billing.Contracts.csproj
expect_no_missing_properties_warning ContractsOnly
expect_event_names_class ContractsOnly BillingEventNames

echo "==> Only the DDDToolkit package"
build_consumer CoreOnly/Acme.Billing.csproj
expect_no_missing_properties_warning CoreOnly
expect_event_names_class CoreOnly BillingEventNames

echo "==> The toolkit through a project reference, and no package reference of its own"
build_consumer ThroughProjectReference/Billing/Acme.Billing.csproj
expect_no_missing_properties_warning ThroughProjectReference/Billing
expect_event_names_class ThroughProjectReference/Billing BillingEventNames

# The other half: when the props file does not arrive, the build has to say so. This is the contracts
# project again, with a reference that keeps the generators and excludes the package's build assets.
# Before DDD00014 this built without a word and named the class after the assembly.
echo "==> Without the props file: DDD00014"
build_consumer ContractsOnly/Acme.Billing.Contracts.csproj -p:WithoutToolkitBuildAssets=true
expect_event_names_class ContractsOnly AcmeBillingContractsEventNames

if ! grep -q 'warning DDD00014' "$build_log"; then
  echo "FAILED: the generators ran without their props file and DDD00014 was not reported." >&2
  exit 1
fi

if ! grep -q 'docs/diagnostics#ddd00014' "$build_log"; then
  echo "FAILED: DDD00014 was reported without the link to its entry in the docs." >&2
  exit 1
fi

# And the case DDD00014 must stay out of: the props file is imported and the project sets no
# DDD_Module. The property then reaches the generator as an empty value, which is not the same as not
# reaching it. If the two were confused, every project that leaves the name to the assembly would warn.
echo "==> With the props file and no DDD_Module: named after the assembly, and no warning"
build_consumer ContractsOnly/Acme.Billing.Contracts.csproj -p:DDD_Module=
expect_no_missing_properties_warning ContractsOnly
expect_event_names_class ContractsOnly AcmeBillingContractsEventNames

echo "==> Package consumption verified"
