param([switch]$ChatOnly, [switch]$ChatPreview)
$ErrorActionPreference = 'Stop'
$testMain = if ($ChatOnly -or $ChatPreview) { 'ChatTests' } else { 'CurrentPetTests' }
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\tests'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Generated test and helper files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $cache; $env:TMP = $cache
$testExe = Join-Path $cache ('DesktopPet.CurrentTests.' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:$testMain /optimize+ /codepage:65001 "/out:$testExe" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll (Join-Path $projectRoot 'DesktopPet.cs') (Join-Path $projectRoot 'PetMenu.cs') (Join-Path $projectRoot 'PetWardrobeWindow.cs') (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $projectRoot 'InputFeedback.cs') (Join-Path $projectRoot 'TypingStatistics.cs') (Join-Path $projectRoot 'AssistantPaths.cs') (Join-Path $projectRoot 'PetAssistant.cs') (Join-Path $projectRoot 'PetAssistantWindow.cs') (Join-Path $projectRoot 'BoundPetScene.cs') (Join-Path $projectRoot 'LayeredPetRig.cs') (Join-Path $projectRoot 'MusicPlayback.cs') (Join-Path $projectRoot 'PetCalendar.cs') (Join-Path $projectRoot 'PetCalendarWorkspace.cs') (Join-Path $projectRoot 'PetChat.cs') (Join-Path $projectRoot 'PetVoice.cs') (Join-Path $projectRoot 'PetChatWindow.cs') (Join-Path $PSScriptRoot 'MenuTestSupport.cs') (Join-Path $PSScriptRoot 'CurrentPetTests.cs') (Join-Path $PSScriptRoot 'UiInteractionTests.cs') (Join-Path $PSScriptRoot 'TransparencyTests.cs') (Join-Path $PSScriptRoot 'TopMostTests.cs') (Join-Path $PSScriptRoot 'InteractionTests.cs') (Join-Path $PSScriptRoot 'TypingHelmetTests.cs') (Join-Path $PSScriptRoot 'HairstyleTests.cs') (Join-Path $PSScriptRoot 'MusicPlaybackTests.cs') (Join-Path $PSScriptRoot 'CalendarTests.cs') (Join-Path $PSScriptRoot 'ChatTests.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
    if ($ChatPreview) { & $testExe $projectRoot '--preview-window' }
    else { & $testExe $projectRoot }
    if ($LASTEXITCODE -ne 0) { throw 'Current pet tests failed.' }
}
finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $testExe) { Remove-Item -LiteralPath $testExe }
}
