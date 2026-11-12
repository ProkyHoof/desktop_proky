$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\hairstyle-tests'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Hairstyle tests must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $env:TEMP=$cache; $env:TMP=$cache
try {
    $library=Join-Path $cache ('Hairstyle.Tests.'+[Guid]::NewGuid().ToString('N')+'.dll')
    $sources=@(Get-ChildItem -LiteralPath $projectRoot -Filter '*.cs' | ForEach-Object {$_.FullName})
    $sources+=Join-Path $PSScriptRoot 'HairstyleTests.cs'
    $sources+=Join-Path $PSScriptRoot 'MenuTestSupport.cs'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:library /optimize+ /codepage:65001 "/out:$library" /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Runtime.Serialization.dll /r:System.Net.Http.dll /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll $sources
    if ($LASTEXITCODE -ne 0) { throw 'Hairstyle tests compilation failed.' }
    $assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($library))
    $method=$assembly.GetType('HairstyleTests').GetMethod('Run',[Reflection.BindingFlags]'Static,NonPublic')
    $method.Invoke($null,[object[]]@([string]$projectRoot))
}
finally { $env:TEMP=$oldTemp; $env:TMP=$oldTmp }
