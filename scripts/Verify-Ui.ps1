param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build\ui-check' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$data = Join-Path $OutputDirectory ('isolated-' + [guid]::NewGuid().ToString('N'))
$state = Join-Path $data 'state'
New-Item -ItemType Directory -Path $state -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $state 'gesture-options.json'),'{"enabled":false,"desktopHotkeysRequested":false,"detailed":false}',[Text.UTF8Encoding]::new($false))
$source = Join-Path $project 'src'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$shared = @('AssemblyInfo.cs','UiKit.cs','AppPaths.cs','UiPreferences.cs') | ForEach-Object { Join-Path $source $_ }
$engine = @(Get-ChildItem -LiteralPath (Join-Path $source 'ThreeFingerDrag') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
$binary = Join-Path $OutputDirectory 'UiSmoke.exe'
& $compiler /nologo /utf8output /target:winexe /platform:x64 /codepage:65001 /main:UiSmoke (('/out:' + $binary)) (('/win32manifest:' + (Join-Path $source 'OpenTrackpad.manifest'))) /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Accessibility.dll @shared @engine (Join-Path $project 'tests\UiSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Isolated UI test compilation failed.' }
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $binary
$info.Arguments = '"' + $data + '" "' + $OutputDirectory + '"'
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.EnvironmentVariables['OPENTRACKPAD_DATA_DIR'] = $data
$process = [Diagnostics.Process]::Start($info)
try {
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Isolated UI verification timed out.' }
    if ($process.ExitCode -ne 0) { throw ('UI verification failed; inspect ' + (Join-Path $OutputDirectory 'ui-smoke.error.txt')) }
} finally { $process.Dispose() }
Get-Content -LiteralPath (Join-Path $OutputDirectory 'ui-smoke.json') -Raw -Encoding UTF8
