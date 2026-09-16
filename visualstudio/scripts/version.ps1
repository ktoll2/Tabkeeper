#Requires -Version 7
<#
.SYNOPSIS
    Reads or stamps the project version. The repo-root VERSION file is the single source of
    truth; this script keeps the VSIX manifest's Identity/@Version in sync with it, since Visual
    Studio reads that attribute directly at build time. Shared by `make current-version` /
    `make stamp-version` and CI so both apply the exact same logic.
.PARAMETER Version
    When given, replaces the VERSION file's content with this value. Either way, the manifest is
    then stamped to match VERSION's (possibly just-updated) content and the resulting version is
    printed - so a plain read also self-heals the manifest if it had drifted, and the manifest's
    committed value never needs to be hand-edited.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$versionFile = Join-Path $PSScriptRoot '..' '..' 'VERSION'
$versionFile = (Resolve-Path $versionFile).Path
$manifest = Join-Path $PSScriptRoot '..' 'Tabkeeper.Vsix' 'source.extension.vsixmanifest'
$manifest = (Resolve-Path $manifest).Path

if ($Version) {
    Set-Content $versionFile -Value $Version -NoNewline
}

$currentVersion = (Get-Content $versionFile -Raw).Trim()

$manifestContent = Get-Content $manifest -Raw
if ($manifestContent -notmatch '<Identity\b[^>]*\bVersion="([^"]+)"') {
    throw "Could not find Identity/@Version in $manifest"
}

if ($Matches[1] -ne $currentVersion) {
    ($manifestContent -replace '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$currentVersion`${2}") |
        Set-Content $manifest -NoNewline
    [Console]::Error.WriteLine("Stamped version $currentVersion (VERSION and $manifest)")
}

Write-Output $currentVersion
