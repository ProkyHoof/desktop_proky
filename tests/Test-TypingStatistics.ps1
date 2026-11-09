param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\typing-tests'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Generated test and helper files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $cache; $env:TMP = $cache
$library = Join-Path $cache ('DesktopPet.TypingTests.' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    # Compile a library and invoke the tests in PowerShell: this creates no EXE.
    $sources = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
    $sources += Join-Path $PSScriptRoot 'TypingStatisticsTests.cs'
    $sources += Join-Path $PSScriptRoot 'MenuTestSupport.cs'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library /optimize+ /codepage:65001 "/out:$library" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'Typing statistics compilation failed.' }
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($library))
    $previewPath = if ($Preview) { Join-Path $projectRoot 'assets\previews\typing-statistics.png' } else { '' }
    $method = $assembly.GetType('TypingStatisticsTests').GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    try { $method.Invoke($null, [object[]]@([string]$projectRoot, [string]$previewPath)) }
    catch { if ($_.Exception.InnerException) { Write-Host $_.Exception.InnerException.ToString() }; throw }
}
finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
    if (Test-Path -LiteralPath $library) { Remove-Item -LiteralPath $library }
}
