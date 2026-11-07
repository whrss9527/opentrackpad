param([string]$OutputPath)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$directory = if ($OutputPath) { Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath)) } else { Join-Path $project 'build' }
& (Join-Path $project 'scripts\Build.ps1') -OutputDirectory $directory
if ($OutputPath -and [IO.Path]::GetFileName($OutputPath) -ne 'OpenTrackpad.DriverPanel.exe') {
    Copy-Item -LiteralPath (Join-Path $directory 'OpenTrackpad.DriverPanel.exe') -Destination $OutputPath -Force
}
