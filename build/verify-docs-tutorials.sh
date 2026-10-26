#!/usr/bin/env bash
#
# Builds the tutorials in docs/ as a reader would type them, against the packed packages.
#
# Why this exists. A tutorial shows every file whole, step by step, and a reader types or pastes it. Nothing else
# compiles that code: a renamed method, a namespace the generators moved, or a step that forgets a using breaks the
# tutorial without breaking a single test. This takes every file from the pages as they are written (the last
# version of each, since a step shows a file whole again when it changes it), puts them in a fresh folder as one
# solution, and builds it with the toolkit's packages at the version being verified. Any error or warning fails.
#
# The tutorials build one application, Harbor, page after page: Tenancy's first, then Membership's on top of it.
#
# Usage: build/verify-docs-tutorials.sh [version]
#   version  defaults to 0.0.0-ci, matching what the Build and Test workflow packs.

set -euo pipefail

version="${1:-0.0.0-ci}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
feed="$root/nupkgs"
packages="$root/artifacts/docs-tutorials-packages"

# Under Git Bash the .NET SDK reads /c/... as C:\c\...; a path written into a file has to be converted.
native_path() {
  if command -v cygpath > /dev/null 2>&1; then cygpath -w "$1"; else printf '%s' "$1"; fi
}

# The first Python that runs: on Windows, python3 can be a Store shortcut that only prints a hint.
python=""
for candidate in python3 python; do
  if "$candidate" --version > /dev/null 2>&1; then
    python="$candidate"
    break
  fi
done
if [ -z "$python" ]; then
  echo "No Python: the extraction is a Python script." >&2
  exit 1
fi

if ! compgen -G "$feed/*DDDToolkit.$version.nupkg" > /dev/null; then
  echo "No DDDToolkit package at $version in $feed. Pack first:" >&2
  echo "  for p in \$(find ./Source -name '*.csproj'); do dotnet pack \"\$p\" -c Release -o nupkgs -p:PackageVersion=$version; done" >&2
  exit 1
fi

# The pages, in reading order. Membership's tutorial continues the application Tenancy's builds.
pages=("$root/docs/tenancy/tutorial.md")
if [ -f "$root/docs/membership/tutorial.md" ]; then
  pages+=("$root/docs/membership/tutorial.md")
fi

# A fresh package folder: NuGet caches by id and version, and a package of an earlier run at this version would
# be restored instead of this build's.
rm -rf "$packages"
export NUGET_PACKAGES="$packages"

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

echo "==> Extracting the files of: ${pages[*]#$root/}"
"$python" "$root/build/docs-tutorials/extract.py" "$work" "$version" "${pages[@]}" | sed 's/^/    /'

# nuget.org for everything else the tutorial uses, and this build's packages for the toolkit.
cat > "$work/NuGet.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="this-build" value="$(native_path "$feed")" />
  </packageSources>
</configuration>
EOF

(
  cd "$work"
  dotnet new sln -n Harbor --format slnx -o . > /dev/null
  find src -name '*.csproj' | sort | while read -r project; do
    dotnet sln Harbor.slnx add "$project" > /dev/null
  done
)

echo "==> Building them as one solution"
build_log="$work/build.log"
# The export is left off: it writes the migrations' SQL, and the tutorial makes its migrations with dotnet ef,
# which this build does not run.
if ! dotnet build "$work/Harbor.slnx" -tl:off -nologo "-p:SupabaseMigrationsExport=" > "$build_log" 2>&1; then
  grep -E ': error ' "$build_log" | sort -u >&2 || cat "$build_log" >&2
  echo "FAILED: the tutorials do not build as written." >&2
  exit 1
fi

if grep -qE ': warning [A-Z]+[0-9]+:' "$build_log"; then
  grep -E ': warning [A-Z]+[0-9]+:' "$build_log" | sort -u >&2
  echo "FAILED: the tutorials build with warnings; a reader who types them sees each one." >&2
  exit 1
fi

echo "==> The tutorials build as written, with no warning"
