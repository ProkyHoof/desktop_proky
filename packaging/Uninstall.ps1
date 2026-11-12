param([switch]$Silent, [switch]$NoRegistration, [switch]$KeepUserData, [switch]$RemoveUserData)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
try {
    . (Join-Path $PSScriptRoot 'PackageSupport.ps1')
    $installRoot = Resolve-PetInstallPath $PSScriptRoot
    $manifest = Read-PetManifest $installRoot
    $dataRoot = Get-PetChildPath $installRoot 'user-data'
    if ($KeepUserData -and $RemoveUserData) { throw '不能同时选择保留和删除个人数据。' }
    $keep = -not $RemoveUserData
    if (-not $Silent) {
        $answer = [Windows.Forms.MessageBox]::Show(('是否保留个人配置和日历记录？' + [Environment]::NewLine + '是：卸载程序，保留 user-data。' + [Environment]::NewLine + '否：卸载程序并删除 user-data。' + [Environment]::NewLine + '取消：退出卸载。'),'卸载桌宠助手','YesNoCancel','Question','Button1')
        if ($answer -eq 'Cancel') { exit 0 }
        $keep = $answer -eq 'Yes'
    }
    if (-not $keep) { Assert-PetPlainTree $dataRoot }
    $app = Join-Path $installRoot 'DesktopPet.exe'
    if (-not $NoRegistration -and (Test-Path -LiteralPath $app)) {
        $stop = Start-Process -FilePath $app -ArgumentList '--exit' -Wait -PassThru -WindowStyle Hidden
        if ($stop.ExitCode -ne 0) { throw '无法请求桌宠退出。' }
        foreach ($pet in @(Get-Process DesktopPet -ErrorAction SilentlyContinue)) {
            if (-not $pet.WaitForExit(7000)) { throw '请先关闭桌宠和日历，再卸载。' }
        }
    }
    # Delete only files owned by the installation manifest, preserving unrelated files.
    Remove-PetProgramFiles $installRoot $manifest
    if (-not $keep -and (Test-Path -LiteralPath $dataRoot)) {
        # The canonical target is exactly user-data under the verified installation.
        if ($dataRoot -ne ([IO.Path]::GetFullPath($installRoot).TrimEnd('\') + '\user-data')) { throw 'Unsafe personal data target.' }
        Remove-Item -LiteralPath $dataRoot -Recurse -Force
    }
    if (-not $NoRegistration) {
        $startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        $startup = (Get-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -ErrorAction SilentlyContinue).DesktopPet_ProkyHoof
        if ($startup -eq ('"' + $app + '"')) { Remove-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof }
        foreach ($name in @('桌宠助手.lnk','白发小骑士.lnk')) {
            Remove-PetShortcuts $installRoot @((Join-Path ([Environment]::GetFolderPath('Programs')) $name),(Join-Path ([Environment]::GetFolderPath('Desktop')) $name))
        }
        $registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopPet'
        $registered = Get-ItemProperty -LiteralPath $registration -ErrorAction SilentlyContinue
        if ($registered.InstallLocation -eq $installRoot) { Remove-Item -LiteralPath $registration }
    }
    if (@(Get-ChildItem -LiteralPath $installRoot -Force).Count -eq 0) { Remove-Item -LiteralPath $installRoot -Force }
    if (-not $Silent) {
        $message = if ($keep) { '已卸载，个人数据保留在：' + $dataRoot } else { '已卸载并删除个人数据。' }
        [void][Windows.Forms.MessageBox]::Show($message,'桌宠助手')
    }
}
catch {
    if (-not $Silent) { [void][Windows.Forms.MessageBox]::Show($_.Exception.Message,'卸载失败','OK','Error') }
    throw
}
