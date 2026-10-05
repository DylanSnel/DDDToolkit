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
#   1. Every assembly a package ships in lib/<tfm>/ has its XML documentation beside it, which is where
#      an editor reads the package's comments from.
#   2. Examples/DDDToolkit.NugetApi: every generator arrives as a dependency of the package above it
#      and produces what Check.cs names.
#   3. build/package-consumers: DDD_Module reaches the generators wherever they run. With only
#      Abstractions and Analyzers, with only the DDDToolkit package, and in a project that gets the
#      toolkit through a project reference.
#   4. DDD00014: a project that has the generators and not their props file is told so, and a project
#      that has both and sets no DDD_Module is not.
#   5. The supporting domains: an application with Tenancy and Membership, in a domain project on their
#      domain packages, an infrastructure project on their Postgres packages and a host, builds with
#      everything it needs arriving as a dependency, Membership's two generators among it, which ship
#      inside Membership's packages and write nothing in the host; and the packages carry their Dutch
#      texts.
#   6. The Supabase export of that application runs in its host, also when SupabaseMigrationsExport is
#      given for the whole build, on the command line: every other project ignores it, with no crash and
#      no warning. The host's SupabaseLoginRole reaches the export, which writes the login role's file.
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

# NuGet lowercases the version for that folder as well. A release tag may carry a prerelease suffix in
# capitals, v3.2.0-RC.1, and the folder is then 3.2.0-rc.1: Windows finds it either way, the release
# job runs on Linux.
version_folder="$(tr '[:upper:]' '[:lower:]' <<< "$version")"

echo "==> Verifying package consumption at version $version, package ids ${prefix}DDDToolkit.*"

if ! compgen -G "$feed/$core_id.$version.nupkg" > /dev/null; then
  echo "No $core_id.$version.nupkg in $feed. Pack first:" >&2
  echo "  for p in \$(find ./Source -name '*.csproj'); do dotnet pack \"\$p\" -c Release -o nupkgs -p:PackageVersion=$version; done" >&2
  exit 1
fi

# The XML documentation beside every assembly in lib/<tfm>/. Visual Studio and every other editor read a
# package's /// comments from that file and nowhere else, and nothing in a build reads it, so a package
# packed without it restores and builds like any other, and its consumers see none of its comments. That
# is how 3.2.0-preview.1 shipped. Read from the packed packages themselves, every one of them, rather than
# from the few the consumers below restore. A satellite assembly, nl/<name>.resources.dll, is one folder
# deeper and needs none; a package that only carries generators has no lib/ and nothing to check.
echo "==> The XML documentation beside every assembly in lib/"
undocumented=""
assemblies=0
for nupkg in "$feed/${prefix}"DDDToolkit*."$version".nupkg; do
  entries="$(unzip -Z1 "$nupkg" | tr -d '\r')"
  while IFS= read -r assembly; do
    assemblies=$((assemblies + 1))
    if ! grep -qxF "${assembly%.dll}.xml" <<< "$entries"; then
      undocumented+="    $(basename "$nupkg"): $assembly"$'\n'
    fi
  done < <(grep -E '^lib/[^/]+/[^/]+\.dll$' <<< "$entries" || true)
done

if [ -n "$undocumented" ]; then
  echo "FAILED: these assemblies have no XML documentation beside them, so an editor shows none of their comments." >&2
  echo "        GenerateDocumentationFile is set for the packages in Directory.Build.props." >&2
  printf '%s' "$undocumented" >&2
  exit 1
fi

if [ "$assemblies" = 0 ]; then
  echo "FAILED: no package in $feed at $version has an assembly in lib/, so there was no documentation to check." >&2
  exit 1
fi

echo "    $assemblies assemblies, each with its XML documentation"

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
analyzers_package="$packages/$(tr '[:upper:]' '[:lower:]' <<< "$analyzers_id")/$version_folder"
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

