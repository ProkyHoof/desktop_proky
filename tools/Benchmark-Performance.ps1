param([int]$Frames = 180)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $env:TEMP ('DesktopPet.Benchmark.' + [Guid]::NewGuid().ToString('N') + '.exe')
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /main:PerformanceBenchmark /optimize+ /codepage:65001 "/out:$helper" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll (Join-Path $projectRoot 'DesktopPet.cs') (Join-Path $projectRoot 'PetMenu.cs') (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $projectRoot 'InputFeedback.cs') (Join-Path $projectRoot 'BoundPetScene.cs') (Join-Path $projectRoot 'LayeredPetRig.cs') (Join-Path $projectRoot 'MusicPlayback.cs') (Join-Path $projectRoot 'PetCalendar.cs') (Join-Path $projectRoot 'PetCalendarWorkspace.cs') (Join-Path $projectRoot 'PetChat.cs') (Join-Path $projectRoot 'PetVoice.cs') (Join-Path $projectRoot 'PetChatWindow.cs') (Join-Path $PSScriptRoot 'Benchmark-Performance.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark compilation failed.' }
    & $helper $projectRoot $Frames
    if ($LASTEXITCODE -ne 0) { throw 'Benchmark failed.' }
} finally {
    if (Test-Path -LiteralPath $helper) { Remove-Item -LiteralPath $helper }
}
