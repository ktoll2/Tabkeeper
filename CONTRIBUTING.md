# Contributing to Tabkeeper

Thank you for contributing. Please open an issue before starting substantial work so the proposed behavior and scope can be discussed.

## Development prerequisites

- .NET SDK 10.0 or later
- Visual Studio 2022 or 2026 with the Visual Studio extension development workload, for packaging and debugging the VSIX on Windows

`Tabkeeper.Vsix` targets .NET Framework 4.8 (Visual Studio loads in-process extensions on the Framework). Its reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies` package, so no separate 4.8 targeting pack is required; the 4.8 runtime is already part of Windows and every Visual Studio install. `Tabkeeper.Core` targets `netstandard2.0`/`net10.0` and the tests target `net10.0`.

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore Tabkeeper.slnx
dotnet build Tabkeeper.slnx --configuration Release --no-restore
dotnet test Tabkeeper.Core.Tests/Tabkeeper.Core.Tests.csproj --configuration Release --no-build --no-restore
```

The test project uses [xUnit v3](https://xunit.net/) on Microsoft.Testing.Platform; `global.json` opts `dotnet test` into that runner. You can also run the test project directly (`dotnet run --project Tabkeeper.Core.Tests`).

The VSIX packaging target runs on Windows. Its output is under `Tabkeeper.Vsix/bin/Release/` and is intentionally ignored by Git.

## Versioning and releases

There is no version to maintain. Every release build derives `YEAR.MONTH.DAY.<GitHub run number>`
and stamps it into `source.extension.vsixmanifest` and the assemblies, so package versions are always
valid and strictly increasing. Do not edit the `Version` attribute in the manifest by hand; its
committed value (`0.1.0`) is only used by local developer builds.

| Workflow | Trigger | Result |
| --- | --- | --- |
| `ci.yml` | any push or pull request | build + test on Windows; uploads the `.vsix` as a run artifact |
| `prerelease.yml` | push to the `prerelease` branch | GitHub pre-release `v<version>-pre` with the `.vsix` |
| `release.yml` | a commit touching extension source (`Tabkeeper.Core/**`, `Tabkeeper.Vsix/**`, `Tabkeeper.slnx`, `global.json`) lands on `main` | GitHub release `v<version>` with the `.vsix` and auto-generated notes |

Both release workflows create their tag automatically and attach a `Tabkeeper.vsix.sha256`
checksum file (the hash is also added to the notes). Documentation-only merges to `main` do
not release.

## Change guidelines

- Keep saved pin state, configuration, and logs out of the repository. They belong under `%LOCALAPPDATA%\Tabkeeper\`.
- Do not close documents, save documents, or modify document contents as part of pin synchronization.
- Add focused tests for Core behavior changes.
- Run the build and relevant tests before opening a pull request.
- Describe user-visible changes clearly in pull requests so GitHub can generate useful release notes.

## Pull requests

Describe the behavioral change, tests run, and any Visual Studio versions used for manual validation. Do not include unredacted repository paths, activity logs, credentials, or other personal information.
