<#
.SYNOPSIS
    Extracts the CHANGELOG.md section for one version, for use as the GitHub release description.

.DESCRIPTION
    The text becomes the "What's new" shown by WinStart's update dialog, so every release must
    have a "## [x.y.z]" section in CHANGELOG.md. The script fails if it is missing or empty.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$OutFile
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lines = Get-Content (Join-Path $root 'CHANGELOG.md') -Encoding UTF8

$heading = '## [' + $Version + ']'
$section = New-Object System.Collections.Generic.List[string]
$inside = $false
foreach ($line in $lines) {
    if ($line.StartsWith('## [')) {
        if ($inside) { break }
        if ($line.StartsWith($heading)) { $inside = $true }
        continue
    }
    if ($line -match '^\[[^\]]+\]:\s') { continue }   # link definitions at the end of the file
    if ($inside) { $section.Add($line) }
}

$text = ($section -join "`n").Trim()
if (-not $inside -or -not $text) {
    throw "CHANGELOG.md has no notes for version $Version. Add a '$heading - <date>' section before releasing."
}

[System.IO.File]::WriteAllText($OutFile, $text + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "Release notes for $Version written to $OutFile"