# ---------------------------------------------------------------------------------------------------
# The supporting domains, Tenancy and Membership, three packages each.
#
# SupportingDomains is an application with both, in the layers an application has. Its domain project
# declares Tenancy's classes with the package's templates, and a resource with members and a role it
# keeps for that resource with Membership's, and references the two domain packages alone. Its
# infrastructure project maps and registers them and references only the two Postgres packages of the
# supporting domains, beside the Supabase export and the provider it needs, so the Entity Framework
# packages, the toolkit's own and every generator it needs have to arrive as a dependency, at the version
# given. Its host references the infrastructure project and nothing else.
#
# Membership's two generators are no packages of their own: they ship inside Membership's packages, in
# analyzers/dotnet/cs, so a package packed without one would still restore, and only a build that needs
# what it writes would notice. This one does, since each project calls what its generators write, and
# treats a generator the compiler could not load, which is a warning, as an error. A generator also
# reaches every project above the one it is meant for, so the host is checked to have been handed both
# of them and to have been written nothing by either.
# ---------------------------------------------------------------------------------------------------

# The file $2 must be in the restored package $1.
expect_in_package() {
  local folder
  folder="$packages/$(tr '[:upper:]' '[:lower:]' <<< "$1")/$version_folder"

  if [ ! -f "$folder/$2" ]; then
    echo "FAILED: $2 is missing from the $1 package." >&2
    exit 1
  fi
}

# The generators that wrote a file in the project folder $1, one per line.
generators_that_wrote() {
  find "$work/package-consumers/$1/obj" -path '*generated*' -name '*.g.cs' | sed "s#.*/generated/##" | cut -d/ -f1 | sort -u
}

# Each generator named after $1 wrote a file in the project folder $1.
expect_generators_wrote() {
  local project="$1" written expected
  shift
  written="$(generators_that_wrote "$project")"

  for expected in "$@"; do
    if ! grep -qxF "$expected" <<< "$written"; then
      echo "FAILED: $project: $expected produced nothing. It should have arrived with the package that carries it." >&2
      exit 1
    fi

    echo "    $project: $expected"
  done
}

echo "==> The supporting domains, in a domain, an infrastructure and a host project"
build_consumer SupportingDomains/Host/Acme.Press.Host.csproj
expect_no_missing_properties_warning SupportingDomains

expect_generators_wrote SupportingDomains/Domain \
  "DDDToolkit.Analyzers" \
  "DDDToolkit.Supporting.Membership.Analyzers"

expect_generators_wrote SupportingDomains/Infrastructure \
  "DDDToolkit.Analyzers" \
  "DDDToolkit.EntityFramework.Analyzers" \
  "DDDToolkit.Supporting.Membership.EntityFramework.Analyzers"

# The host is handed Membership's generators too, as a dependency of a dependency, and has nothing of
# theirs to be written: the classes and the contexts are below it. A second member list or a second
# registration there would be a generator that writes for what a project only references.
host_assets="$work/package-consumers/SupportingDomains/Host/obj/project.assets.json"
host_written="$(generators_that_wrote SupportingDomains/Host)"
for generator in DDDToolkit.Supporting.Membership.Analyzers DDDToolkit.Supporting.Membership.EntityFramework.Analyzers; do
  if ! grep -qF "analyzers/dotnet/cs/$generator.dll" "$host_assets"; then
    echo "FAILED: SupportingDomains/Host: $generator was not handed to the host, so finding nothing written by it proves nothing." >&2
    exit 1
  fi

  if grep -qxF "$generator" <<< "$host_written"; then
    echo "FAILED: SupportingDomains/Host: $generator wrote a file in the host, which declares nothing of its own." >&2
    exit 1
  fi
done

echo "    SupportingDomains/Host: handed both of Membership's generators, and written nothing by either"

