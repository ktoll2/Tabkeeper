#Requires -Version 7
<#
.SYNOPSIS
    Reads or stamps the VSIX manifest's Identity Version. Shared by `make current-version` /
    `make stamp-version` and CI so both apply the exact same logic.
.PARAMETER Version
    When given, replaces the manifest's version with this value. When omitted, prints the current
    version.
#>
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot '..' 'Tabkeeper.Vsix' 'source.extension.vsixmanifest'
$manifest = (Resolve-Path $manifest).Path
$content = Get-Content $manifest -Raw

if ($content -notmatch '<Identity\b[^>]*\bVersion="([^"]+)"') {
    throw "Could not find Identity/@Version in $manifest"
}

if ($Version) {
    ($content -replace '(<Identity\b[^>]*\bVersion=")[^"]*(")', "`${1}$Version`${2}") |
        Set-Content $manifest -NoNewline
    Write-Host "Stamped VSIX version $Version"
} else {
    Write-Output $Matches[1]
}
