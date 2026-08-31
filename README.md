# Branch Pins

Branch Pins is a Visual Studio 2022 extension that keeps pinned document tabs associated with the active Git branch. When you switch branches, it saves the pins for the branch you leave, unpins the repository documents currently open, and opens/pins the saved documents for the destination branch.

It never closes documents, saves documents, or changes document contents. Unsaved documents remain untouched apart from their pin state.

## Requirements

- Visual Studio 2022 (17.x)
- A solution located inside one Git repository

## Usage

1. Pin the documents useful for the branch you are working on.
2. Switch Git branches using Visual Studio or another Git client.
3. Branch Pins records the outgoing branch's pinned documents and restores the destination branch's pins.

A branch with no saved pin set starts with no pinned repository tabs. Open documents are retained; only their pinned state changes.

Commands are available under **Tools > Branch Pins**:

- **Enable Branch Pins** — toggles automatic synchronization.
- **Synchronize Branch Pins Now** — records current pins and reapplies the active branch set.
- **Clear Current Branch Pins** — deletes the active set and unpins documents in scope.
- **Manage Branch Rules** — opens `config.json`.
- **Export Current Pin Set** / **Import Pins to Current Branch** — transfer a portable pin-set JSON file.
- **Copy Current Pins to Branch** — copies the current set to a named local branch.
- **Clean Up Missing Branches** — removes saved sets for local branches that no longer exist.
- **Open Saved Pin State** — opens the local pin-state data file.
- **Open Activity Log** — opens the local history and diagnostic log.

## Configuration

All behavior is configured in the local file:

```text
%LOCALAPPDATA%\BranchPins\config.json
```

The extension creates this file with defaults on first load. Changes take effect on the next synchronization; use **Synchronize Branch Pins Now** to apply them immediately.

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
| `pollingIntervalSeconds` | `2` | `1`–`60` | Fallback Git `HEAD` check interval; file-system watching remains active. |
| `maximumRestoredTabs` | `25` | `1`–`100` | Maximum existing saved files to open and pin for a switch. |
| `restoreDelayMilliseconds` | `500` | `0`–`5000` | Wait time after a detected checkout before restoration. |
| `preserveActiveDocument` | `true` | `true`, `false` | Returns focus to the document active before restoration. |
| `missingFileBehavior` | `"skip"` | `"skip"`, `"showMessage"` | Silently skip missing files or show a message. Missing files are always logged. |
| `pinScope` | `"repository"` | `"repository"`, `"solutionDirectory"` | Manage files throughout the repository or only below the solution directory. |
| `detachedHeadBehavior` | `"keepCommitPins"` | `"keepCommitPins"`, `"clearPins"` | Keep per-commit sets when detached, or always clear pins. |
| `rules` | `[]` | array | Optional branch-pattern rules described below. |

Out-of-range numeric values are clamped to the listed ranges. Unknown string values fall back to the default behavior.

## Branch rules

Rules let multiple branches share a pin set or exclude matching branches from Branch Pins. Each rule has:

- `pattern`: a case-insensitive branch glob. `*` matches any sequence and `?` matches one character.
- `pinSetName`: the local shared-set name.
- `enabled`: `true` shares the named set; `false` makes Branch Pins leave matching branches unchanged.

The most-specific matching pattern wins. If no rule matches, a branch uses its own branch-name pin set.

For example, `feature/login` matches both `feature/*` and `feature/login`, but it uses `login-work` because that pattern is more specific. The disabled `release/*` rule prevents pin changes for all matching release branches.

Rules, state, configuration, exports, and activity logs are personal local data. They are never written into the Git repository or committed.

## Local files

Branch Pins stores all of its local data under:

```text
%LOCALAPPDATA%\BranchPins\
```

| File | Purpose |
| --- | --- |
| `config.json` | All extension configuration and branch rules. |
| `pinned-tabs.json` | Saved pin sets, keyed by repository path and branch or shared rule-set name. |
| `activity.log` | Timestamped synchronization, restore, import/export, and cleanup records. |

For example, the full paths for a user named `Alice` are:

```text
C:\Users\Alice\AppData\Local\BranchPins\config.json
C:\Users\Alice\AppData\Local\BranchPins\pinned-tabs.json
C:\Users\Alice\AppData\Local\BranchPins\activity.log
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

The activity log records saves, restores, skipped missing files, restore-limit exclusions, imports, exports, copies, cleanup results, and system events. It is available from **Open Activity Log**.

### Activity-log format

Every log line follows this format:

```text
[{timestamp}] [{level}] [{instance}] [{repository}] [{solution}] [{branch}] - {message}
```

For example:

```text
[2026-08-25 17:43:40.000 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [system] - Branch Pins session started.
[2026-08-25 17:43:50.123 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [feature/login -> main] - Branch switch detected.
[2026-08-25 17:43:50.654 -04:00] [INFO] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [main] - Restored 4 pinned tabs.
[2026-08-25 17:43:50.700 -04:00] [WARN] [VS:18420/7eab71c2] [MyApp] [MyApp.sln] [main] - Missing: src/MyApp/Login/LegacyLogin.cs
[2026-08-25 17:44:02.000 -04:00] [WARN] [VS:18420/7eab71c2] [-] [-] [system] - No Git repository found for the active solution.
```

`level` is `INFO` for ordinary events, `WARN` for recoverable conditions, and `ERROR` for failed operations.

`instance` identifies the Visual Studio process and Branch Pins session:

```text
VS:<process-id>/<session-id>
```

The process ID is the number before `/`; for example, `18420` in `VS:18420/7eab71c2`. To verify which active Visual Studio instance produced a line, open **Task Manager**, select the **Details** tab, and match that number to the `devenv.exe` PID column. The eight-character session ID is randomly generated when Branch Pins loads, so it remains unique even if Windows later reuses a process ID.

`repository` is the Git-root directory name, `solution` is the solution filename, and `branch` is either the branch name, a branch transition such as `feature/login -> main`, or `system`. A `-` indicates that no repository or solution context was available. Serilog's shared file sink coordinates writes from multiple Visual Studio instances.

## Third-party logging

Branch Pins uses [Serilog](https://serilog.net/) and `Serilog.Sinks.File` for activity logging. Their runtime assemblies are packaged in the VSIX; users do not need to install Serilog separately.

## Development and releases

See [CONTRIBUTING.md](CONTRIBUTING.md) for development prerequisites and build/test commands. GitHub Releases provide the release history.

Branch Pins is available under the [MIT License](LICENSE). Visual assets and Marketplace links will be added when they are available.
