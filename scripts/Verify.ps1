param([string]$OutputDirectory, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'build\verify' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$buildDirectory = Join-Path $project 'build'
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -OutputDirectory $buildDirectory }
$source = Join-Path $project 'src'
$compiler = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$shared = @('AssemblyInfo.cs','UiKit.cs','AppPaths.cs','UiPreferences.cs') | ForEach-Object { Join-Path $source $_ }
$engine = @(Get-ChildItem -LiteralPath (Join-Path $source 'ThreeFingerDrag') -Filter '*.cs' -File | Sort-Object Name | Select-Object -ExpandProperty FullName)
$harness = Join-Path $OutputDirectory 'CoreTests.exe'
& $compiler /nologo /utf8output /target:exe /platform:x64 /optimize+ /codepage:65001 /main:CoreTests (('/out:' + $harness)) /reference:System.dll /reference:System.Core.dll /reference:System.Management.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:Accessibility.dll @shared @engine (Join-Path $project 'tests\CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Deterministic test compilation failed.' }
$dataDirectory = Join-Path $OutputDirectory ('isolated-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
function Invoke-IsolatedTest([string]$Executable, [string[]]$Arguments, [string]$ResultPath) {
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $Executable
    $info.Arguments = (@($Arguments) | ForEach-Object { '"' + $_ + '"' }) -join ' '
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.WorkingDirectory = $OutputDirectory
    $info.EnvironmentVariables['OPENTRACKPAD_DATA_DIR'] = $dataDirectory
    $process = [Diagnostics.Process]::Start($info)
    try {
        if (-not $process.WaitForExit(20000)) { $process.Kill(); throw ('Isolated test timed out: ' + [IO.Path]::GetFileName($Executable)) }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $ResultPath -PathType Leaf)) { throw ('Isolated test failed; inspect ' + $ResultPath) }
    } finally { $process.Dispose() }
    $result = Get-Content -LiteralPath $ResultPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if (-not $result.success) { throw ('Test reported failure: ' + $ResultPath) }
    $result
}
$core = Invoke-IsolatedTest $harness @((Join-Path $OutputDirectory 'core-tests.json')) (Join-Path $OutputDirectory 'core-tests.json')
$programSource = Get-Content -LiteralPath (Join-Path $source 'ThreeFingerDrag\Program.cs') -Raw -Encoding UTF8
if (-not $programSource.Contains('"--self-test-runtime"')) { throw 'The isolated runtime test entry point has not been integrated yet.' }
$runtime = Invoke-IsolatedTest (Join-Path $buildDirectory 'OpenTrackpad.exe') @('--self-test-runtime',(Join-Path $OutputDirectory 'runtime-tests.json')) (Join-Path $OutputDirectory 'runtime-tests.json')
$driverPanel = Invoke-IsolatedTest (Join-Path $buildDirectory 'OpenTrackpad.DriverPanel.exe') @('--self-test',(Join-Path $OutputDirectory 'driver-panel-tests.json')) (Join-Path $OutputDirectory 'driver-panel-tests.json')
$tokens = $null; $syntaxErrors = $null
$maintenance = [Management.Automation.Language.Parser]::ParseFile((Join-Path $project 'Manage-Trackpad.ps1'),[ref]$tokens,[ref]$syntaxErrors)
if ($syntaxErrors.Count -gt 0) { throw ($syntaxErrors | Out-String) }
$parameters = @($maintenance.ParamBlock.Parameters | ForEach-Object { $_.Name.VariablePath.UserPath })
foreach ($name in @('Action','ResultPath','DataDirectory','PackageRoot')) { if ($name -notin $parameters) { throw ('Missing maintenance script parameter: ' + $name) } }
$sourceBoundaries = & (Join-Path $project 'tests\SourceBoundary.ps1') -OutputDirectory $OutputDirectory | ConvertFrom-Json
$summary = [pscustomobject]@{ success=$true; coreChecks=@($core.checks).Count; runtime=$runtime; driverPanel=$driverPanel; maintenanceParameters=$parameters; sourceBoundaries=$sourceBoundaries; isolatedData=$dataDirectory; hardwareRequired=$false; windowsSettingsModified=$false; globalInputInjected=$false }
$json = $summary | ConvertTo-Json -Depth 8
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'verification.json'),$json,[Text.UTF8Encoding]::new($false))
$json
