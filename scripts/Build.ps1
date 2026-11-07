param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
$source = Join-Path $project 'src'
if ($env:OS -ne 'Windows_NT') { throw 'Open Trackpad is built on Windows x64.' }
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) { throw 'The Windows .NET Framework x64 C# compiler was not found. Install .NET Framework 4.8.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$version = Get-Content -LiteralPath (Join-Path $project 'version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($version.version -notmatch '^\d+\.\d+\.\d+$' -or $version.assemblyVersion -ne ($version.version + '.0')) { throw 'version.json is inconsistent.' }
$assemblyInfo = Get-Content -LiteralPath (Join-Path $source 'AssemblyInfo.cs') -Raw -Encoding UTF8
if (-not $assemblyInfo.Contains('AssemblyFileVersion("' + $version.assemblyVersion + '")') -or -not $assemblyInfo.Contains('AssemblyInformationalVersion("' + $version.version + '")')) { throw 'Update AssemblyInfo.cs to match version.json.' }
[xml]$manifest = Get-Content -LiteralPath (Join-Path $source 'OpenTrackpad.manifest') -Raw -Encoding UTF8
if ($manifest.assembly.assemblyIdentity.version -ne $version.assemblyVersion) { throw 'Update the application manifest version to match version.json.' }
$shared = @('AssemblyInfo.cs','UiKit.cs','AppPaths.cs','UiPreferences.cs') | ForEach-Object { Join-Path $source $_ }
foreach ($file in $shared) { if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing shared source: ' + $file) } }
$common = @('/nologo','/utf8output','/target:winexe','/platform:x64','/optimize+','/codepage:65001',('/win32manifest:' + (Join-Path $source 'OpenTrackpad.manifest')),('/win32icon:' + (Join-Path $source 'ThreeFingerDrag.ico')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Management.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll','/reference:Accessibility.dll')
$app = Join-Path $OutputDirectory 'OpenTrackpad.exe'
$engine = @(Get-ChildItem -LiteralPath (Join-Path $source 'ThreeFingerDrag') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
& $compiler @common /main:ThreeFingerDrag.Program (('/out:' + $app)) @shared @engine
if ($LASTEXITCODE -ne 0) { throw 'OpenTrackpad.exe compilation failed.' }
$driverPanel = Join-Path $OutputDirectory 'OpenTrackpad.DriverPanel.exe'
& $compiler @common /main:TrackpadWindow (('/out:' + $driverPanel)) @shared (Join-Path $source 'OpenTrackpad.cs')
if ($LASTEXITCODE -ne 0) { throw 'OpenTrackpad.DriverPanel.exe compilation failed.' }
foreach ($binary in @($app,$driverPanel)) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($binary).FileVersion -ne $version.assemblyVersion) { throw ('Unexpected executable version: ' + $binary) }
}
Copy-Item -LiteralPath (Join-Path $project 'Manage-Trackpad.ps1') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $project 'version.json') -Destination $OutputDirectory -Force
[pscustomobject]@{ version=$version.version; output=$OutputDirectory; files=@(@($app,$driverPanel) | ForEach-Object { [pscustomobject]@{ name=[IO.Path]::GetFileName($_); bytes=(Get-Item -LiteralPath $_).Length; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } }) } | ConvertTo-Json -Depth 4
