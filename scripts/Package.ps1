param([string]$OutputDirectory, [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
$project = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'SourceFiles.ps1')
& (Join-Path $PSScriptRoot 'Verify-Source.ps1') -ProjectRoot $project
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') }
$version = Get-Content -LiteralPath (Join-Path $project 'version.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $project 'artifacts' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$build = Join-Path $project 'build'
$runtime = @{}
foreach ($name in @('OpenTrackpad.exe','OpenTrackpad.DriverPanel.exe','Manage-Trackpad.ps1','version.json')) {
    $path = Join-Path $build $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw ('Missing build output: ' + $name) }
    $runtime[$name] = $path
}
foreach ($name in @('README.md','LICENSE.txt','THIRD-PARTY-NOTICES.md')) { $runtime[$name] = Join-Path $project $name }
foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $project 'licenses') -File)) { $runtime['licenses/' + $file.Name] = $file.FullName }
foreach ($name in @('OpenTrackpad.exe','OpenTrackpad.DriverPanel.exe')) {
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($runtime[$name]).FileVersion -ne $version.assemblyVersion) { throw ('Rebuild before packaging: ' + $name) }
}
$latestSource = (Get-ChildItem -LiteralPath (Join-Path $project 'src') -Recurse -File | Where-Object { $_.Extension -in @('.cs','.manifest','.ico') } | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1).LastWriteTimeUtc
foreach ($name in @('OpenTrackpad.exe','OpenTrackpad.DriverPanel.exe')) {
    if ((Get-Item -LiteralPath $runtime[$name]).LastWriteTimeUtc -lt $latestSource) { throw ('Source has changed; rebuild ' + $name) }
}
Add-Type -AssemblyName System.IO.Compression
foreach ($name in @('Manage-Trackpad.ps1','version.json')) {
    if ((Get-FileHash -LiteralPath $runtime[$name] -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $project $name) -Algorithm SHA256).Hash) { throw ('Rebuild updated support file: ' + $name) }
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Write-VerifiedZip([string]$Path,[hashtable]$Files,[string]$Prefix) {
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($name in @($Files.Keys | Sort-Object)) {
                if ($name -match '(^|/)\.\.(/|$)' -or [IO.Path]::IsPathRooted($name)) { throw ('Invalid package path: ' + $name) }
                $null = [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,$Files[$name],($Prefix + '/' + $name.Replace('\','/')),[IO.Compression.CompressionLevel]::Optimal)
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        if ($archive.Entries.Count -ne $Files.Count) { throw 'Package entry count mismatch.' }
        foreach ($name in $Files.Keys) {
            $entry = $archive.GetEntry($Prefix + '/' + $name.Replace('\','/'))
            if ($null -eq $entry) { throw ('Package entry missing: ' + $name) }
            $entryStream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
            try { $actual = [BitConverter]::ToString($hash.ComputeHash($entryStream)).Replace('-','') } finally { $entryStream.Dispose(); $hash.Dispose() }
            if ($actual -ne (Get-FileHash -LiteralPath $Files[$name] -Algorithm SHA256).Hash) { throw ('Package bytes differ: ' + $name) }
        }
    } finally { $archive.Dispose() }
}
$source = @{}
$root = [IO.Path]::GetFullPath($project).TrimEnd('\','/')
foreach ($file in @(Get-OpenTrackpadSourceFiles $project)) { $source[$file.Substring($root.Length + 1).Replace('\','/')] = $file }
$portableZip = Join-Path $OutputDirectory ('OpenTrackpad-' + $version.version + '-win-x64.zip')
$sourceZip = Join-Path $OutputDirectory ('OpenTrackpad-' + $version.version + '-source.zip')
Write-VerifiedZip $portableZip $runtime 'OpenTrackpad'
Write-VerifiedZip $sourceZip $source 'opentrackpad'
$hashes = @(@($portableZip,$sourceZip) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($_) })
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
[pscustomobject]@{ version=$version.version; sourceFiles=$source.Count; artifacts=@(@($portableZip,$sourceZip) | ForEach-Object { [pscustomobject]@{ path=$_; bytes=(Get-Item -LiteralPath $_).Length; sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() } }); privateDataIncluded=$false; driverBinariesIncluded=$false } | ConvertTo-Json -Depth 4
