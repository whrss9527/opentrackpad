Set-StrictMode -Version 2
function Get-OpenTrackpadSourceFiles([string]$ProjectRoot) {
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\','/')
    $files = New-Object 'Collections.Generic.List[string]'
    foreach ($name in @('.gitignore','version.json','README.md','CONTRIBUTING.md','LICENSE.txt','THIRD-PARTY-NOTICES.md','Manage-Trackpad.ps1')) {
        $file = Join-Path $root $name
        if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw ('Missing repository file: ' + $name) }
        $files.Add($file)
    }
    foreach ($directory in @('src','scripts','tests','licenses','.github','docs')) {
        $path = Join-Path $root $directory
        if (-not (Test-Path -LiteralPath $path -PathType Container)) { continue }
        $entries = @(Get-Item -LiteralPath $path) + @(Get-ChildItem -LiteralPath $path -Recurse -Force)
        foreach ($entry in $entries) {
            if (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw ('Source packages cannot follow links: ' + $entry.FullName) }
            if ($entry.PSIsContainer) { continue }
            $relative = $entry.FullName.Substring($root.Length + 1).Replace('\','/')
            if ($entry.Extension.ToLowerInvariant() -eq '.png' -and $relative -notmatch '^docs/screenshots/[^/]+\.png$') { throw ('Screenshots must be under docs/screenshots: ' + $relative) }
            if ($relative -match '(?i)(^|/)(state|logs|recovery|analysis|build|artifacts|node_modules)(/|$)') { throw ('Private/generated directory inside source tree: ' + $relative) }
            if ($entry.Extension.ToLowerInvariant() -notin @('.cs','.ps1','.md','.json','.manifest','.ico','.svg','.yml','.yaml','.txt','.png')) { throw ('Unexpected source-package file: ' + $relative) }
            $files.Add($entry.FullName)
        }
    }
    $files | Sort-Object -Unique
}
