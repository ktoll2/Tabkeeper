#Requires -Version 7
<#
.SYNOPSIS
    Reads or stamps the project version. The repo-root VERSION file is the single source of
    truth; this script keeps the VSIX manifest's Identity/@Version in sync with it, since Visual
    Studio reads that attribute directly at build time. Shared by `make current-version` /
    `make stamp-version` and CI so both apply the exact same logic.
.PARAMETER Version
    When given, replaces the VERSION file's content and the manifest's version with this value.
    When omitted, prints the current version from the VERSION file.
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

    $manifestContent = Get-Content $manifest -Raw
    if ($manifestContent -notmatch '<Identity\b[^>]*\bVersion="([^"]+)"') {
        throw "Could not find Identity/@Version in $manifest"
    }

    ($manifestContent -replace '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$Version`${2}") |
        Set-Content $manifest -NoNewline
    Write-Host "Stamped version $Version (VERSION and $manifest)"
} else {
    Write-Output (Get-Content $versionFile -Raw).Trim()
}
