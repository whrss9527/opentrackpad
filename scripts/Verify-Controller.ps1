param([string]$OutputDirectory, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $project 'src'
$controllerSource = Get-Content -LiteralPath (Join-Path $source 'ThreeFingerDrag\AppController.cs') -Raw -Encoding UTF8
# Refuse to invoke the escalation negative test unless the reviewed guard is the first statement.
if ($controllerSource -notmatch 'void\s+RestartAsAdministrator\(\)\s*\{\s*if\s*\(\s*AppPaths\.IsIsolated\s*\)') { throw 'Refusing to exercise administrator restart: the reviewed isolation guard is missing.' }
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build\controller-check' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$production = Join-Path $project 'build\OpenTrackpad.exe'
$version = Get-Content -LiteralPath (Join-Path $project 'version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ([Diagnostics.FileVersionInfo]::GetVersionInfo($production).FileVersion -ne $version.assemblyVersion) { throw 'The production executable version is stale.' }
$latest = (Get-ChildItem -LiteralPath $source -Recurse -Filter '*.cs' -File | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
if ((Get-Item -LiteralPath $production).LastWriteTimeUtc -lt $latest) { throw 'Rebuild before running controller integration checks.' }
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$shared = @('AssemblyInfo.cs','UiKit.cs','AppPaths.cs','UiPreferences.cs') | ForEach-Object { Join-Path $source $_ }
$engine = @(Get-ChildItem -LiteralPath (Join-Path $source 'ThreeFingerDrag') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
$binary = Join-Path $OutputDirectory 'ControllerSmoke.exe'
& $compiler /nologo /utf8output /target:winexe /platform:x64 /codepage:65001 /main:ControllerSmoke (('/out:' + $binary)) (('/win32manifest:' + (Join-Path $source 'OpenTrackpad.manifest'))) /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Accessibility.dll @shared @engine (Join-Path $project 'tests\ControllerSmoke.cs')
if ($LASTEXITCODE -ne 0) { throw 'Controller integration harness compilation failed.' }
$data = Join-Path $OutputDirectory ('isolated-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $data -Force | Out-Null
$report = Join-Path $OutputDirectory 'controller-smoke.json'
$info = New-Object Diagnostics.ProcessStartInfo
$info.FileName = $binary
$info.Arguments = '"' + $report + '" "' + $production + '"'
$info.UseShellExecute = $false
$info.CreateNoWindow = $true
$info.EnvironmentVariables['OPENTRACKPAD_DATA_DIR'] = $data
$process = [Diagnostics.Process]::Start($info)
try {
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Isolated controller verification timed out.' }
    if ($process.ExitCode -ne 0) { throw ('Controller verification failed; inspect ' + $report) }
} finally { $process.Dispose() }
Get-Content -LiteralPath $report -Raw -Encoding UTF8
