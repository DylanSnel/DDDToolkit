# DDDToolkit.NugetApi

The only thing in this repository that consumes the toolkit the way you do: as packages.

## Why it exists

Everything else here references the toolkit with `ProjectReference`. That is a different path through
MSBuild from the one a real consumer takes, and the differences are exactly where this has broken
before:

| | Inside this repository | A consumer |
|---|---|---|
| Generators arrive as | `ProjectReference` with `OutputItemType="Analyzer"` | `analyzers/dotnet/cs` inside the package |
| `DDD_Module` arrives from | `Directory.Build.props` | `build/DDDToolkit.props` inside the `DDDToolkit` package |
| Integration generators arrive | listed one by one in every project | transitively, as a dependency of the package above |

A green solution build and eleven packages that pack prove the packages are well formed. They prove
nothing about whether they work once installed. This project is that proof.

## What it checks

`Domain.cs` declares one of each thing a generator reacts to, including an aggregate root that owns a
child entity, so the code a root is given for answering on behalf of its children has to compile here
as well. `Check.cs` then names every member those generators are supposed to produce. Nothing calls
it; the compiler is the assertion, so a generator that did not arrive, or arrived and produced
something differently named, fails the build with a message that says which member is missing.

Two checks matter more than the rest:

- **`AddNugetTestConverters`.** The name comes from `<DDD_Module>NugetTest</DDD_Module>`, which only
  reaches the generator through `build/DDDToolkit.props`. If that file stops shipping or stops
  applying, the generator falls back to the assembly name and this stops compiling.
- **The three analyzer packages are not referenced.** Only `DDDToolkit`,
  `DDDToolkit.EntityFramework` and `DDDToolkit.FluentValidation` are. Each generator has to arrive as
  a dependency of the package above it, and the verification script fails if one produced nothing.

## Running it

```bash
build/verify-package-consumption.sh
```

The script packs nothing itself. Pack first, at the version you want to verify:

```bash
for p in $(find ./Source -name '*.csproj'); do
  dotnet pack "$p" -c Release -o nupkgs -p:PackageVersion=0.0.0-ci
done
```

The Build and Test workflow does both on every pull request, so this runs before a release rather than
after one.

## Why it is not in DDDToolkit.slnx

It cannot build until the packages exist, and a solution build has no way to pack first. Adding it
would make `dotnet build DDDToolkit.slnx` fail on a clean checkout.

`NuGet.config` clears the package sources before adding the local feed. Without that, a restore could
quietly satisfy `DDDToolkit` from nuget.org and verify the published 2.0.22 instead of your build.
