# Contributing to Tabkeeper

Thank you for contributing. Please open an issue before starting substantial work so the proposed
behavior and scope can be discussed. Say which platform — Visual Studio, VS Code/VSCodium, or both
— your issue or change applies to.

This repository holds two independent implementations of the same idea: [`visualstudio/`](visualstudio)
(.NET/VSIX, for Visual Studio) and [`vscode/`](vscode) (TypeScript, for VS Code/VSCodium). Pick the
section below for whichever you're touching.

## Visual Studio (`visualstudio/`)

### Prerequisites

- .NET SDK 10.0 or later
- Visual Studio 2022 or 2026 with the Visual Studio extension development workload, for packaging
  and debugging the VSIX on Windows

`Tabkeeper.Vsix` targets .NET Framework 4.8 (Visual Studio loads in-process extensions on the
Framework). Its reference assemblies come from the `Microsoft.NETFramework.ReferenceAssemblies`
package, so no separate 4.8 targeting pack is required; the 4.8 runtime is already part of Windows
and every Visual Studio install. `Tabkeeper.Core` targets `netstandard2.0`/`net10.0` and the tests
target `net10.0`.

### Build and test

Run from `visualstudio/`:

```powershell
make test
```

or the equivalent commands directly:

```powershell
dotnet restore Tabkeeper.slnx
dotnet build Tabkeeper.slnx --configuration Release --no-restore
dotnet test Tabkeeper.Core.Tests/Tabkeeper.Core.Tests.csproj --configuration Release --no-build --no-restore
```

The test project uses [xUnit v3](https://xunit.net/) on Microsoft.Testing.Platform; `global.json`
opts `dotnet test` into that runner. You can also run the test project directly (`dotnet run
--project Tabkeeper.Core.Tests`). The VSIX packaging target runs on Windows; its output is under
`Tabkeeper.Vsix/bin/Release/` and is intentionally ignored by Git.

### Versioning and releases

Tabkeeper uses semantic versioning. The single source of truth for both platforms is the
repo-root [`VERSION`](../VERSION) file (`MAJOR.MINOR.PATCH`) — bump it by hand as part of the
change that should ship as a release:

- **PATCH** for a bug fix with no behavior change beyond the fix.
- **MINOR** for a backwards-compatible feature or setting addition.
- **MAJOR** for a breaking change (a setting or branch-rule format changes incompatibly, a command
  is removed, etc.).

`visualstudio/scripts/version.ps1` keeps `source.extension.vsixmanifest`'s `Identity/@Version` in
sync with `VERSION` automatically (it self-heals whenever `make current-version`/`make build`
runs, so the manifest's committed value never needs hand-editing), and `vscode/Makefile`'s
`package` target passes `VERSION`'s value straight into `vsce package <version>`, which updates
`vscode/package.json` and `package-lock.json` itself.

Releasing is manual: run **Release** (`.github/workflows/release.yml`) from the Actions tab
(`workflow_dispatch`) and choose a `release` input. This one workflow builds *both* platforms in
parallel — Visual Studio through `visualstudio/Makefile`, VS Code through `vscode/Makefile` — and
only publishes once *both* builds succeed, so a failure in either one never leaves a release with
just one platform's asset attached:

| `release` input | Result |
| --- | --- |
| `none` (default) | build + test both platforms; uploads each `.vsix` as a run artifact, publishes nothing |
| `prerelease` | also publishes a GitHub pre-release tagged `v<version>-pre.<run>` with both `.vsix` files, once both builds succeed. `VERSION` isn't required to change per run — the run number is appended to the *tag* for uniqueness, but the packaged version itself is never modified. |
| `release` | also publishes a GitHub release tagged `v<version>` with both `.vsix` files, once both builds succeed. **Fails the build** if `VERSION` wasn't increased since the last `v<major>.<minor>.<patch>` release tag — bump it before running, not after. |

Both `.vsix` files ship under the same tag/version, computed once from `VERSION`. The release
notes list each `.vsix`'s SHA-256 checksum inline; no separate `.sha256` file is attached. Nothing
pushes or releases automatically on a plain commit or pull request — every run is a deliberate,
manual trigger.

## VS Code (`vscode/`)

### Prerequisites

- Node.js 20 or later and npm
- VS Code or VSCodium, for manual testing (`F5` launches an Extension Development Host)

### Build and test

Run from `vscode/`:

```bash
make ci
make test
```

or the equivalent commands directly:

```bash
npm ci
npm test
```

`make test`/`npm test` compiles and runs the extension test suite (`vscode/src/test/`) against a
real VS Code test host — on Linux this needs a virtual display (`xvfb-run -a make test`), which is
what CI uses. Manual testing is also available via `F5` with `vscode/` open in VS Code. Package a
`.vsix` with `make package` (`npx --yes @vscode/vsce package`), which also stamps the package to
match the repo-root `VERSION` file's value (see "Versioning and releases" above — both platforms
share that one source of truth).

## Change guidelines

- Keep saved pin state, configuration, and logs out of the repository. Visual Studio stores them
  under `%LOCALAPPDATA%\Tabkeeper\`; VS Code/VSCodium under the extension's global storage
  directory (see [`vscode/README.md`](vscode/README.md)).
- Do not close documents, save documents, or modify document contents as part of pin
  synchronization, on either platform.
- Add focused tests for `Tabkeeper.Core` behavior changes.
- Run the build and relevant tests for whichever platform you touched before opening a pull
  request.
- Describe user-visible changes clearly in pull requests so GitHub can generate useful release
  notes.

## Pull requests

State which platform(s) a change affects, the behavioral change, tests run, and any Visual
Studio/VS Code versions used for manual validation. Do not include unredacted repository paths,
activity logs, credentials, or other personal information.
