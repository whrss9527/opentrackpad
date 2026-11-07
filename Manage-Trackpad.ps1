param(
    [ValidateSet('Status','Install','Rollback')][string]$Action = 'Status',
    [string]$ResultPath,
    [string]$DataDirectory,
    [string]$PackageRoot
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
if (-not $DataDirectory) { $DataDirectory = if ($env:OPENTRACKPAD_DATA_DIR) { $env:OPENTRACKPAD_DATA_DIR } else { Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'OpenTrackpad' } }
if (-not $PackageRoot) { $PackageRoot = if ($env:OPENTRACKPAD_RESOURCES_DIR) { $env:OPENTRACKPAD_RESOURCES_DIR } else { $PSScriptRoot } }
$rootPath = [IO.Path]::GetFullPath($PackageRoot)
$dataRoot = [IO.Path]::GetFullPath($DataDirectory)
$statePath = Join-Path $dataRoot 'state'
New-Item -ItemType Directory -Path $statePath -Force | Out-Null
if (-not $ResultPath) { $ResultPath = Join-Path $statePath 'result.json' }
$script:rebootRequired = $false
$script:changed = $false
$operationMutex = $null
$operationLock = $false
$logPath = Join-Path $statePath 'operations.log'

function Log([string]$Message) {
    Add-Content -LiteralPath $logPath -Value ('{0:o} {1}' -f (Get-Date), $Message) -Encoding UTF8
}
function Write-Result($Value) {
    $parent = Split-Path -Parent ([IO.Path]::GetFullPath($ResultPath))
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $json = $Value | ConvertTo-Json -Depth 9
    $temp = $ResultPath + '.tmp'
    [IO.File]::WriteAllText($temp, $json, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $ResultPath -Force
}
function Assert-Admin {
    $principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw '安装和恢复驱动需要 Windows 管理员权限。请从 OpenTrackpad 程序启动。'
    }
}
function Get-Targets {
    @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object {
        $_.InstanceId -match '(?i)(PID&0265|PID_0265)' -and
        $_.InstanceId -match '(?i)(0001004C|VID_05AC)'
    })
}
function Get-TargetDrivers {
    # The signed-driver WMI view can lag behind device rebinding. Read the
    # currently present devnode properties used by Device Manager instead.
    foreach ($device in @(Get-Targets)) {
        $values = @{}
        foreach ($property in @(Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName DEVPKEY_Device_DriverInfPath,DEVPKEY_Device_DriverProvider,DEVPKEY_Device_DriverVersion -ErrorAction SilentlyContinue)) {
            if ($property.PSObject.Properties['Data']) { $values[$property.KeyName] = $property.Data }
        }
        [pscustomobject]@{
            DeviceName = $device.FriendlyName; DeviceID = $device.InstanceId
            InfName = $values['DEVPKEY_Device_DriverInfPath']
            DriverProviderName = $values['DEVPKEY_Device_DriverProvider']
            DriverVersion = $values['DEVPKEY_Device_DriverVersion']
        }
    }
}
function Get-Status {
    $devices = @(Get-Targets)
    $drivers = @(Get-TargetDrivers)
    $free = @($drivers | Where-Object { $_.DriverProviderName -eq 'Bingxing Wang, Vito Plantamura' })
    $paid = @($drivers | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' })
    $primary = @($devices | Where-Object { $_.InstanceId -match '(?i)^HID\\.*PID&0265&COL01\\' })
    $freePrimary = @($free | Where-Object { $_.DeviceID -match '(?i)^HID\\.*PID&0265&COL01\\' })
    $active = 'system'
    if ($paid.Count) { $active = 'magic' }
    if ($freePrimary.Count -and -not $paid.Count) { $active = 'free' }
    if ($freePrimary.Count -and $paid.Count) { $active = 'mixed' }
    [pscustomobject]@{
        Success = $true; Action = 'Status'; Active = $active
        Connected = ($devices.Count -gt 0)
        PrimaryDetected = ($primary.Count -gt 0)
        Healthy = ($primary.Count -gt 0 -and @($primary | Where-Object { $_.Status -ne 'OK' }).Count -eq 0)
        Devices = @($devices | Select-Object Status,Class,FriendlyName,InstanceId)
        Drivers = @($drivers | Select-Object DeviceName,DriverProviderName,DriverVersion,InfName,DeviceID)
        CanRollback = (Test-Path -LiteralPath (Join-Path $statePath 'baseline.json'))
        Message = $(if (-not $devices.Count) { '暂未检测到 Magic Trackpad 2，请检查连接后刷新。' } elseif (-not $primary.Count) { '已检测到蓝牙连接接口，尚未确认触摸板输入接口。请唤醒设备后刷新；这不代表设备故障。' } else { '触摸板接口检测完成。驱动绑定状态不能代替实际手势体验验证。' })
        RebootRequired = $false
    }
}
function Invoke-Pnp([string[]]$PnpArgs) {
    Log ('pnputil ' + ($PnpArgs -join ' '))
    $text = & (Join-Path $env:SystemRoot 'System32\pnputil.exe') @PnpArgs 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $text) { Log ([string]$line) }
    if ($code -ne 0 -and $PnpArgs[0] -eq '/delete-driver' -and $PnpArgs[1] -match '^oem\d+\.inf$' -and
        -not (Test-Path -LiteralPath (Join-Path (Join-Path $env:SystemRoot 'INF') $PnpArgs[1]))) {
        Log 'OEM INF 已被原厂卸载器移除，不再重复删除。'
        return
    }
    if ($code -eq 3010 -or $code -eq 1641) { $script:rebootRequired = $true }
    elseif ($code -ne 0) { throw ('pnputil 失败，退出码 {0}。详情见 state\operations.log。' -f $code) }
}
function Assert-Package {
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -ne 'AMD64') {
        throw '此安装包仅适用于 Windows x64。'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $rootPath 'driver-manifest.json'))) { throw '未找到可选驱动包。请先阅读开源驱动说明；日常手势设置不需要重新安装驱动。' }
    $manifest = Get-Content -LiteralPath (Join-Path $rootPath 'driver-manifest.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.files) {
        if ($entry.name -notmatch '^[A-Za-z0-9_.-]+$') { throw '驱动清单文件名无效。' }
        $filePath = Join-Path (Join-Path $rootPath 'driver') $entry.name
        if ((Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash -ne $entry.sha256) {
            throw ('驱动文件校验失败：' + $entry.name)
        }
    }
    $signature = Get-AuthenticodeSignature -LiteralPath (Join-Path $rootPath 'driver\amtptpdevice.cat')
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Windows Hardware Compatibility Publisher,') {
        throw '驱动目录签名未通过 Windows 微软硬件发布者验证。'
    }
}
function Save-Baseline {
    $baselineFile = Join-Path $statePath 'baseline.json'
    $currentInstallKeys = @(Get-ItemProperty -Path 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -like 'Magic Utilities*' })
    if ($currentInstallKeys.Count -eq 0 -and (Test-Path -LiteralPath $baselineFile) -and -not (Get-Service -Name 'MagicUtilitiesService' -ErrorAction SilentlyContinue)) {
        $saved = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
        foreach ($originalInf in $saved.OriginalInfPaths) { if (-not (Test-Path -LiteralPath $originalInf)) { throw '恢复备份已丢失，不能继续先前迁移。' } }
        if (-not (Test-Path -LiteralPath $saved.OriginalInstallerPath)) { throw '恢复安装器已丢失。' }
        $externalMagic = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' -and $_.DeviceID -notmatch '(?i)(PID&0265|PID_0265)' })
        if ($externalMagic.Count) { throw '检测到其他 Magic Utilities 设备，停止继续迁移。' }
        Log '原厂卸载已完成且备份存在，继续先前迁移。'
        return
    }
    $paid = @(Get-TargetDrivers | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' })
    if (-not $paid.Count) { throw '没有找到可备份的 Magic Utilities 原驱动；停止自动切换。' }
    $service = Get-CimInstance Win32_Service -Filter "Name='MagicUtilitiesService'"
    if (-not $service) { throw '没有找到原驱动服务；停止自动切换。' }
    $installKey = @(Get-ItemProperty -Path 'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue | Where-Object { $_.PSObject.Properties['DisplayName'] -and $_.DisplayName -like 'Magic Utilities*' })
    if ($installKey.Count -ne 1 -or $installKey[0].'Inno Setup: Selected Components' -ne 'magictrackpad') {
        throw '检测到的 Magic Utilities 安装范围不只触摸板；停止自动卸载，避免影响其他设备。'
    }
    $originalInstaller = Join-Path $rootPath 'recovery\MagicUtilities-Setup-3.1.6.4.exe'
    if (-not (Test-Path -LiteralPath $originalInstaller)) { throw '缺少原软件恢复安装器。' }
    $installerSignature = Get-AuthenticodeSignature -LiteralPath $originalInstaller
    if ($installerSignature.Status -ne 'Valid' -or $installerSignature.SignerCertificate.Subject -notmatch 'CN=Magic Utilities Pty Ltd,') {
        throw '原软件恢复安装器签名验证失败。'
    }
    $otherDevices = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object {
        $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' -and $_.DeviceID -notmatch '(?i)(PID&0265|PID_0265)'
    })
    if ($otherDevices.Count) { throw '检测到其他设备使用 Magic Utilities，停止整包卸载。' }
    # Always validate current scope, including after a previous rollback.
    if (Test-Path -LiteralPath $baselineFile) {
        $existing = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
        foreach ($originalInf in $existing.OriginalInfPaths) {
            if (-not (Test-Path -LiteralPath $originalInf)) { throw '原驱动备份已丢失，停止切换。' }
        }
        return
    }
    $backupPath = Join-Path $statePath 'backup'
    New-Item -ItemType Directory -Path $backupPath -Force | Out-Null
    $oldInfNames = @($paid | Select-Object -ExpandProperty InfName -Unique)
    foreach ($infName in $oldInfNames) {
        if ($infName -notmatch '^oem\d+\.inf$') { throw '原驱动 INF 名称无效。' }
        Invoke-Pnp -PnpArgs @('/export-driver', $infName, $backupPath)
    }
    $exported = @(Get-ChildItem -LiteralPath $backupPath -Recurse -Filter 'MagicTrackpad.inf' -File)
    if (-not $exported.Count) { throw '原驱动导出验证失败；没有修改驱动。' }
    $originalApplication = Join-Path $env:ProgramFiles 'MagicUtilities'
    if (Test-Path -LiteralPath $originalApplication) { Copy-Item -LiteralPath $originalApplication -Destination (Join-Path $backupPath 'OriginalApplication') -Recurse -Force }
    $baseline = [pscustomobject]@{
        CapturedAt = (Get-Date).ToString('o'); OriginalInfNames = $oldInfNames
        OriginalServiceStartMode = $service.StartMode; OriginalServiceState = $service.State
        OriginalInfPaths = @($exported | Select-Object -ExpandProperty FullName)
        OriginalInstallerPath = $originalInstaller
        OriginalDrivers = @($paid | Select-Object DeviceName,DriverVersion,DeviceID)
    }
    $baseline | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $baselineFile -Encoding UTF8
    Log '已导出并记录原驱动与服务状态。'
}
function Restore-Original {
    $baselineFile = Join-Path $statePath 'baseline.json'
    if (-not (Test-Path -LiteralPath $baselineFile)) { throw '没有找到恢复备份。' }
    $baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
    foreach ($originalInf in $baseline.OriginalInfPaths) {
        if (-not (Test-Path -LiteralPath $originalInf)) { throw ('备份丢失：' + $originalInf) }
    }
    $gestureApp = Join-Path $PSScriptRoot 'OpenTrackpad.exe'
    if (-not (Test-Path -LiteralPath $gestureApp)) { $gestureApp = Join-Path $rootPath 'ThreeFingerDrag.exe' }
    if ((Test-Path -LiteralPath $gestureApp) -and (Test-Path -LiteralPath (Join-Path $statePath 'gestures-before.json'))) {
        $stopGesture = Start-Process -FilePath $gestureApp -ArgumentList '--shutdown' -WindowStyle Hidden -PassThru
        $stopGesture.WaitForExit()
        $gestureName = [IO.Path]::GetFileNameWithoutExtension($gestureApp)
        foreach ($process in @(Get-Process -Name $gestureName -ErrorAction SilentlyContinue)) { if ($process.Path -eq $gestureApp) { $null = $process.WaitForExit(2000) } }
        $gestureResult = Join-Path $statePath 'restored-gestures.json'
        $restoreGesture = Start-Process -FilePath $gestureApp -ArgumentList ('--restore "' + $gestureResult + '"') -WindowStyle Hidden -PassThru
        $restoreGesture.WaitForExit()
        Log ('三指手势恢复进程退出码：' + $restoreGesture.ExitCode)
    }
    $packagedInf = Join-Path $rootPath 'driver\AmtPtpDevice.inf'
    $packagedHash = (Get-FileHash -LiteralPath $packagedInf -Algorithm SHA256).Hash
    $packagedLength = (Get-Item -LiteralPath $packagedInf).Length
    # Include staged packages, even when no target device is currently bound.
    $freeInfNames = @(Get-ChildItem -LiteralPath (Join-Path $env:SystemRoot 'INF') -Filter 'oem*.inf' -File | Where-Object {
        $_.Length -eq $packagedLength -and (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash -eq $packagedHash
    } | Select-Object -ExpandProperty Name)
    foreach ($freeInf in $freeInfNames) {
        if ($freeInf -notmatch '^oem\d+\.inf$') { throw '免费驱动 INF 名称无效。' }
        $externalUsers = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object {
            $_.InfName -eq $freeInf -and $_.DeviceID -notmatch '(?i)(PID&0265|PID_0265)'
        })
        if ($externalUsers.Count) { throw '其他设备也在使用该免费驱动包，停止整包删除。' }
        $installedInf = Join-Path (Join-Path $env:SystemRoot 'INF') $freeInf
        if ((Get-FileHash -LiteralPath $installedInf -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath (Join-Path $rootPath 'driver\AmtPtpDevice.inf') -Algorithm SHA256).Hash) {
            throw '待移除 OEM 包与本次驱动 INF 不一致，停止恢复。'
        }
        Invoke-Pnp -PnpArgs @('/delete-driver', $freeInf, '/uninstall')
    }
    foreach ($originalInf in $baseline.OriginalInfPaths) { Invoke-Pnp -PnpArgs @('/add-driver', $originalInf, '/install') }
    if (-not (Get-Service -Name 'MagicUtilitiesService' -ErrorAction SilentlyContinue)) {
        $installer = Start-Process -FilePath $baseline.OriginalInstallerPath -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /COMPONENTS="magictrackpad"' -WindowStyle Hidden -PassThru
        $installer.WaitForExit()
        if ($installer.ExitCode -eq 3010) { $script:rebootRequired = $true }
        elseif ($installer.ExitCode -ne 0) { throw ('原软件安装器失败，退出码 ' + $installer.ExitCode) }
    }
    $modeMap = @{ Auto = 'Automatic'; Manual = 'Manual'; Disabled = 'Disabled' }
    $mode = $modeMap[[string]$baseline.OriginalServiceStartMode]
    if (-not $mode) { throw '备份中的服务启动类型无效。' }
    Set-Service -Name 'MagicUtilitiesService' -StartupType $mode
    if ($baseline.OriginalServiceState -eq 'Running' -and $mode -ne 'Disabled') { Start-Service -Name 'MagicUtilitiesService' }
    Invoke-Pnp -PnpArgs @('/scan-devices')
    $restored = Get-Status
    $versionMatches = @($restored.Drivers | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' -and $_.DriverVersion -eq '3.1.5.3' }).Count -gt 0
    if (($restored.Active -ne 'magic' -or -not $restored.Healthy -or -not $versionMatches) -and -not $script:rebootRequired) { throw '恢复命令已执行，但尚未确认原驱动版本与正常绑定。请检查日志或重新安装原安装包。' }
    Log '原驱动恢复流程完成。'
    return $restored
}

try {
    if ($Action -eq 'Status') { Write-Result (Get-Status); exit 0 }
    Assert-Admin
    $operationMutex = [Threading.Mutex]::new($false, 'Local\OpenTrackpad-Migration-PID0265')
    try { $operationLock = $operationMutex.WaitOne(0) }
    catch [Threading.AbandonedMutexException] { $operationLock = $true }
    if (-not $operationLock) { throw '已有安装或恢复任务正在运行，请等待它完成。' }
    if ($Action -eq 'Rollback') {
        $result = Restore-Original
        $result.Action = 'Rollback'; $result.RebootRequired = $script:rebootRequired
        $result.Message = '已执行原驱动恢复。请验证指针、点击和滚动；如提示重启，请保存工作后自行重启。'
        Write-Result $result
        exit 0
    }
    Assert-Package
    $before = Get-Status
    $currentPaidInfNames = @($before.Drivers | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' } | Select-Object -ExpandProperty InfName -Unique)
    if ($before.Active -eq 'free' -and $before.Healthy) {
        $before.Message = '免费驱动已绑定，无需再次切换。'
        Write-Result $before
        exit 0
    }
    if (-not $before.Connected) { throw '未检测到已连接的 Magic Trackpad 2（PID 0265）。' }
    # Stage the trusted package before detaching the current working driver.
    Save-Baseline
    Invoke-Pnp -PnpArgs @('/add-driver', (Join-Path $rootPath 'driver\AmtPtpDevice.inf'))
    $baseline = Get-Content -LiteralPath (Join-Path $statePath 'baseline.json') -Raw | ConvertFrom-Json
    $script:changed = $true
    $uninstallerPath = 'C:\Program Files\MagicUtilities\Uninstall\unins000.exe'
    if (Test-Path -LiteralPath $uninstallerPath) {
        $recoveryManifest = Get-Content -LiteralPath (Join-Path $rootPath 'recovery-manifest.json') -Raw | ConvertFrom-Json
        if ((Get-FileHash -LiteralPath $uninstallerPath -Algorithm SHA256).Hash -ne $recoveryManifest.uninstallerSha256) { throw '原厂卸载程序与已经验证签名的备份不一致。' }
        foreach ($gui in @(Get-Process -Name MagicTrackpadUtilities -ErrorAction SilentlyContinue)) { Stop-Process -Id $gui.Id -Force }
        Log '调用 Magic Utilities 自带卸载程序，仅卸载已记录的 magictrackpad 组件。'
        $uninstaller = Start-Process -FilePath $uninstallerPath -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru
        $uninstaller.WaitForExit()
        if ($uninstaller.ExitCode -eq 3010) { $script:rebootRequired = $true }
        elseif ($uninstaller.ExitCode -ne 0) { throw ('原软件卸载失败，退出码 ' + $uninstaller.ExitCode) }
    }
    $remainingPaid = @(Get-TargetDrivers | Where-Object { $_.DriverProviderName -eq 'Magic Utilities Pty Ltd' } | Select-Object -ExpandProperty InfName -Unique)
    foreach ($oldInf in $remainingPaid) {
        if ($oldInf -notin $currentPaidInfNames) { throw '卸载后检测到未记录的付费驱动，停止切换。' }
        Invoke-Pnp -PnpArgs @('/delete-driver', $oldInf, '/uninstall')
    }
    Invoke-Pnp -PnpArgs @('/scan-devices')
    foreach ($parentBefore in @($before.Devices | Where-Object { $_.InstanceId -match '(?i)^BTHENUM\\\{00001124-' })) {
        Invoke-Pnp -PnpArgs @('/restart-device', $parentBefore.InstanceId)
    }
    foreach ($parent in @(Get-Targets | Where-Object { $_.InstanceId -match '(?i)^BTHENUM\\\{00001124-' })) {
        $filterProperty = Get-PnpDeviceProperty -InstanceId $parent.InstanceId -KeyName DEVPKEY_Device_LowerFilters -ErrorAction SilentlyContinue
        if ($filterProperty -and $filterProperty.PSObject.Properties['Data'] -and @($filterProperty.Data) -contains 'MagicTrackpad') {
            throw '原 MagicTrackpad 过滤器仍存在，停止叠加新驱动。可能需要先重启。'
        }
    }
    Invoke-Pnp -PnpArgs @('/add-driver', (Join-Path $rootPath 'driver\AmtPtpDevice.inf'), '/install')
    $after = Get-Status
    for ($attempt = 0; $attempt -lt 3 -and $after.Active -ne 'free'; $attempt++) {
        Start-Sleep -Seconds 2
        $after = Get-Status
    }
    if (($after.Active -ne 'free' -or -not $after.Healthy) -and -not $script:rebootRequired) {
        throw '免费驱动尚未正常绑定，开始恢复原驱动。'
    }
    $after.Action = 'Install'; $after.RebootRequired = $script:rebootRequired
    $after.Message = '免费驱动切换流程已执行。请实测移动、点击、双指滚动、双指缩放及三指手势。'
    if ($script:rebootRequired) { $after.Message = 'Windows 要求重启，尚不能确认免费驱动已生效。请保存工作后自行重启，再刷新状态。' }
    Write-Result $after
    Log $after.Message
    exit 0
} catch {
    $failure = $_.Exception.Message
    Log ('ERROR: ' + $failure)
    $recovery = ''
    if ($Action -eq 'Install' -and $script:changed) {
        try { $null = Restore-Original; $recovery = ' 已执行原驱动恢复。' }
        catch { $recovery = ' 自动恢复未完成：' + $_.Exception.Message; Log $recovery }
    }
    Write-Result ([pscustomobject]@{ Success = $false; Action = $Action; Message = $failure + $recovery; RebootRequired = $script:rebootRequired })
    exit 1
} finally {
    if ($operationMutex) {
        if ($operationLock) { $operationMutex.ReleaseMutex() }
        $operationMutex.Dispose()
    }
}
