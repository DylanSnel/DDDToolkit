#!/usr/bin/env bash
#
# Writes the API reference of both supporting domains from their packages' code and XML documentation:
# docs/tenancy/reference/ for Tenancy, and docs/membership/reference/ for Membership.
#
# Run it after changing a public member of DDDToolkit.Supporting.Tenancy or DDDToolkit.Supporting.Membership,
# of the Entity Framework or Postgres package of either, or a comment on one, and commit the pages with the
# change. The pages say what the assemblies and their XML files say and nothing else, so they are never edited
# by hand: a wrong sentence is fixed in the /// comment it comes from.
#
# The tool builds the six packages in Release first, reads each domain's three with Roslyn as a consumer's
# compiler reads a package, and replaces every page in each folder. It prints, per domain, what it wrote, the
# members without a comment, and the parameters a comment leaves out. Which types go on which page is in
# build/ApiReference/Pages/, a file per domain.
set -euo pipefail

cd "$(dirname "$0")/.."
dotnet run --project build/ApiReference --configuration Release
