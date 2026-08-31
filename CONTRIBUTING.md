# Contributing to Branch Pins

Thank you for contributing. Please open an issue before starting substantial work so the proposed behavior and scope can be discussed.

## Development prerequisites

- .NET SDK 10.0 or later
- Visual Studio 2022 (17.x) with the Visual Studio extension development workload, for packaging and debugging the VSIX on Windows

## Build and test

Run these commands from the repository root:

```powershell
dotnet restore BranchPins.slnx
dotnet build BranchPins.slnx --configuration Release --no-restore
dotnet test tests/BranchPins.Core.Tests/BranchPins.Core.Tests.csproj --configuration Release --no-build --no-restore
```

The VSIX packaging target runs on Windows. Its output is under `src/BranchPins.Vsix/bin/Release/` and is intentionally ignored by Git.

## Change guidelines

- Keep saved pin state, configuration, and logs out of the repository. They belong under `%LOCALAPPDATA%\BranchPins\`.
- Do not close documents, save documents, or modify document contents as part of pin synchronization.
- Add focused tests for Core behavior changes.
- Run the build and relevant tests before opening a pull request.
- Describe user-visible changes clearly in pull requests so GitHub can generate useful release notes.

## Pull requests

Describe the behavioral change, tests run, and any Visual Studio versions used for manual validation. Do not include unredacted repository paths, activity logs, credentials, or other personal information.
