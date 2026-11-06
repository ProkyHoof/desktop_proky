param([switch]$Silent, [switch]$NoRegistration, [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Programs/DesktopPet'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
    $InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
    $normalRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/DesktopPet'))
    if ((-not $NoRegistration -and $InstallRoot -ne $normalRoot) -or (Split-Path -Leaf $InstallRoot) -ne 'DesktopPet') { throw 'Unexpected installation directory.' }
    if (-not $Silent -and [Windows.Forms.MessageBox]::Show(('安装白发小骑士到：' + [Environment]::NewLine + $InstallRoot + [Environment]::NewLine + '个人数据单独保存在 %LOCALAPPDATA%\DesktopPet。'), '安装桌宠', 'OKCancel', 'Information') -ne 'OK') { exit 0 }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stage = Join-Path $env:TEMP ('DesktopPet-install-' + [Guid]::NewGuid().ToString('N'))
    [IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'DesktopPet.zip'), $stage)
    $selfTest = Start-Process -FilePath (Join-Path $stage 'DesktopPet.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
    if ($selfTest.ExitCode -ne 0) { throw '安装文件自检失败。' }
    # Older installations may be running from a different directory.
    if (-not $NoRegistration) {
        $stop = Start-Process -FilePath (Join-Path $stage 'DesktopPet.exe') -ArgumentList '--exit' -Wait -PassThru -WindowStyle Hidden
        if ($stop.ExitCode -ne 0) { throw 'Could not request shutdown.' }
        foreach ($pet in @(Get-Process DesktopPet -ErrorAction SilentlyContinue)) {
            if (-not $pet.WaitForExit(7000)) { throw '请先关闭桌宠和日历，再安装。' }
        }
    }
    # Copy legacy data without overriding an existing user profile.
    $userRoot = Join-Path $env:LOCALAPPDATA 'DesktopPet'
    if (-not $NoRegistration -and (Test-Path -LiteralPath $InstallRoot) -and -not (Test-Path -LiteralPath (Join-Path $userRoot '.legacy-migrated'))) {
        [void](New-Item -ItemType Directory -Path $userRoot -Force)
        foreach ($name in @('ai_chat_config.json','ai_chat_config.json.bak','keyboard_pet_config.json','input_bindings.tsv')) {
            $source = Join-Path $InstallRoot $name
            $target = Join-Path $userRoot $name
            if ((Test-Path -LiteralPath $source) -and -not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $source -Destination $target }
        }
    }
    [void](New-Item -ItemType Directory -Path $InstallRoot -Force)
    foreach ($item in Get-ChildItem -LiteralPath $stage) { Copy-Item -LiteralPath $item.FullName -Destination $InstallRoot -Recurse -Force }
    if (-not $NoRegistration) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) '白发小骑士.lnk'))
        $shortcut.TargetPath = Join-Path $InstallRoot 'DesktopPet.exe'
        $shortcut.WorkingDirectory = $InstallRoot
        $shortcut.Save()
        $registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopPet'
        [void](New-Item -Path $registration -Force)
        $uninstall = '"' + (Join-Path $env:WINDIR 'System32/WindowsPowerShell/v1.0/powershell.exe') + '" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File "' + (Join-Path $InstallRoot 'Uninstall.ps1') + '"'
        $values = @{ DisplayName='白发小骑士 · 键鼠桌宠'; DisplayVersion='1.0.0'; Publisher='ProkyHoof'; InstallLocation=$InstallRoot; DisplayIcon=(Join-Path $InstallRoot 'DesktopPet.exe'); UninstallString=$uninstall; QuietUninstallString=($uninstall + ' -Silent') }
        foreach ($name in $values.Keys) { [void](New-ItemProperty -LiteralPath $registration -Name $name -Value $values[$name] -PropertyType String -Force) }
        foreach ($name in @('NoModify','NoRepair')) { [void](New-ItemProperty -LiteralPath $registration -Name $name -Value 1 -PropertyType DWord -Force) }
        $startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        if (Get-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -ErrorAction SilentlyContinue) {
            Set-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -Value ('"' + (Join-Path $InstallRoot 'DesktopPet.exe') + '"')
        }
    }
    if (-not $Silent) { [void][Windows.Forms.MessageBox]::Show('安装完成。请从开始菜单打开“白发小骑士”。','白发小骑士') }
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
