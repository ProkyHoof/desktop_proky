param([string]$Version = '1.1.0')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$temporary = Join-Path ([IO.Path]::GetFullPath($env:TEMP)) ('DesktopPet-package-test-' + [Guid]::NewGuid().ToString('N'))
$extracted = Join-Path $temporary 'extracted'
$installed = Join-Path $temporary (([string][char]0x684c + [char]0x5ba0) + ' Custom Folder')
$legacy = Join-Path $temporary 'legacy-profile'
$data = Join-Path $installed 'user-data'
[void](New-Item -ItemType Directory -Path $extracted -Force)
function Check([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Run-PackageScript([string]$Script, [string]$Arguments, [bool]$ExpectFailure = $false) {
    $stdout = Join-Path $temporary 'process.stdout.txt'
    $stderr = Join-Path $temporary 'process.stderr.txt'
    $result = Start-Process -FilePath 'powershell.exe' -ArgumentList ('-NoProfile -STA -ExecutionPolicy Bypass -File "' + $Script + '" ' + $Arguments) -Wait -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    if ($ExpectFailure) { Check ($result.ExitCode -ne 0) 'Unsafe operation unexpectedly succeeded.' }
    elseif ($result.ExitCode -ne 0) { throw ('Package operation failed: ' + [IO.File]::ReadAllText($stderr)) }
}
try {
    $setup = Join-Path $projectRoot "dist/DesktopPet-$Version-Setup.exe"
    $result = Start-Process -FilePath $setup -ArgumentList ('/Q /C /T:"' + $extracted + '"') -Wait -PassThru -WindowStyle Hidden
    Check ($result.ExitCode -eq 0) 'Installer extraction failed.'
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $extracted 'DesktopPet.zip'))
    try {
        foreach ($entry in $zip.Entries) {
            Check ($entry.FullName -notmatch '(?i)(ai_chat_config|keyboard_pet_config|input_bindings|user-data|data[/\\]calendar|previews|\.bak$)') ('Private or development file included: ' + $entry.FullName)
        }
        [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip,(Join-Path $temporary 'payload'))
    } finally { $zip.Dispose() }
    . (Join-Path $temporary 'payload/PackageSupport.ps1')
    $manifest = Read-PetManifest (Join-Path $temporary 'payload')
    Check ($manifest.version -eq $Version) 'Incorrect installer version.'
    try { [void](Resolve-PetInstallPath ([IO.Path]::GetPathRoot($temporary))); throw 'Root directory was accepted.' }
    catch { if ($_.Exception.Message -eq 'Root directory was accepted.') { throw } }
    $form = New-PetInstallForm $installed $true $false
    $form.Icon = [Drawing.Icon]::ExtractAssociatedIcon((Join-Path $temporary 'payload/DesktopPet.exe'))
    try {
        Check $form.Controls['DesktopShortcut'].Checked 'Desktop shortcut should be enabled by default.'
        $form.Show()
        [Windows.Forms.Application]::DoEvents()
        $preview = Join-Path $projectRoot 'build/installer-preview.png'
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $preview))
        $image = New-Object Drawing.Bitmap($form.Width,$form.Height)
        try { $form.DrawToBitmap($image,(New-Object Drawing.Rectangle(0,0,$form.Width,$form.Height))); $image.Save($preview) }
        finally { $image.Dispose() }
        $form.AcceptButton.PerformClick()
        Check ($form.Tag.InstallRoot -eq $installed) ('Directory selection handler failed. Expected: ' + $installed + '; actual: ' + $form.Tag.InstallRoot + '; result: ' + $form.DialogResult)
        Check $form.Tag.DesktopShortcut 'Desktop choice was lost.'
    } finally { $form.Dispose() }
    [void][IO.Directory]::CreateDirectory((Join-Path $legacy 'data/calendar/history/snapshot'))
    [IO.File]::WriteAllText((Join-Path $legacy 'keyboard_pet_config.json'),'{"size":252}')
    [IO.File]::WriteAllText((Join-Path $legacy 'ai_chat_config.json'),'{"protectedApiKey":"fixture-only"}')
    [IO.File]::WriteAllText((Join-Path $legacy 'data/calendar/2026-11-06.json'),'calendar-fixture')
    [IO.File]::WriteAllText((Join-Path $legacy 'data/calendar/2026-11-06.json.bak'),'backup-fixture')
    [IO.File]::WriteAllText((Join-Path $legacy 'data/calendar/history/snapshot/2026-11-06.json'),'history-fixture')
    $installer = Join-Path $extracted 'Install.ps1'
    $installArgs = '-Silent -NoRegistration -InstallRoot "' + $installed + '"'
    Run-PackageScript $installer ($installArgs + ' -ImportLegacyData -LegacyDataRoot "' + $legacy + '"')
    Check (Test-Path -LiteralPath (Join-Path $data '.legacy-migrated')) 'User data should be initialized inside the installation.'
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $writeRules = @((Get-Acl -LiteralPath $data).Access | Where-Object { $_.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -eq $currentSid -and ($_.FileSystemRights -band [Security.AccessControl.FileSystemRights]::Modify) -eq [Security.AccessControl.FileSystemRights]::Modify })
    Check ($writeRules.Count -gt 0) 'Installing user needs write access to personal data.'
    foreach ($file in Get-ChildItem -LiteralPath $legacy -Recurse -File) {
        $relative = $file.FullName.Substring($legacy.Length + 1)
        Check ((Get-FileHash -LiteralPath $file.FullName).Hash -eq (Get-FileHash -LiteralPath (Join-Path $data $relative)).Hash) 'Legacy import changed a personal file.'
    }
    [IO.File]::WriteAllText((Join-Path $data 'keyboard_pet_config.json'),'{"size":420}')
    [IO.File]::WriteAllText((Join-Path $installed 'unrelated.txt'),'Must survive uninstall')
    Run-PackageScript $installer $installArgs
    Check ([IO.File]::ReadAllText((Join-Path $data 'keyboard_pet_config.json')) -eq '{"size":420}') 'Upgrade overwrote user preferences.'
    $snapshots = @{}
    foreach ($file in Get-ChildItem -LiteralPath $data -Recurse -File -Force) { $snapshots[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    $links = @((Join-Path $temporary 'shortcuts/Desktop.lnk'),(Join-Path $temporary 'shortcuts/Start.lnk'))
    Set-PetShortcuts $installed $links
    $shell = New-Object -ComObject WScript.Shell
    foreach ($linkPath in $links) {
        $link = $shell.CreateShortcut($linkPath)
        Check ($link.TargetPath -eq (Join-Path $installed 'DesktopPet.exe')) 'Shortcut target differs from the custom directory.'
        Check ($link.WorkingDirectory -eq $installed) 'Shortcut working directory is incorrect.'
        Check ($link.IconLocation -eq ((Join-Path $installed 'DesktopPet.exe') + ',0')) 'Shortcut does not use the application icon.'
    }
    $foreignLink = $shell.CreateShortcut($links[0]); $foreignLink.TargetPath = Join-Path $env:WINDIR 'notepad.exe'; $foreignLink.Save()
    Remove-PetShortcuts $installed $links
    Check (Test-Path -LiteralPath $links[0]) 'Uninstall deleted a shortcut belonging to another target.'
    Check (-not (Test-Path -LiteralPath $links[1])) 'Owned shortcut was not removed.'
    $uninstaller = Join-Path $installed 'Uninstall.ps1'
    $manifestPath = Join-Path $installed $PetManifestName
    $originalManifest = [IO.File]::ReadAllText($manifestPath)
    $corrupted = $originalManifest | ConvertFrom-Json
    $corrupted.files += '..\outside.txt'
    [IO.File]::WriteAllText($manifestPath,($corrupted | ConvertTo-Json -Depth 3))
    Run-PackageScript $uninstaller '-Silent -NoRegistration -KeepUserData' $true
    Check (Test-Path -LiteralPath (Join-Path $installed 'DesktopPet.exe')) 'Invalid manifest caused a partial uninstall.'
    [IO.File]::WriteAllText($manifestPath,$originalManifest)
    Run-PackageScript $uninstaller '-Silent -NoRegistration -KeepUserData'
    Check (-not (Test-Path -LiteralPath (Join-Path $installed 'DesktopPet.exe'))) 'Program files remain after uninstall.'
    foreach ($file in $snapshots.Keys) { Check ((Get-FileHash -LiteralPath $file).Hash -eq $snapshots[$file]) 'Keep-data uninstall changed personal data.' }
    Check (Test-Path -LiteralPath (Join-Path $installed 'unrelated.txt')) 'Uninstall removed an unrelated file.'
    Run-PackageScript $installer $installArgs
    Run-PackageScript (Join-Path $installed 'Uninstall.ps1') '-Silent -NoRegistration -RemoveUserData'
    Check (-not (Test-Path -LiteralPath $data)) 'Remove-data uninstall left personal data behind.'
    Check (Test-Path -LiteralPath (Join-Path $installed 'unrelated.txt')) 'Remove-data uninstall removed an unrelated file.'
    Check (@(Get-ChildItem -LiteralPath $installed -Force).Count -eq 1) 'Unexpected program files remained.'
    Write-Output 'PASS: Unicode/custom directory UI, install-local data, legacy import, upgrade preservation, desktop/start shortcuts, manifest traversal protection, keep/delete-data uninstall and unrelated-file preservation.'
}
finally {
    $resolved = [IO.Path]::GetFullPath($temporary)
    $allowed = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\') + '\DesktopPet-package-test-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package test cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
