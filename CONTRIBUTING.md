# Contributing to Tabkeeper

Thank you for contributing. Please open an issue before starting substantial work so the proposed behavior and scope can be discussed.

## Development prerequisites

- .NET SDK 10.0 or later
- Visual Studio 2022 or 2026 with the Visual Studio extension development workload, for packaging and debugging the VSIX on Windows

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

There is no version to maintain. The release workflows derive `YEAR.MONTH.DAY.<run number>`, stamp it
into `source.extension.vsixmanifest` and the assemblies, create the tag, and publish the GitHub
release. A code change merged to `main` publishes a release; a push to the `prerelease` branch
publishes a preview. Do not edit the `Version` attribute in `source.extension.vsixmanifest` by hand -
its committed value (`0.1.0`) is only used by local developer builds.

## Change guidelines

- Keep saved pin state, configuration, and logs out of the repository. They belong under `%LOCALAPPDATA%\Tabkeeper\`.
- Do not close documents, save documents, or modify document contents as part of pin synchronization.
- Add focused tests for Core behavior changes.
- Run the build and relevant tests before opening a pull request.
- Describe user-visible changes clearly in pull requests so GitHub can generate useful release notes.

## Pull requests

Describe the behavioral change, tests run, and any Visual Studio versions used for manual validation. Do not include unredacted repository paths, activity logs, credentials, or other personal information.
