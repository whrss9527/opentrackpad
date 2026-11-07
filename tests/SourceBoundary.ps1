param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $project 'scripts\SourceFiles.ps1')
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build\verify' }
$fixtures = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) ('source-boundaries-' + [guid]::NewGuid().ToString('N'))
$sources = @(Get-OpenTrackpadSourceFiles $project)
$passed = @()
foreach ($test in @('private-state','binary-in-source','absolute-user-path','screenshot-outside-docs')) {
    $root = Join-Path $fixtures $test
    foreach ($file in $sources) {
        $relative = $file.Substring($project.Length + 1)
        $destination = Join-Path $root $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $file -Destination $destination
    }
    if ($test -eq 'private-state') {
        New-Item -ItemType Directory -Path (Join-Path $root 'state') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $root 'state\sample.json'),'{}')
    } elseif ($test -eq 'binary-in-source') {
        [IO.File]::WriteAllBytes((Join-Path $root 'src\proprietary-recovery.exe'),[byte[]]@(77,90))
    } elseif ($test -eq 'absolute-user-path') {
        $fakePath = 'C:' + [char]92 + 'Users' + [char]92 + 'sample-profile' + [char]92 + 'private'
        [IO.File]::WriteAllText((Join-Path $root 'src\private-path.txt'),$fakePath)
    } else {
        [IO.File]::WriteAllBytes((Join-Path $root 'src\unexpected.png'),[byte[]]@(137,80,78,71))
    }
    $rejected = $false
    try { $null = & (Join-Path $project 'scripts\Verify-Source.ps1') -ProjectRoot $root } catch { $rejected = $true }
    if (-not $rejected) { throw ('Source boundary guard accepted ' + $test) }
    $passed += $test
}
[pscustomobject]@{ success=$true; rejected=$passed; realUserDataRead=$false } | ConvertTo-Json -Depth 3