# Where each generator ships, and the Dutch texts of both domains, which nothing in a build reads.
expect_in_package "${prefix}DDDToolkit.Supporting.Membership" analyzers/dotnet/cs/DDDToolkit.Supporting.Membership.Analyzers.dll
expect_in_package "${prefix}DDDToolkit.Supporting.Membership.EntityFramework" analyzers/dotnet/cs/DDDToolkit.Supporting.Membership.EntityFramework.Analyzers.dll
expect_in_package "${prefix}DDDToolkit.Supporting.Tenancy" lib/net10.0/nl/DDDToolkit.Supporting.Tenancy.resources.dll
expect_in_package "${prefix}DDDToolkit.Supporting.Membership" lib/net10.0/nl/DDDToolkit.Supporting.Membership.resources.dll

for generator in DDDToolkit.Supporting.Membership.Analyzers DDDToolkit.Supporting.Membership.EntityFramework.Analyzers; do
  if [ -f "$feed/$prefix$generator.$version.nupkg" ]; then
    echo "FAILED: $prefix$generator was packed as a package of its own. It ships inside the package that needs it." >&2
    exit 1
  fi
done

# ---------------------------------------------------------------------------------------------------
# The Supabase export of the same application.
#
# The infrastructure project references the Supabase package, where its contexts and their marked factories
# are, and the host turns the export on in its project file, as docs/supabase.md says; the host lists the
# row access SQL of both packages, so the build above wrote the two contexts' access files. The package's
# build step and generator reach both projects through buildTransitive, so both import them; through
# project references only the host did, which is how a property that reached the others went unnoticed.
#
# A CI script may give the property on the command line instead, and a Directory.Build.props sets it for
# every project: either way it reaches every project the build reaches. The step starts the program the
# project built, and in the infrastructure project, a library, it started the library: MissingMethodException,
# "Entry point not found", and MSB3073. So each value the property takes is given here for the whole build:
# Write and Check reach the host, which exports, and no other project, which hears nothing of it, not even
# DDD00054 about what only the host lists; an empty value turns the export off in the host as well.
# ---------------------------------------------------------------------------------------------------

supabase_generator="DDDToolkit.EntityFramework.Supabase.Analyzers"
supabase_migrations="$work/package-consumers/SupportingDomains/supabase/migrations"

# What the export said, one line per file: "<status> <file>".
export_lines() {
  { grep -E 'Supabase migrations: ' "$build_log" || true; } | sed 's/.*Supabase migrations: *//' | tr -d '\r' | sort -u
}

# The export ran in the host and in no other project, and said $1 of every file.
expect_exported_by_the_host_only() {
  local status="$1" project lines

  if ! grep -qxF "$supabase_generator" <<< "$(generators_that_wrote SupportingDomains/Host)"; then
    echo "FAILED: SupportingDomains/Host: the Supabase generator wrote no list of sources in the host that turned the export on." >&2
    exit 1
  fi

  for project in Domain Infrastructure; do
    if grep -qxF "$supabase_generator" <<< "$(generators_that_wrote "SupportingDomains/$project")"; then
      echo "FAILED: SupportingDomains/$project: the Supabase generator wrote its export hook into a project that is not the host." >&2
      exit 1
    fi
  done

  lines="$(export_lines)"
  if grep -vqE "^$status " <<< "$lines"; then
    echo "FAILED: the export said something other than $status:" >&2
    echo "$lines" >&2
    exit 1
  fi

  if [ "$(grep -cE '_access\.press\.ddd\.sql$' <<< "$lines")" != 2 ]; then
    echo "FAILED: the export did not report the access files of both contexts:" >&2
    echo "$lines" >&2
    exit 1
  fi

  echo "    SupportingDomains/Host: exported"
  echo "$lines" | sed 's/^/      /'
}

