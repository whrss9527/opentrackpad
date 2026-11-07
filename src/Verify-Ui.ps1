param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Verify-Ui.ps1') -OutputDirectory $OutputDirectory
