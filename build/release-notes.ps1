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

# Join lines that were wrapped by hand in CHANGELOG.md, so every paragraph and list item is a
# single line. GitHub renders both forms identically, and the update dialog (plain text) can
# then wrap the notes to its own width.
$joined = New-Object System.Collections.Generic.List[string]
$inCode = $false
foreach ($line in $section) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith('```')) { $inCode = -not $inCode; $joined.Add($line.TrimEnd()); continue }
    $previous = if ($joined.Count -gt 0) { $joined[$joined.Count - 1] } else { '' }
    $startsBlock = $trimmed -eq '' -or $trimmed -match '^(#|[-*+] |\d+\. |>|\|)'
    $canJoin = -not $inCode -and -not $startsBlock -and $previous.Trim() -ne '' -and -not $previous.TrimStart().StartsWith('#') -and -not $previous.TrimStart().StartsWith('```')
    if ($canJoin) { $joined[$joined.Count - 1] = $previous.TrimEnd() + ' ' + $trimmed }
    else { $joined.Add($line.TrimEnd()) }
}

$text = ($joined -join "`n").Trim()
if (-not $inside -or -not $text) {
    throw "CHANGELOG.md has no notes for version $Version. Add a '$heading - <date>' section before releasing."
}

[System.IO.File]::WriteAllText($OutFile, $text + "`n", (New-Object System.Text.UTF8Encoding $false))
Write-Host "Release notes for $Version written to $OutFile"
