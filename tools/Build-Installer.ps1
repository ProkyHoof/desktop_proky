param([string]$Version = '1.2.0')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a numeric major.minor.patch version.' }
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Build.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem
$dist = Join-Path $projectRoot 'dist'
[void](New-Item -ItemType Directory -Path $dist -Force)
$work = Join-Path $projectRoot ('build/package-' + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $work 'payload'
$rigTarget = Join-Path $payload 'assets/keyboard-knight/rig'
$completed = $false
[void](New-Item -ItemType Directory -Path $rigTarget -Force)
try {
    Copy-Item -LiteralPath (Join-Path $projectRoot 'DesktopPet.exe') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/keyboard-knight/soft-style-arms.png') -Destination (Join-Path $payload 'assets/keyboard-knight')
    $rigSource = Join-Path $projectRoot 'assets/keyboard-knight/rig'
    $files = @('layers.tsv','hair-cap.png','hair-back-left.png','hair-back-right.png')
    foreach ($line in Get-Content -LiteralPath (Join-Path $rigSource 'layers.tsv')) {
        $file = ($line -split "`t")[1]
        if ($file.Contains('{00..32}')) {
            foreach ($frame in 0..32) { $files += $file.Replace('{00..32}', $frame.ToString('D2')) }
        } else { $files += $file }
    }
    foreach ($file in $files | Select-Object -Unique) {
        $target = Join-Path $rigTarget $file
        [void](New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force)
        Copy-Item -LiteralPath (Join-Path $rigSource $file) -Destination $target
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/Uninstall.ps1') -Destination $payload
    Copy-Item -LiteralPath (Join-Path $projectRoot 'packaging/PackageSupport.ps1') -Destination $payload
    $manifestFiles = @(Get-ChildItem -LiteralPath $payload -File -Recurse | ForEach-Object { $_.FullName.Substring($payload.Length + 1) })
    $manifestText = @{appId='DesktopPet.ProkyHoof';version=$Version;files=$manifestFiles} | ConvertTo-Json -Depth 3
    [IO.File]::WriteAllText((Join-Path $payload '.desktop-pet-install.json'), $manifestText, (New-Object Text.UTF8Encoding $false))
    $installText = [IO.File]::ReadAllText((Join-Path $projectRoot 'packaging/Install.ps1'))
    # Windows PowerShell 5.1 requires a BOM for Chinese text.
    [IO.File]::WriteAllText((Join-Path $work 'Install.ps1'), $installText, (New-Object Text.UTF8Encoding $true))
    foreach ($name in @('Uninstall.ps1','PackageSupport.ps1')) {
        $scriptPath = Join-Path $payload $name
        [IO.File]::WriteAllText($scriptPath, [IO.File]::ReadAllText($scriptPath), (New-Object Text.UTF8Encoding $true))
    }
    $check = Start-Process -FilePath (Join-Path $payload 'DesktopPet.exe') -ArgumentList '--self-test' -Wait -PassThru -WindowStyle Hidden
    if ($check.ExitCode -ne 0) { throw 'Packaged application self-test failed.' }
    $archive = Join-Path $work 'DesktopPet.zip'
    [IO.Compression.ZipFile]::CreateFromDirectory($payload, $archive)
    Copy-Item -LiteralPath $archive -Destination (Join-Path $dist "DesktopPet-$Version-portable.zip") -Force
    $setup = Join-Path $dist "DesktopPet-$Version-Setup.exe"
    if (Test-Path -LiteralPath $setup) { Remove-Item -LiteralPath $setup }
    $sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setup
FriendlyName=DesktopPet $Version
AppLaunched=powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Install.ps1
PostInstallCmd=<None>
AdminQuietInstCmd=powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Install.ps1 -Silent
UserQuietInstCmd=powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File Install.ps1 -Silent
SourceFiles=SourceFiles
[Strings]
FILE0="Install.ps1"
FILE1="DesktopPet.zip"
[SourceFiles]
SourceFiles0=$work\
[SourceFiles0]
%FILE0%=
%FILE1%=
"@
    $sedPath = Join-Path $work 'package.sed'
    [IO.File]::WriteAllText($sedPath, ($sed -replace '\r?\n', "`r`n") + "`r`n", [Text.Encoding]::Default)
    # Its legacy parser does not strip quotes around the directive filename.
    # A relative filename also supports project paths containing spaces.
    $builder = Start-Process -FilePath (Join-Path $env:WINDIR 'System32/iexpress.exe') -ArgumentList '/N package.sed /Q' -WorkingDirectory $work -Wait -PassThru -WindowStyle Hidden
    if ($builder.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $setup)) { throw "IExpress packaging failed ($($builder.ExitCode)). Diagnostics: $work" }
    Get-ChildItem -LiteralPath $dist -File | Where-Object { $_.Name -like "DesktopPet-$Version*" -and $_.Extension -ne '.txt' } |
        Get-FileHash -Algorithm SHA256 | ForEach-Object { $_.Hash + '  ' + (Split-Path -Leaf $_.Path) } |
        Set-Content -LiteralPath (Join-Path $dist "DesktopPet-$Version-SHA256.txt") -Encoding ASCII
    Write-Output "Installer: $setup"
    $completed = $true
}
finally {
    $resolved = [IO.Path]::GetFullPath($work)
    $allowed = [IO.Path]::GetFullPath((Join-Path $projectRoot 'build')).TrimEnd('\') + '\package-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe package cleanup path.' }
    if ($completed -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
