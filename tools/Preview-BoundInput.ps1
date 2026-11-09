param([string]$Name = 'current', [switch]$KeepFrames, [switch]$StillsOnly, [switch]$Interaction, [switch]$MouseFeedback, [switch]$Helmet, [switch]$Music, [switch]$ArmoredMusic)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\preview'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Generated test and helper files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $cache; $env:TMP = $cache
if ($MouseFeedback -and $Name -eq 'current') { $Name = 'current-mouse-feedback' }
if ($Helmet -and $Name -eq 'current') { $Name = 'current-typing-helmet' }
if ($Music -and $Name -eq 'current') { $Name = 'current-music' }
if ($ArmoredMusic -and $Name -eq 'current') { $Name = 'current-armored-music' }
$helper = Join-Path $cache ('DesktopPet.BoundPreview.' + [Guid]::NewGuid().ToString('N') + '.exe')
$frameRoot = Join-Path $projectRoot ('assets/previews/' + $Name + '-frames')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:BoundPreview /optimize+ /codepage:65001 "/out:$helper" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll (Join-Path $projectRoot 'DesktopPet.cs') (Join-Path $projectRoot 'PetMenu.cs') (Join-Path $projectRoot 'PetWardrobeWindow.cs') (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $projectRoot 'InputFeedback.cs') (Join-Path $projectRoot 'TypingStatistics.cs') (Join-Path $projectRoot 'AssistantPaths.cs') (Join-Path $projectRoot 'PetAssistant.cs') (Join-Path $projectRoot 'PetAssistantWindow.cs') (Join-Path $projectRoot 'BoundPetScene.cs') (Join-Path $projectRoot 'LayeredPetRig.cs') (Join-Path $projectRoot 'MusicPlayback.cs') (Join-Path $projectRoot 'PetCalendar.cs') (Join-Path $projectRoot 'PetCalendarWorkspace.cs') (Join-Path $projectRoot 'PetChat.cs') (Join-Path $projectRoot 'PetVoice.cs') (Join-Path $projectRoot 'PetChatWindow.cs') (Join-Path $PSScriptRoot 'Render-BoundPreview.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Preview compilation failed.' }
    if ($ArmoredMusic) {
        & $helper $projectRoot $frameRoot --armored-music
        if ($LASTEXITCODE -ne 0) { throw 'Armored music preview rendering failed.' }
    }
    elseif ($Music) {
        & $helper $projectRoot $frameRoot --music
        if ($LASTEXITCODE -ne 0) { throw 'Music preview rendering failed.' }
    }
    elseif ($Helmet) {
        & $helper $projectRoot $frameRoot --helmet
        if ($LASTEXITCODE -ne 0) { throw 'Helmet preview rendering failed.' }
    }
    elseif ($MouseFeedback) {
        & $helper $projectRoot $frameRoot --mouse-feedback
        if ($LASTEXITCODE -ne 0) { throw 'Mouse feedback preview rendering failed.' }
    }
    elseif ($StillsOnly) {
        & $helper $projectRoot $frameRoot --stills
        if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed.' }
        return
    }
    elseif ($Interaction) { & $helper $projectRoot $frameRoot --interaction }
    else { & $helper $projectRoot $frameRoot }
    if ($LASTEXITCODE -ne 0) { throw 'Preview rendering failed.' }
    $encoder = [System.Windows.Media.Imaging.GifBitmapEncoder]::new()
    $files = @(Get-ChildItem -LiteralPath $frameRoot -Filter '*.png' | Sort-Object Name)
    foreach ($file in $files) {
        $stream = [System.IO.File]::OpenRead($file.FullName)
        try { $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($stream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat, [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)) }
        finally { $stream.Dispose() }
    }
    $gifPath = Join-Path $projectRoot ('assets/previews/' + $Name + '-demo.gif')
    $fileStream = [System.IO.File]::Create($gifPath)
    try { $encoder.Save($fileStream) } finally { $fileStream.Dispose() }
    # Preserve the encoder's image blocks; add a 50ms frame clock and looping.
    $bytes = [System.IO.File]::ReadAllBytes($gifPath)
    $header = 13
    if (($bytes[10] -band 128) -ne 0) { $header += 3 * [Math]::Pow(2, ($bytes[10] -band 7) + 1) }
    $output = [System.IO.MemoryStream]::new()
    try {
        $output.Write($bytes, 0, $header)
        $loop = [byte[]]@(33,255,11,78,69,84,83,67,65,80,69,50,46,48,3,1,0,0,0)
        $output.Write($loop, 0, $loop.Length)
        $position = $header; $count = 0
        while ($position -lt $bytes.Length) {
            $start = $position; $marker = $bytes[$position++]
            if ($marker -eq 59) { $output.WriteByte(59); break }
            if ($marker -eq 33) {
                $label = $bytes[$position++]
                while ($true) { $length = $bytes[$position++]; if ($length -eq 0) { break }; $position += $length }
                if ($label -ne 249 -and $label -ne 255) { $output.Write($bytes, $start, $position - $start) }
            } elseif ($marker -eq 44) {
                $control = [byte[]]@(33,249,4,8,5,0,0,0)
                $output.Write($control, 0, $control.Length); $count++
                $packed = $bytes[$position + 8]; $position += 9
                if (($packed -band 128) -ne 0) { $position += 3 * [Math]::Pow(2, ($packed -band 7) + 1) }
                $position++
                while ($true) { $length = $bytes[$position++]; if ($length -eq 0) { break }; $position += $length }
                $output.Write($bytes, $start, $position - $start)
            } else { throw 'Unexpected GIF block.' }
        }
        if ($count -ne $files.Count) { throw 'Preview frame count mismatch.' }
        [System.IO.File]::WriteAllBytes($gifPath, $output.ToArray())
    } finally { $output.Dispose() }
    Write-Output "Rendered $count bound-input frames: $gifPath"
    if (-not $KeepFrames) {
        $resolvedFrames = [System.IO.Path]::GetFullPath($frameRoot)
        $allowedPreviews = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'assets/previews')) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $resolvedFrames.StartsWith($allowedPreviews, [System.StringComparison]::OrdinalIgnoreCase)) { throw 'Preview frame folder is outside the project preview directory.' }
        Remove-Item -LiteralPath $resolvedFrames -Recurse -Force
    }
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $helper) { Remove-Item -LiteralPath $helper }
}
