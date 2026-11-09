param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\assistant-tests'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Tests must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp = $env:TEMP; $oldTmp = $env:TMP
$env:TEMP = $cache; $env:TMP = $cache
$library = Join-Path $cache ('Assistant.Tests.' + [Guid]::NewGuid().ToString('N') + '.dll')
try {
    $sources = @(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' | ForEach-Object { $_.FullName })
    $sources += Join-Path $PSScriptRoot 'AssistantTests.cs'
    $sources += Join-Path $PSScriptRoot 'MenuTestSupport.cs'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library /optimize+ /codepage:65001 "/out:$library" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'Assistant test compilation failed.' }
    $assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($library))
    $previewPath = if ($Preview) { Join-Path $projectRoot 'assets\previews\assistant.png' } else { '' }
    $testRoot = Join-Path $cache ('run-' + [Guid]::NewGuid().ToString('N'))
    $method = $assembly.GetType('AssistantTests').GetMethod('Run', [Reflection.BindingFlags]'Static,NonPublic')
    try { $method.Invoke($null, [object[]]@([string]$projectRoot, [string]$testRoot, [string]$previewPath)) }
    catch { Write-Host $_.Exception.ToString(); throw }
}
finally {
    $env:TEMP = $oldTemp; $env:TMP = $oldTmp
}
