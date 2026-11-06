param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$temporary = Join-Path $env:TEMP ('DesktopPet-package-test-' + [Guid]::NewGuid().ToString('N'))
$extracted = Join-Path $temporary 'extracted'
$installed = Join-Path $temporary 'DesktopPet'
[void](New-Item -ItemType Directory -Path $extracted -Force)
try {
    $setup = Join-Path $projectRoot "dist/DesktopPet-$Version-Setup.exe"
    $result = Start-Process -FilePath $setup -ArgumentList ('/Q /C /T:"' + $extracted + '"') -Wait -PassThru -WindowStyle Hidden
    if ($result.ExitCode -ne 0) { throw 'Installer extraction failed.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $extracted 'DesktopPet.zip'))
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName -match '(?i)(ai_chat_config|keyboard_pet_config|input_bindings|data[/\\]calendar|previews|\.bak$)') { throw "Private or development file included: $($entry.FullName)" }
        }
    } finally { $zip.Dispose() }
    $installer = Join-Path $extracted 'Install.ps1'
    foreach ($iteration in 1..2) {
        $result = Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $installer + '" -Silent -NoRegistration -InstallRoot "' + $installed + '"') -Wait -PassThru -WindowStyle Hidden
        if ($result.ExitCode -ne 0) { throw "Install/upgrade failed on iteration $iteration." }
        if (-not (Test-Path -LiteralPath (Join-Path $installed 'assets/keyboard-knight/rig/layers.tsv'))) { throw 'Runtime assets missing.' }
    }
    $uninstaller = Join-Path $installed 'Uninstall.ps1'
    $result = Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $uninstaller + '" -Silent -NoRegistration') -Wait -PassThru -WindowStyle Hidden
    if ($result.ExitCode -ne 0 -or (Test-Path -LiteralPath $installed)) { throw 'Uninstall failed.' }
    Write-Output 'PASS: setup extraction, private-file exclusion, clean install, upgrade, runtime self-tests and uninstall in a temporary directory.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $allowed = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\DesktopPet-package-test-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
