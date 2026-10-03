<#
.SYNOPSIS
    Builds, tests and packages WinStart. Used locally and by GitHub Actions, so both produce the same result.

.DESCRIPTION
    Output goes to .\artifacts:
        app\WinStart.exe                 the application
        WinStart-Setup-<version>.exe     the installer (also what the in-app updater downloads)
        SHA256SUMS.txt                   checksum of the installer

    The version comes from Directory.Build.props.

.EXAMPLE
    .\build\build.ps1
    .\build\build.ps1 -SkipInstaller      # no Inno Setup needed
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'

function Assert-ExitCode([string]$what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)." }
}

function Get-ProjectVersion {
    [xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
    $version = $props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw 'No <Version> found in Directory.Build.props.' }
    return ([string]$version).Trim()
}

function Find-InnoSetup {
    $command = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }
    throw 'Inno Setup 6 was not found. Install it with "winget install JRSoftware.InnoSetup", or pass -SkipInstaller.'
}

$version = Get-ProjectVersion
$numericVersion = ($version -split '[-+]')[0]   # installers need a purely numeric file version
Write-Host "Building WinStart $version ($Configuration)" -ForegroundColor Cyan

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }
New-Item -ItemType Directory -Path $artifacts | Out-Null

if (-not $SkipTests) {
    dotnet test (Join-Path $root 'WinStart.slnx') -c $Configuration --nologo
    Assert-ExitCode 'dotnet test'
}

$appDir = Join-Path $artifacts 'app'
dotnet publish (Join-Path $root 'src\WinStart\WinStart.csproj') -c $Configuration -o $appDir --nologo
Assert-ExitCode 'dotnet publish'

$exe = Join-Path $appDir 'WinStart.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe to exist after publishing." }

# Code signing hook: sign $exe here, before it is packed into the installer (see README, "Code signing").

if (-not $SkipInstaller) {
    $iscc = Find-InnoSetup
    & $iscc /Qp "/DAppVersion=$version" "/DAppNumericVersion=$numericVersion" "/DSourceDir=$appDir" "/O$artifacts" (Join-Path $root 'installer\WinStart.iss')
    Assert-ExitCode 'Inno Setup'

    $installerName = "WinStart-Setup-$version.exe"
    $installer = Join-Path $artifacts $installerName
    if (-not (Test-Path $installer)) { throw "Expected $installer to exist after packaging." }

    # Code signing hook: sign $installer here, before its checksum is taken.

    # sha256sum format: "<hash>  <file name>". The in-app updater verifies downloads against this file.
    $hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText((Join-Path $artifacts 'SHA256SUMS.txt'), "$hash  $installerName`n", (New-Object System.Text.UTF8Encoding $false))
}

Write-Host ''
Write-Host "Done. Artifacts in $artifacts" -ForegroundColor Green
Get-ChildItem $artifacts -File | ForEach-Object { '  {0,-34} {1,10:N0} bytes' -f $_.Name, $_.Length }
