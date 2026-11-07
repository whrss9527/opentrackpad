param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
& (Join-Path (Split-Path -Parent $PSScriptRoot) 'scripts\Verify.ps1') -OutputDirectory $OutputDirectory
