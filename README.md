# Tabkeeper

Tabkeeper is a Visual Studio extension that keeps pinned document tabs associated with the active Git branch. When you switch branches, it saves the pins for the branch you leave, unpins the repository documents currently open, and opens/pins the saved documents for the destination branch.

It never closes documents, saves documents, or changes document contents. Unsaved documents remain untouched apart from their pin state.

## Requirements

- Visual Studio 2022 or 2026 (17.x or 18.x)
- A solution located inside one Git repository

## Installation

Tabkeeper is distributed as a `.vsix` package on the [Releases page](https://github.com/ktoll2/Tabkeeper/releases).

1. On the latest release, download `Tabkeeper.vsix` from the **Assets** list.
2. Close Visual Studio, then double-click the downloaded file and confirm the VSIX Installer prompt.
   (Alternatively, from inside Visual Studio use **Extensions > Manage Extensions > Install from file**.)
3. Start Visual Studio. Tabkeeper loads automatically the first time a solution is opened; its
   commands appear under **Tools > Tabkeeper**.

To update, download the newer `.vsix` and install it over the existing one. To remove it, use
**Extensions > Manage Extensions > Installed**. Installs from Releases are not updated automatically.

### Verifying the download

Each release attaches `Tabkeeper.vsix.sha256` and lists the SHA-256 in the notes. Check it before
installing:

```powershell
(Get-FileHash Tabkeeper.vsix -Algorithm SHA256).Hash
```

When [signing is configured](#signing-the-vsix), release packages are digitally signed and Visual
Studio shows the publisher as **Tabkeeper** on install. The current certificate is self-signed, so
the publisher is also reported as unverified.

## Usage

1. Pin the documents useful for the branch you are working on.
2. Switch Git branches using Visual Studio or another Git client.
3. Tabkeeper records the outgoing branch's pinned documents and restores the destination branch's pins.

A branch with no saved pin set starts with no pinned repository tabs. Open documents are retained; only their pinned state changes.

Commands are available under **Tools > Tabkeeper**:

- **Enable Tabkeeper** - toggles automatic synchronization.
- **Sync Tabkeeper Now** - records current pins and reapplies the active branch set.
- **Clear This Branch's Tab Set** - deletes the active set and unpins documents in scope.
- **Manage Branch Rules** - opens the settings page (or `config.json` when the settings store is unavailable).
- **Export Tab Set** / **Import Tab Set to This Branch** - transfer a portable pin-set JSON file.
- **Copy Tab Set to Branch** - copies the current set to a named local branch.
- **Clean Up Missing Branches** - removes saved sets for local branches that no longer exist.
- **Open Saved Tab State** - opens the local pin-state data file.
- **Open Activity Log** - opens the local history and diagnostic log.

## How it works

Tabkeeper watches the active repository's `.git/HEAD` file with a filesystem watcher, backed by a
periodic poll as a fallback. When the checked-out branch changes it:

1. **Saves the outgoing branch's set** - the repository-relative paths of every pinned tab within the
   configured [scope](#configuration-reference) are written to the local state file under the branch
   you are leaving.
2. **Unpins** every in-scope document that is currently open.
3. **Restores the incoming branch's set** - for each saved path that still exists, the document is
   opened if necessary and pinned. Missing files are logged and skipped; if a checkout is still
   writing files, the restore is retried briefly. The document that was active before the switch is
   re-activated.

The first time a branch is seen with no saved set, the tabs you already have pinned are adopted as
that branch's initial set rather than unpinned. Detached HEAD states are keyed by commit id, and
[branch rules](#branch-rules) let several branches share one set or opt out entirely.

All state lives under `%LOCALAPPDATA%\Tabkeeper\` and is never written into the repository. The core
logic (`Tabkeeper.Core`) is Git-metadata-only - it reads `HEAD` and refs directly and never invokes
`git`.

## Configuration

Settings are edited in **Tools > Options > Tabkeeper** (the Visual Studio settings store):
enable/disable, preserve active document, managed scope, restore limit and delay, missing-file
behavior, detached-HEAD behavior, fallback polling interval, and **Branch rules (JSON)**. Changes are
picked up on the next synchronization (within the polling interval); use **Sync Tabkeeper Now** to
apply immediately.

If the Visual Studio settings service is unavailable, Tabkeeper falls back to a local file that has
the same fields plus a `rules` array, and that you can edit directly:

```text
%LOCALAPPDATA%\Tabkeeper\config.json
```

### Complete configuration example

```json
{
  "enabled": true,
  "pollingIntervalSeconds": 2,
  "maximumRestoredTabs": 25,
  "restoreDelayMilliseconds": 500,
  "preserveActiveDocument": true,
  "missingFileBehavior": "skip",
  "pinScope": "repository",
  "detachedHeadBehavior": "keepCommitPins",
  "rules": [
    {
      "pattern": "feature/*",
      "pinSetName": "feature-work",
      "enabled": true
    },
    {
      "pattern": "feature/login",
      "pinSetName": "login-work",
      "enabled": true
    },
    {
      "pattern": "release/*",
      "pinSetName": "release",
      "enabled": false
    }
  ]
}
```

### Configuration reference

| Property | Default | Valid values | Description |
| --- | --- | --- | --- |
| `enabled` | `true` | `true`, `false` | Enables automatic branch switching behavior. |
| `pollingIntervalSeconds` | `2` | `1`-`60` | Fallback Git `HEAD` check interval; file-system watching remains active. |
| `maximumRestoredTabs` | `25` | `1`-`100` | Maximum existing saved files to open and pin for a switch. |
| `restoreDelayMilliseconds` | `500` | `0`-`5000` | Wait time after a detected checkout before restoration. |
| `preserveActiveDocument` | `true` | `true`, `false` | Returns focus to the document active before restoration. |
| `missingFileBehavior` | `"skip"` | `"skip"`, `"showMessage"` | Silently skip missing files or show a message. Missing files are always logged. |
| `pinScope` | `"repository"` | `"repository"`, `"solutionDirectory"` | Manage files throughout the repository or only below the solution directory. |
| `detachedHeadBehavior` | `"keepCommitPins"` | `"keepCommitPins"`, `"clearPins"` | Keep per-commit sets when detached, or always clear pins. |
| `rules` | `[]` | array | Optional branch-pattern rules described below. |

Out-of-range numeric values are clamped to the listed ranges. Unknown string values fall back to the default behavior.

## Branch rules

Rules let multiple branches share a pin set or exclude matching branches from Tabkeeper. Each rule has:

- `pattern`: a case-insensitive branch glob. `*` matches any sequence and `?` matches one character.
- `pinSetName`: the local shared-set name.
- `enabled`: `true` shares the named set; `false` makes Tabkeeper leave matching branches unchanged.

The most-specific matching pattern wins. If no rule matches, a branch uses its own branch-name pin set.

For example, `feature/login` matches both `feature/*` and `feature/login`, but it uses `login-work` because that pattern is more specific. The disabled `release/*` rule prevents pin changes for all matching release branches.

Rules, state, configuration, exports, and activity logs are personal local data. They are never written into the Git repository or committed.

## Local files

Tabkeeper stores all of its local data under:

```text
%LOCALAPPDATA%\Tabkeeper\
```

| File | Purpose |
| --- | --- |
| `config.json` | All extension configuration and branch rules. |
| `pinned-tabs.json` | Saved pin sets, keyed by repository path and branch or shared rule-set name. |
| `activity.log` | Timestamped synchronization, restore, import/export, and cleanup records. |

For example, the full paths for a user named `Alice` are:

```text
C:\Users\Alice\AppData\Local\Tabkeeper\config.json
C:\Users\Alice\AppData\Local\Tabkeeper\pinned-tabs.json
C:\Users\Alice\AppData\Local\Tabkeeper\activity.log
```

### Saved pin-state example

`pinned-tabs.json` contains repository-relative paths. This keeps saved pins valid even when the repository is moved locally. A simplified example is:

```json
{
  "repositories": {
    "C:\\Source\\MyApp": {
      "branches": {
        "main": {
          "pinnedPaths": [
            "src/MyApp/Program.cs",
            "src/MyApp/appsettings.json"
          ]
        },
        "feature/login": {
          "pinnedPaths": [
            "src/MyApp/Login/LoginController.cs",
            "src/MyApp/Login/LoginService.cs"
          ]
        },
        "set/feature-work": {
          "pinnedPaths": [
            "src/MyApp/Shared/Result.cs"
          ]
        }
      }
    }
  }
}
```

Entries beginning with `set/` are shared pin sets selected by enabled branch rules. Detached-HEAD entries begin with `detached/` and are keyed by commit ID.

## Export format

Exports use a portable JSON file containing a format version, source branch metadata, and repository-relative pinned paths:

```json
{
  "formatVersion": 1,
  "sourceBranch": "feature/login",
  "pinnedPaths": [
    "src/Login/LoginController.cs",
    "src/Login/LoginService.cs"
  ]
}
```

Imports reject absolute paths and parent-directory traversal paths. Importing replaces the active branch or matching shared rule's current pin set.

## Cleanup and diagnostics

**Clean Up Missing Branches** only removes ordinary branch-specific pin sets whose local branches no longer exist. Shared rule sets and detached-commit sets are retained.

The activity log records saves, restores, adopted initial sets, skipped missing files, restore-limit exclusions, imports, exports, copies, cleanup results, unreadable-configuration warnings, and system events. It is available from **Open Activity Log**.

When a branch is seen for the first time and has no saved set, Tabkeeper records the tabs you already have pinned as that branch's initial set instead of unpinning them ("Adopted the currently pinned tabs as this branch's initial set."). If a branch switch is still writing files when pins are restored, the restore is retried a few times so tabs whose files arrive late are still pinned.

### Activity-log format

Every log line follows this format:

```text
[{timestamp}] [{level}] [{instance}] [{repository}] [{solution}] [{branch}] - {message}
```

For example:

```text
[2026-08-25 17:43:40.000 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [system] - Tabkeeper session started.
[2026-08-25 17:43:50.123 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [feature/login -> main] - Branch switch detected.
[2026-08-25 17:43:50.654 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [main] - Restored 4 pinned tabs.
[2026-08-25 17:43:50.700 -04:00] [WARN] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [main] - Missing: src/MyApp/Login/LegacyLogin.cs
[2026-08-25 17:44:02.000 -04:00] [WARN] [VS:18420/7eab71c2] [-] [-] [system] - No Git repository found for the active solution.
```

`level` is `INFO` for ordinary events, `WARN` for recoverable conditions (missing files, an unreadable `config.json`), `ERROR` for failed operations, and `DEBUG` for diagnostic detail such as a transient synchronization error that was retried automatically.

If `config.json` cannot be parsed, Tabkeeper keeps using the last valid settings (or defaults if it has never read a valid file) and writes a single `WARN` line naming the file. Fix the file and the next synchronization picks it up.

`instance` identifies the Visual Studio process and Tabkeeper session:

```text
VS:<process-id>/<session-id>
```

The process ID is the number before `/`; for example, `18420` in `VS:18420/7eab71c2`. To verify which active Visual Studio instance produced a line, open **Task Manager**, select the **Details** tab, and match that number to the `devenv.exe` PID column. The eight-character session ID is randomly generated when Tabkeeper loads, so it remains unique even if Windows later reuses a process ID.

`repository` is the Git-root directory name, `solution` is the solution filename, and `branch` is either the branch name, a branch transition such as `feature/login -> main`, or `system`. A `-` indicates that no repository or solution context was available. A system-wide mutex coordinates log writes so multiple Visual Studio instances can share the one file without interleaving lines.

Tabkeeper has no third-party runtime dependencies; the activity log is written with a small built-in file appender.

## Development and releases

See [CONTRIBUTING.md](CONTRIBUTING.md) for development prerequisites and build/test commands.

Versions are derived, never hand-maintained. Every release build is stamped
`YEAR.MONTH.DAY.<GitHub run number>` (for example `2026.9.7.14`) into the VSIX manifest and the
assemblies, so each package's version is valid and strictly increasing.

| Channel | Trigger | Result |
| --- | --- | --- |
| CI | Any push or pull request | [`ci.yml`](.github/workflows/ci.yml) builds and tests on Windows and uploads the `.vsix` as a build artifact. No release. |
| Prerelease | Push to the `prerelease` branch | [`prerelease.yml`](.github/workflows/prerelease.yml) publishes a GitHub **pre-release** `v<version>-pre` with the `.vsix` attached. |
| Release | A commit touching extension source lands on `main` | [`release.yml`](.github/workflows/release.yml) publishes the GitHub **release** `v<version>` with the `.vsix` attached and auto-generated notes. |

Merging a pull request that changes code under `Tabkeeper.Core/` or `Tabkeeper.Vsix/` publishes a
release; documentation-only merges do not. Pushing to the `prerelease` branch publishes a preview.
Both workflows create their tag automatically - you never tag by hand.

### Signing the VSIX

The release and prerelease workflows Authenticode-sign the `.vsix` when two repository secrets are
present; without them the build produces an unsigned package.

| Secret | Value |
| --- | --- |
| `VSIX_CERTIFICATE_BASE64` | the code-signing certificate as a base64-encoded `.pfx` |
| `VSIX_CERTIFICATE_PASSWORD` | the `.pfx` password |

Create the base64 with `[Convert]::ToBase64String([IO.File]::ReadAllBytes('cert.pfx')) > cert.b64`.
For a real publisher identity use a certificate from a CA or [Azure Trusted
Signing](https://learn.microsoft.com/azure/trusted-signing/); a self-signed certificate works for
testing but Visual Studio shows the publisher as untrusted.

To sign a local build, pass the certificate directly:

```powershell
dotnet build Tabkeeper.slnx -c Release `
  -p:VsixCertificatePath=cert.pfx -p:VsixCertificatePassword=*** `
  -p:VsixTimestampUrl=http://timestamp.digicert.com
```

Tabkeeper is available under the [MIT License](LICENSE). A Marketplace listing will be added when it is available.
