param([switch]$Run)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$candidate = Join-Path $projectRoot 'DesktopPet.build.exe'
$executable = Join-Path $projectRoot 'DesktopPet.exe'
$buildCache = Join-Path $projectRoot '.tools\build'
if ([IO.Path]::GetPathRoot($buildCache) -ieq 'C:\') { throw 'Local build files must stay off C:.' }
[IO.Directory]::CreateDirectory($buildCache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $buildCache; $env:TMP = $buildCache
$iconHelper = Join-Path $buildCache ('DesktopPet.Icon.' + [Guid]::NewGuid().ToString('N') + '.exe')
$iconPath = $iconHelper + '.ico'
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 "/out:$iconHelper" /r:System.Drawing.dll /r:System.Windows.Forms.dll (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $PSScriptRoot 'Export-AppIcon.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Icon helper compilation failed.' }
    & $iconHelper $iconPath
    if ($LASTEXITCODE -ne 0) { throw 'Icon export failed.' }
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 "/out:$candidate" "/win32icon:$iconPath" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll (Join-Path $projectRoot 'DesktopPet.cs') (Join-Path $projectRoot 'PetMenu.cs') (Join-Path $projectRoot 'PetWardrobeWindow.cs') (Join-Path $projectRoot 'KnightUi.cs') (Join-Path $projectRoot 'InputFeedback.cs') (Join-Path $projectRoot 'TypingStatistics.cs') (Join-Path $projectRoot 'AssistantPaths.cs') (Join-Path $projectRoot 'PetAssistant.cs') (Join-Path $projectRoot 'PetAssistantWindow.cs') (Join-Path $projectRoot 'BoundPetScene.cs') (Join-Path $projectRoot 'LayeredPetRig.cs') (Join-Path $projectRoot 'MusicPlayback.cs') (Join-Path $projectRoot 'PetCalendar.cs') (Join-Path $projectRoot 'PetCalendarWorkspace.cs') (Join-Path $projectRoot 'PetChat.cs') (Join-Path $projectRoot 'PetVoice.cs') (Join-Path $projectRoot 'PetChatWindow.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
    $check = Start-Process -FilePath $candidate -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw 'Animation self-test failed; previous executable retained.' }
    $running = Get-Process DesktopPet -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
    if ($running) {
        $exitRequest = Start-Process -FilePath $executable -ArgumentList '--exit' -PassThru -Wait -WindowStyle Hidden
        if ($exitRequest.ExitCode -ne 0) { throw 'Could not ask the running pet to exit.' }
    }
    foreach ($pet in $running) {
        # A taskbar-hidden layered form has no Process.MainWindowHandle.
        # The named event closes it through its own UI loop and saves location.
        if (-not $pet.WaitForExit(5000)) { throw 'Please close the running desktop pet before replacing the executable.' }
    }
    Move-Item -LiteralPath $candidate -Destination $executable -Force
    # The pet itself is an interactive window; only the self-test runs hidden.
    if ($Run) { Start-Process -FilePath $executable -WorkingDirectory $projectRoot -WindowStyle Normal }
    Write-Output 'Build and self-test passed.'
}
finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $candidate) { Remove-Item -LiteralPath $candidate }
    foreach ($file in @($iconHelper,$iconPath)) { if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } }
}
