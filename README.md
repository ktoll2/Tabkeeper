<img src="Tabkeeper.Vsix/Resources/Logo-256.png" align="left" width="120" alt="Tabkeeper logo">

# Tabkeeper

[![CI](https://github.com/ktoll2/Tabkeeper/actions/workflows/ci.yml/badge.svg)](https://github.com/ktoll2/Tabkeeper/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ktoll2/Tabkeeper?display_name=tag&logo=github)](https://github.com/ktoll2/Tabkeeper/releases/latest)
[![Prerelease](https://img.shields.io/github/v/release/ktoll2/Tabkeeper?include_prereleases&display_name=tag&label=prerelease)](https://github.com/ktoll2/Tabkeeper/releases)
[![License: MIT](https://img.shields.io/github/license/ktoll2/Tabkeeper)](LICENSE)

Tabkeeper is a Visual Studio extension that gives each Git branch its own set of pinned document
tabs. Switch branches and your pinned tabs switch with you: the tabs you had pinned on the branch
you leave are saved, and the ones saved for the branch you arrive on are opened and pinned.

Tabkeeper only changes which tabs are pinned. It never opens, closes, or saves files beyond that,
and never touches file contents.

## Requirements

- Visual Studio 2022 or 2026
- A solution inside a Git repository

## Install

1. Download `Tabkeeper.vsix` from the latest [release](https://github.com/ktoll2/Tabkeeper/releases).
2. Close Visual Studio, double-click the file, and confirm the installer.
3. Reopen Visual Studio. Tabkeeper starts automatically when you open a solution.

To update, install a newer `.vsix` over the old one. To remove it, use
**Extensions > Manage Extensions**.

Every release also publishes `Tabkeeper.vsix.sha256`. To check the download:

```powershell
(Get-FileHash Tabkeeper.vsix -Algorithm SHA256).Hash
```

## Using it

1. Pin the tabs you want for the branch you are on.
2. Switch branches, in Visual Studio or any Git client.
3. Tabkeeper saves the old branch's pinned tabs and restores the new branch's.

The first time you visit a branch, whatever you already have pinned becomes that branch's set, so
nothing is lost. A branch you have never set up starts with no pinned tabs. If a branch checkout is
still writing files when tabs are restored, Tabkeeper retries for a few seconds so late-arriving
files are still pinned.

Commands are under **Tools > Tabkeeper**:

| Command | Does |
| --- | --- |
| Enable Tabkeeper | Turn automatic switching on or off. |
| Sync Tabkeeper Now | Save the current pins and re-apply this branch's set immediately. |
| Clear This Branch's Tab Set | Forget this branch's saved tabs and unpin them. |
| Export Tab Set / Import Tab Set to This Branch | Move a branch's tab set between machines as a JSON file. |
| Copy Tab Set to Branch | Copy this branch's set to another local branch. |
| Clean Up Missing Branches | Drop saved sets for local branches that no longer exist. |
| Manage Branch Rules | Open the settings, where branch rules are edited. |
| Open Saved Tab State / Open Activity Log | Open the data and log files. |

## Settings

**Tools > Options > Tabkeeper**:

| Setting | Default | Notes |
| --- | --- | --- |
| Enable Tabkeeper | On | Master on/off switch. |
| Preserve active document | On | Return focus to the document you were on before the switch. |
| Managed scope | Repository | Manage tabs anywhere in the repository, or only under the solution folder. |
| Maximum restored tabs | 25 | Cap on how many tabs one switch opens and pins (1-100). |
| Restore delay (ms) | 500 | Wait after a checkout before restoring, so slow checkouts finish writing files (0-5000). |
| Missing file behavior | Skip | Skip a saved tab whose file is gone, or show a message. Either way it is logged. |
| Detached HEAD behavior | Keep commit pins | Keep a per-commit set while HEAD is detached, or just unpin. |
| Fallback polling interval (s) | 2 | How often the branch is re-checked if the file watcher misses a change (1-60). |
| Branch rules (JSON) | `[]` | See below. |

Changes take effect on the next branch check; **Sync Tabkeeper Now** applies them at once.

## Branch rules

By default each branch keeps its own tab set. Rules let branches share a set, or opt out entirely.
Set **Branch rules (JSON)** to an array of objects:

```json
[
  { "pattern": "feature/*", "pinSetName": "features", "enabled": true },
  { "pattern": "release/*", "pinSetName": "release",  "enabled": false }
]
```

- `pattern` - a case-insensitive branch glob (`*` matches any run of characters, `?` matches one).
- `pinSetName` - the shared set that matching branches use instead of their own.
- `enabled` - `true` shares the set; `false` leaves matching branches untouched.

The most specific pattern wins, so `feature/login` uses its own rule ahead of `feature/*`.

## Your data

Everything Tabkeeper stores is local to your machine and never committed:

```
%LOCALAPPDATA%\Tabkeeper\
  config.json       settings and branch rules
  pinned-tabs.json  saved tab sets, per repository and branch
  activity.log      what Tabkeeper did, timestamped
```

Paths in `pinned-tabs.json` are repository-relative, so moving the repository on disk does not break
saved sets. When the Visual Studio settings service is available, settings live there instead of in
`config.json`.

## Troubleshooting

**Open Activity Log** shows what happened: branch switches, restores, skipped files, and warnings.

- `Missing: src/App/Old.cs` - a saved tab's file does not exist on the branch, so it was skipped.
- `No Git repository found for the active solution` - the solution is not inside a Git working tree.
- `'...config.json' could not be read` - the file has a syntax error; the last good settings stay in
  effect until it is fixed.

If a setting does not seem to apply, run **Sync Tabkeeper Now**.

## Contributing

Build, test, and release details are in [CONTRIBUTING.md](CONTRIBUTING.md).

Tabkeeper is available under the [MIT License](LICENSE).