# Nothing in the log is a warning: not DDD00054 in a project that is not the host, and not the step's own.
expect_no_warning() {
  if grep -q 'DDD00054' "$build_log"; then
    echo "FAILED: $1: DDD00054 was reported. Only the host lists row access contributions, and no other project may run the generator that asks for them." >&2
    exit 1
  fi

  if grep -qE ': warning [A-Z]+[0-9]+:|warning : ' "$build_log"; then
    echo "FAILED: $1: the build warned:" >&2
    grep -E ': warning [A-Z]+[0-9]+:|warning : ' "$build_log" | sort -u >&2
    exit 1
  fi
}

# Starts the three projects from nothing, so what a build writes in obj/ is its own.
clean_supporting_domains() {
  local project
  for project in Domain Infrastructure Host; do
    rm -rf "$work/package-consumers/SupportingDomains/$project/obj" "$work/package-consumers/SupportingDomains/$project/bin"
  done
}

echo "==> The Supabase export, turned on in the host's project file"
expect_exported_by_the_host_only Created
expect_no_warning SupportingDomains

if [ "$(find "$supabase_migrations" -name '*_access.press.ddd.sql' | wc -l | tr -d ' ')" != 2 ]; then
  echo "FAILED: the export did not write the access files of both contexts into $supabase_migrations." >&2
  exit 1
fi

# The host names the role it logs in as with SupabaseLoginRole, which the packaged build step hands the export
# among its variables: the export wrote the migration that makes the role, after every other file, granting it
# the roles callers run as and nothing else.
login_role_file="$(find "$supabase_migrations" -name '*_login_role.press_api.ddd.sql')"
if [ -z "$login_role_file" ] || [ "$(wc -l <<< "$login_role_file" | tr -d ' ')" != 1 ]; then
  echo "FAILED: the export did not write the login role's file into $supabase_migrations, so SupabaseLoginRole did not reach it." >&2
  exit 1
fi

if [ "$(find "$supabase_migrations" -name '*.sql' -exec basename {} \; | LC_ALL=C sort | tail -n 1)" != "$(basename "$login_role_file")" ]; then
  echo "FAILED: the login role's file is not the last in $supabase_migrations, after the access files that make the roles it grants." >&2
  exit 1
fi

# Each role in a statement of its own, in the order the file grants them.
granted="$(tr -d '\r' < "$login_role_file" | sed -n 's/^ *GRANT \(.*\) TO press_api;$/\1/p' | paste -sd ' ' -)"
if [ "$granted" != "anon authenticated ddd_system_in" ]; then
  echo "FAILED: the login role's file grants press_api '$granted', not the roles callers run as:" >&2
  cat "$login_role_file" >&2
  exit 1
fi

echo "    SupportingDomains/Host: $(basename "$login_role_file")"

for mode in Check Write ""; do
  echo "==> SupabaseMigrationsExport='$mode' for the whole build, on the command line"
  clean_supporting_domains

  if ! build_consumer SupportingDomains/Host/Acme.Press.Host.csproj "-p:SupabaseMigrationsExport=$mode"; then
    if grep -qE 'MissingMethodException|MSB3073' "$build_log"; then
      echo "FAILED: SupabaseMigrationsExport=$mode for the whole build ran the export's step in a project that is not the host, and the program it started has no entry point." >&2
    else
      echo "FAILED: the build with SupabaseMigrationsExport=$mode for the whole build failed." >&2
    fi
    exit 1
  fi

  expect_no_warning "SupabaseMigrationsExport=$mode"

  if [ -n "$mode" ]; then
    # The files the first build wrote are what the model and the rules give, so Write writes none and Check finds them so.
    expect_exported_by_the_host_only Unchanged
  elif [ -n "$(export_lines)" ] || grep -qxF "$supabase_generator" <<< "$(generators_that_wrote SupportingDomains/Host)"; then
    echo "FAILED: an empty SupabaseMigrationsExport for the whole build did not turn the export off in the host." >&2
    exit 1
  else
    echo "    SupportingDomains/Host: no export"
  fi
done

echo "==> Package consumption verified"
