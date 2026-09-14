# DDDToolkit.NugetApi

This project checks the toolkit **as a consumer sees it**: from the published NuGet packages, not from
project references. Everything else in this repository builds the generators from source, which is
faster to work with and hides a whole class of problem. A generator that never made it into the
`analyzers/dotnet/cs` folder of a package, a missing props file, a dependency that was not declared,
a target framework mismatch: none of those show up until somebody installs the package. This is the
project that installs it.

## Why it is pinned to 2.0.13

Because that is the newest version on nuget.org. It cannot be moved to 3.0 before 3.0 ships, and
pointing it at 3.0 packages that do not exist yet would only break the build.

It is `net8.0` and references MediatR 12 for the same reason: those are what 2.0.13 was built against.
Nothing here should be modernised while the pin stands. The code is a snapshot of what 2.x users have,
and its value is that it still compiles against what they downloaded.

## Why it is outside the solution

It is not in `DDDToolkit.slnx`, so `dotnet build DDDToolkit.slnx` never touches it. That is deliberate.
A solution build would restore the published packages alongside the projects that generate the same
types, which is confusing at best, and the generators in a 2.x package are not the ones under
`Source/`. Build it on its own when you want to verify a release:

```bash
dotnet build Examples/DDDToolkit.NugetApi/DDDToolkit.NugetApi.csproj
```

## What has to happen after 3.0 ships

Someone has to decide, and nobody has. The options, in the order they are worth considering:

- **Repoint it at 3.0 and rewrite it.** Retarget `net10.0`, move to the 3.0 packages, drop MediatR for
  Mediator, and bring the code in line with the 3.0 API. That restores the thing this project is for,
  and it costs a rewrite because 3.0 is a breaking release.
- **Replace it with a smoke test in CI.** Pack the projects, install the packages into a throw-away
  project, build it, assert the generated types exist. That catches the same failures and does not
  need a hand-maintained application.
- **Delete it.** Its 2.x snapshot is in the history, and 2.x is no longer the version anyone installs.

Until that decision is made, leave the code alone.
