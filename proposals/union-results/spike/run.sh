#!/bin/bash
# Compiles each case in cases/ as a library together with Common.cs and Polyfill.cs, and prints the diagnostics.
# CSC: a command that runs csc.dll of Microsoft.Net.Compilers.Toolset 5.12.0 or later, such as "dotnet <path>/csc.dll".
# REF: the folder with the reference assemblies of Microsoft.NETCore.App (net10.0 or net11.0).
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
for f in "$HERE"/cases/c*.cs "$HERE"/cases/d*.cs; do
  echo "=== $(basename "$f")"
  $CSC -nologo -t:library -langversion:preview -nullable:enable -out:"${TMPDIR:-/tmp}/union-spike.dll" \
    $(ls "$REF"/*.dll | sed 's/^/-r:/') "$HERE/Polyfill.cs" "$HERE/cases/Common.cs" "$f" 2>&1 \
    | sed 's#.*/cases/##' | grep -E "error|warning" || echo "(no diagnostics)"
done
