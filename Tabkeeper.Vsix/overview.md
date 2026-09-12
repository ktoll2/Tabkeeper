# Tabkeeper

Tabkeeper gives each Git branch its own set of pinned document tabs. Switch branches
and your pinned tabs switch with you: the tabs you had pinned on the branch you leave
are saved, and the ones saved for the branch you arrive on are opened and pinned.

Tabkeeper only changes which tabs are pinned. It never opens, closes, or saves files
beyond that, and never touches file contents.

Before switching branches, `GitRepository.cs` and `GitBranchReader.cs` are pinned:

![Pinned tabs before a branch switch](https://raw.githubusercontent.com/ktoll2/Tabkeeper/main/Tabkeeper.Vsix/Resources/Screenshot-PinnedTabs.png)

After switching branches, that branch's own saved pins take over:

![Pinned tabs after a branch switch](https://raw.githubusercontent.com/ktoll2/Tabkeeper/main/Tabkeeper.Vsix/Resources/Screenshot-PinnedTabs-AfterSwitch.png)

## How it works

1. Pin the tabs you want for the branch you are on.
2. Switch branches, in Visual Studio or any Git client.
3. Tabkeeper saves the old branch's pinned tabs and restores the new branch's.

The first time you visit a branch, whatever you already have pinned becomes that
branch's set, so nothing is lost. A branch you have never set up starts with no
pinned tabs. If a branch checkout is still writing files when tabs are restored,
Tabkeeper retries for a few seconds so late-arriving files are still pinned.

## Commands

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

Configurable under **Tools > Options > Tabkeeper**: master on/off switch, whether to
preserve the active document across a switch, managed scope (whole repository or just
the solution folder), a cap on restored tabs, a restore delay for slow checkouts,
missing-file behavior, detached HEAD behavior, and JSON branch rules that let related
branches (for example `feature/*`) share one tab set.

![Settings page](https://raw.githubusercontent.com/ktoll2/Tabkeeper/main/Tabkeeper.Vsix/Resources/Screenshot-Settings.png)

## Your data

Everything Tabkeeper stores is local to your machine and never committed:
settings and branch rules, saved tab sets per repository and branch, and a
timestamped activity log, all under `%LOCALAPPDATA%\Tabkeeper\`.

## Source

https://github.com/ktoll2/Tabkeeper
