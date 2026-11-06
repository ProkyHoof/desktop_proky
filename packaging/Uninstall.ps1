param([switch]$Silent, [switch]$NoRegistration)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$installRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$normalRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs/DesktopPet'))
if (-not $NoRegistration -and $installRoot -ne $normalRoot) { throw 'Unexpected installation directory.' }
if (-not (Test-Path -LiteralPath (Join-Path $installRoot 'DesktopPet.exe'))) { throw 'DesktopPet.exe is missing; no files were removed.' }
if (-not $Silent -and [Windows.Forms.MessageBox]::Show('卸载白发小骑士？个人配置和日历记录将保留。','卸载桌宠','YesNo','Question') -ne 'Yes') { exit 0 }
$app = Join-Path $installRoot 'DesktopPet.exe'
if (-not $NoRegistration) {
    $stop = Start-Process -FilePath $app -ArgumentList '--exit' -Wait -PassThru -WindowStyle Hidden
    if ($stop.ExitCode -ne 0) { throw 'Could not request shutdown.' }
    foreach ($pet in @(Get-Process DesktopPet -ErrorAction SilentlyContinue)) {
        if (-not $pet.WaitForExit(7000)) { throw '请先关闭桌宠和日历，再卸载。' }
    }
}
if (-not $NoRegistration) {
    $startupKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    $startup = (Get-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof -ErrorAction SilentlyContinue).DesktopPet_ProkyHoof
    if ($startup -eq ('"' + $app + '"')) { Remove-ItemProperty -LiteralPath $startupKey -Name DesktopPet_ProkyHoof }
    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) '白发小骑士.lnk'
    if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
    $registration = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopPet'
    if (Test-Path -LiteralPath $registration) { Remove-Item -LiteralPath $registration }
}
# Verify the absolute target before recursive deletion; personal data lives elsewhere.
if ($installRoot -eq [IO.Path]::GetPathRoot($installRoot) -or (Split-Path -Leaf $installRoot) -ne 'DesktopPet') { throw 'Unsafe uninstall target.' }
Remove-Item -LiteralPath $installRoot -Recurse -Force
if (-not $Silent) { [void][Windows.Forms.MessageBox]::Show('已卸载。个人数据仍保存在 %LOCALAPPDATA%\DesktopPet。','白发小骑士') }
