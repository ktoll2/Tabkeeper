# Tabkeeper (VS Code)

Tabkeeper gives each Git branch its own set of pinned editor tabs. Switch branches and your
pinned tabs switch with you — the tabs pinned on the branch you leave are saved, and the ones
saved for the branch you arrive on are opened and pinned.

Tabkeeper only changes which tabs are pinned. It never closes unpinned tabs, edits file contents,
or touches Git itself.

## Requirements

- VS Code 1.90 or later
- A workspace folder inside a Git repository

## Install

1. Download the `.vsix` from the latest [release](https://github.com/ktoll2/Tabkeeper/releases).
2. In VS Code or VSCodium, open the Extensions view, choose the `...` menu, and select
   **Install from VSIX...**. Or from a terminal:
   ```bash
   code --install-extension Tabkeeper.VSCode.vsix
   ```
   (use `codium` instead of `code` for VSCodium).

To update, install a newer `.vsix` over the old one. To remove it, use the Extensions view.

Every release's notes list the `.vsix`'s SHA-256 checksum. To check the download:

```bash
sha256sum Tabkeeper.VSCode.vsix
```

## Using it

1. Pin the tabs you want for the branch you're on (right-click a tab → **Pin**, or drag it to the
   pinned area).
2. Switch branches, in VS Code's Source Control view, the built-in Git extension, or any Git
   client.
3. Tabkeeper saves the old branch's pinned tabs and restores the new branch's.

The first time you visit a branch, whatever you already have pinned becomes that branch's set, so
nothing is lost. A branch you've never set up starts with no pinned tabs. If a branch checkout is
still writing files when tabs are restored, Tabkeeper retries for a few seconds so late-arriving
files are still pinned.

A status bar item shows whether Tabkeeper is on; click it to toggle. All commands are available
from the Command Palette (`Ctrl+Shift+P` / `Cmd+Shift+P`) under **Tabkeeper: ...**:

| Command | Does |
| --- | --- |
| Tabkeeper: Toggle Enabled | Turn automatic switching on or off. |
| Tabkeeper: Sync Now | Save the current pins and re-apply this branch's set immediately. |
| Tabkeeper: Clear This Branch's Tab Set | Forget this branch's saved tabs and unpin them. |
| Tabkeeper: Export Tab Set... / Import Tab Set to This Branch... | Move a branch's tab set between machines as a JSON file. |
| Tabkeeper: Copy Tab Set to Branch... | Copy this branch's set to another local branch. |
| Tabkeeper: Clean Up Missing Branches | Drop saved sets for local branches that no longer exist. |
| Tabkeeper: Manage Branch Rules | Open Settings, scrolled to the branch rules. |
| Tabkeeper: Open Saved Tab State / Open Activity Log | Open the data and log files. |

## Settings

Under **Settings → Extensions → Tabkeeper** (`tabkeeper.*`):

| Setting | Default | Notes |
| --- | --- | --- |
| `tabkeeper.enabled` | `true` | Master on/off switch. |
| `tabkeeper.preserveActiveDocument` | `true` | Return focus to the document you were on before the switch. |
| `tabkeeper.pinScope` | `repository` | Manage tabs anywhere in the repository, or only under this workspace folder (`workspaceFolder`). |
| `tabkeeper.maximumRestoredTabs` | `25` | Cap on how many tabs one switch opens and pins (1-100). |
| `tabkeeper.restoreDelayMilliseconds` | `500` | Wait after a checkout before restoring, so slow checkouts finish writing files (0-5000). |
| `tabkeeper.missingFileBehavior` | `skip` | Skip a saved tab whose file is gone, or also show a warning (`showMessage`). Either way it's logged. |
| `tabkeeper.detachedHeadBehavior` | `keepCommitPins` | Keep a per-commit set while HEAD is detached, or just unpin (`clearPins`). |
| `tabkeeper.pollingIntervalSeconds` | `2` | How often the branch is re-checked if the file watcher misses a change (1-60). |
| `tabkeeper.rules` | `[]` | See below. |

Changes take effect on the next branch check; **Tabkeeper: Sync Now** applies them at once.

## Branch rules

By default each branch keeps its own tab set. Rules let branches share a set, or opt out entirely.
Set `tabkeeper.rules` to an array of objects (editable directly in `settings.json`, or via the
Settings UI's array editor):

```json
[
  { "pattern": "feature/*", "pinSetName": "features", "enabled": true },
  { "pattern": "release/*", "pinSetName": "release", "enabled": false }
]
```

- `pattern` — a case-insensitive branch glob (`*` matches any run of characters, `?` matches one).
- `pinSetName` — the shared set that matching branches use instead of their own.
- `enabled` — `true` shares the set; `false` leaves matching branches untouched.

The most specific pattern wins, so `feature/login` uses its own rule ahead of `feature/*`.

## Your data

Everything Tabkeeper stores is local to your machine and never committed, under the extension's
global storage directory:

```
pinned-tabs.json  saved tab sets, per repository and branch
activity.log      what Tabkeeper did, timestamped
```

Use **Tabkeeper: Open Saved Tab State** / **Open Activity Log** to find and open them directly.
Paths in `pinned-tabs.json` are repository-relative, so moving the repository on disk doesn't
break saved sets.

## Multi-root workspaces

If a workspace has multiple folders, Tabkeeper runs one synchronizer per distinct Git repository
root (folders that resolve to the same repository share one synchronizer). Commands that act on
"the current branch" use the repository of the active editor's folder, falling back to the first
detected repository.

## How pinning works in VS Code

VS Code's tab API only lets Tabkeeper toggle the pin state of the *active* tab in a group, so
restoring a branch's tabs briefly activates each one in turn to pin it, then restores your
previous active document afterward (unless `preserveActiveDocument` is off).

## Troubleshooting

**Tabkeeper: Open Activity Log** shows what happened: branch switches, restores, skipped files,
and warnings.

- `Missing: src/App/Old.ts` — a saved tab's file doesn't exist on the branch, so it was skipped.
- `No Git repository found for the active workspace` — no workspace folder is inside a Git working
  tree.

If a setting doesn't seem to apply, run **Tabkeeper: Sync Now**.

## Build from source

Prerequisites: Node.js 20 or later and npm.

A Makefile wraps the build - run `make help` from this directory to list targets:

```bash
make ci
make package
```

`make ci` installs dependencies reproducibly from the lockfile (use `make install` instead if
you're adding or updating a dependency); `make package` compiles and produces a `.vsix`. Other
targets include `make build`, `make watch`, `make current-version`, `make run` (launch an
Extension Development Host from the CLI), and `make clean`.

Or run the equivalent commands directly:

```bash
npm install
npm run compile
npx --yes @vscode/vsce package
```

Press `F5` (with this folder open in VS Code) to launch an Extension Development Host for manual
testing instead of packaging.

## Contributing

Build, test, and release details are in [CONTRIBUTING.md](../CONTRIBUTING.md).

Tabkeeper is available under the [MIT License](LICENSE).
