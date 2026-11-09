param([int]$Frames = 180)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\benchmark'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Generated test and helper files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $cache; $env:TMP = $cache
$helper = Join-Path $cache ('DesktopPet.Benchmark.' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:PerformanceBenchmark /optimize+ /codepage:65001 "/out:$helper" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll (Join-Path $projectRoot 'DesktopPet.cs') (Join-Path $projectRoot 'PetMenu.cs') (Join-Path $projectRoot 'PetWardrobeWindow.cs') (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $projectRoot 'InputFeedback.cs') (Join-Path $projectRoot 'TypingStatistics.cs') (Join-Path $projectRoot 'AssistantPaths.cs') (Join-Path $projectRoot 'PetAssistant.cs') (Join-Path $projectRoot 'PetAssistantWindow.cs') (Join-Path $projectRoot 'BoundPetScene.cs') (Join-Path $projectRoot 'LayeredPetRig.cs') (Join-Path $projectRoot 'MusicPlayback.cs') (Join-Path $projectRoot 'PetCalendar.cs') (Join-Path $projectRoot 'PetCalendarWorkspace.cs') (Join-Path $projectRoot 'PetChat.cs') (Join-Path $projectRoot 'PetVoice.cs') (Join-Path $projectRoot 'PetChatWindow.cs') (Join-Path $PSScriptRoot 'Benchmark-Performance.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark compilation failed.' }
    & $helper $projectRoot $Frames
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
} finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $helper) { Remove-Item -LiteralPath $helper }
}
