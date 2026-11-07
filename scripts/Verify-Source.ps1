param([string]$ProjectRoot)
$ErrorActionPreference = 'Stop'
if (-not $ProjectRoot) { $ProjectRoot = Split-Path -Parent $PSScriptRoot }
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
. (Join-Path $PSScriptRoot 'SourceFiles.ps1')
foreach ($name in @('state','logs','recovery','analysis')) {
    if (Test-Path -LiteralPath (Join-Path $ProjectRoot $name)) { throw ('Private runtime directory in repository root: ' + $name) }
}
foreach ($file in @(Get-ChildItem -LiteralPath $ProjectRoot -Force -File)) {
    if ($file.Extension.ToLowerInvariant() -in @('.exe','.dll','.sys','.cat','.pdb','.zip','.log')) { throw ('Build output or recovery binary in repository root: ' + $file.Name) }
}
$files = @(Get-OpenTrackpadSourceFiles $ProjectRoot)
foreach ($file in $files) {
    if ([IO.Path]::GetExtension($file).ToLowerInvariant() -in @('.png','.ico')) { continue }
    $text = [IO.File]::ReadAllText($file)
    if ($text -match '(?i)[a-z]:[/\\]+Users[/\\]+') { throw ('Absolute user-machine path in source: ' + $file.Substring($ProjectRoot.Length + 1)) }
    if ([IO.Path]::GetExtension($file) -eq '.ps1') {
        $tokens = $null; $parseErrors = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file,[ref]$tokens,[ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw ($parseErrors | Out-String) }
    }
}
[pscustomobject]@{ success=$true; sourceFiles=$files.Count; privateStateIncluded=$false; proprietaryRecoveryIncluded=$false; absoluteUserPaths=$false } | ConvertTo-Json
