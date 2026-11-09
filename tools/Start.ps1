$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$executable = Join-Path $projectRoot 'DesktopPet.exe'
$binary = Get-Item -LiteralPath $executable -ErrorAction SilentlyContinue
$needsBuild = $null -eq $binary
if ($binary) {
    foreach ($relative in @('DesktopPet.cs','PetMenu.cs','PetWardrobeWindow.cs','KnightUi.cs','InputFeedback.cs','TypingStatistics.cs','AssistantPaths.cs','PetAssistant.cs','PetAssistantWindow.cs','BoundPetScene.cs','LayeredPetRig.cs','MusicPlayback.cs','PetCalendar.cs','PetCalendarWorkspace.cs','PetChat.cs','PetChatWindow.cs','PetVoice.cs','tools/Build.ps1','tools/Export-AppIcon.cs')) {
        $source = Get-Item -LiteralPath (Join-Path $projectRoot $relative)
        if ($source.LastWriteTimeUtc -gt $binary.LastWriteTimeUtc) { $needsBuild = $true; break }
    }
}
if ($needsBuild) {
    & (Join-Path $PSScriptRoot 'Build.ps1') -Run
} else {
    $running = Get-Process DesktopPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
    if (-not $running) { Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Normal }
}
