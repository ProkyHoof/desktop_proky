param(
    [switch]$Silent,
    [switch]$NoRegistration,
    [string]$InstallRoot,
    [switch]$NoDesktopShortcut,
    [switch]$ImportLegacyData,
    [string]$LegacyDataRoot = (Join-Path $env:LOCALAPPDATA 'DesktopPet')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[Windows.Forms.Application]::EnableVisualStyles()
$registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopPet'
$stage = $null
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage = Join-Path $env:TEMP ('DesktopPet-install-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'DesktopPet.zip'), $stage)
    . (Join-Path $stage 'PackageSupport.ps1')
    $manifest = Read-PetManifest $stage
    $previous = if (-not $NoRegistration) { Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue }
    if (-not $InstallRoot) {
        $InstallRoot = if ($previous.InstallLocation) { $previous.InstallLocation } else { Join-Path $env:LOCALAPPDATA 'Programs/DesktopPet' }
    }
    $createDesktop = -not $NoDesktopShortcut
    $importLegacy = $ImportLegacyData.IsPresent
    if (-not $Silent) {
        $legacyAvailable = Test-Path -LiteralPath $LegacyDataRoot -PathType Container
        $dialog = New-PetInstallForm $InstallRoot $legacyAvailable ($importLegacy -or $previous.DisplayVersion -like '1.0.*')
        $dialog.Icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $stage 'DesktopPet.exe'))
        try {
            if ($dialog.ShowDialog() -ne 'OK') { exit 0 }
            $InstallRoot = $dialog.Tag.InstallRoot
            $createDesktop = $dialog.Tag.DesktopShortcut
            $importLegacy = $dialog.Tag.ImportLegacy
        } finally { $dialog.Dispose() }
    }
    $InstallRoot = Resolve-PetInstallPath $InstallRoot
    $dataRoot = Get-PetChildPath $InstallRoot 'user-data'
    $oldManifest = $null
    if (Test-Path -LiteralPath (Join-Path $InstallRoot $PetManifestName)) { $oldManifest = Read-PetManifest $InstallRoot }
    $legacyInstallation = $previous.InstallLocation -and [IO.Path]::GetFullPath($previous.InstallLocation).TrimEnd('\') -eq $InstallRoot
    foreach ($relative in $manifest.files) {
        $target = Get-PetChildPath $InstallRoot $relative
        if ((Test-Path -LiteralPath $target) -and -not $oldManifest -and -not $legacyInstallation) { throw '所选目录含有同名文件，请选择空目录或原桌宠安装目录。' }
    }
    Test-PetWritableDirectory $InstallRoot
    Test-PetWritableDirectory $dataRoot
    Grant-PetDataAccess $dataRoot
    $selfTest = Start-Process -FilePath (Join-Path $stage 'DesktopPet.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) { throw '安装文件自检失败。' }
    if (-not $NoRegistration) {
        $stop = Start-Process -FilePath (Join-Path $stage 'DesktopPet.exe') -ArgumentList '--exit' -Wait -PassThru -WindowStyle Hidden
        if ($stop.ExitCode -ne 0) { throw '无法请求桌宠退出。' }
        foreach ($pet in @(Get-Process DesktopPet -ErrorAction SilentlyContinue)) {
            if (-not $pet.WaitForExit(7000)) { throw '请先关闭桌宠和日历，再安装。' }
        }
    }
    # A relocated upgrade keeps the old personal files as a recovery copy.
    if ($previous.InstallLocation -and $previous.InstallLocation -ne $InstallRoot) {
        $previousRoot = Resolve-PetInstallPath $previous.InstallLocation
        Copy-PetPersonalData (Join-Path $previousRoot 'user-data') $dataRoot
        Copy-PetPersonalData $previousRoot $dataRoot
    }
    if ($importLegacy) { Copy-PetPersonalData $LegacyDataRoot $dataRoot }
    Copy-PetPersonalData $InstallRoot $dataRoot
    foreach ($relative in $manifest.files) {
        $target = Get-PetChildPath $InstallRoot $relative
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        [IO.File]::Copy((Get-PetChildPath $stage $relative), $target, $true)
    }
    [IO.File]::Copy((Join-Path $stage $PetManifestName), (Join-Path $InstallRoot $PetManifestName), $true)
    $migration = Start-Process -FilePath (Join-Path $InstallRoot 'DesktopPet.exe') -ArgumentList '--migrate-data' -Wait -PassThru -WindowStyle Hidden
    if ($migration.ExitCode -ne 0) { throw '个人数据初始化失败。' }
    if (-not $NoRegistration) {
        $startLink = Join-Path ([Environment]::GetFolderPath('Programs')) '桌宠助手.lnk'
        $desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) '桌宠助手.lnk'
        $links = @($startLink)
        if ($createDesktop) { $links += $desktopLink }
        Set-PetShortcuts $InstallRoot $links
        # Remove only legacy links that still target this installation (or
        # the previous upgrade location), preserving unrelated shortcuts.
        $legacyLinks = @((Join-Path ([Environment]::GetFolderPath('Programs')) '白发小骑士.lnk'),(Join-Path ([Environment]::GetFolderPath('Desktop')) '白发小骑士.lnk'))
        Remove-PetShortcuts $InstallRoot $legacyLinks
        if ($previous.InstallLocation) { Remove-PetShortcuts $previous.InstallLocation $legacyLinks }
        if (-not $createDesktop) {
            Remove-PetShortcuts $InstallRoot @($desktopLink)
            if ($previous.InstallLocation) { Remove-PetShortcuts $previous.InstallLocation @($desktopLink) }
        }
        [void](New-Item -Path $registration -Force)
        $uninstall = '"' + (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') + '" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $InstallRoot 'Uninstall.ps1') + '"'
        $values = @{ DisplayName='桌宠助手'; DisplayVersion=$manifest.version; Publisher='ProkyHoof'; InstallLocation=$InstallRoot; DisplayIcon=(Join-Path $InstallRoot 'DesktopPet.exe'); UninstallString=$uninstall; QuietUninstallString=($uninstall + ' -Silent -KeepUserData') }
        foreach ($name in $values.Keys) { [void](New-ItemProperty -LiteralPath $registration -Name $name -Value $values[$name] -PropertyType String -Force) }
        foreach ($name in @('NoModify','NoRepair')) { [void](New-ItemProperty -LiteralPath $registration -Name $name -Value 1 -PropertyType DWord -Force) }
        $startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        if (Get-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -ErrorAction SilentlyContinue) {
            Set-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -Value ('"' + (Join-Path $InstallRoot 'DesktopPet.exe') + '"')
        }
    }
    if (-not $Silent) { [void][Windows.Forms.MessageBox]::Show(('安装完成。可从桌面或开始菜单启动。' + [Environment]::NewLine + '个人数据位置：' + $dataRoot),'桌宠助手') }
}
catch {
    if (-not $Silent) { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message, '安装失败', 'OK', 'Error') }
    throw
}
finally {
    if ($stage -and (Test-Path -LiteralPath $stage)) {
        $resolvedStage = [IO.Path]::GetFullPath($stage)
        $allowedPrefix = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\DesktopPet-install-'
        if (-not $resolvedStage.StartsWith($allowedPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe staging directory.' }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
