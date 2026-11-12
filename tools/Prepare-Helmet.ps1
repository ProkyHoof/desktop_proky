$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\helmet'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Helmet build files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $env:TEMP=$cache; $env:TMP=$cache
try {
    $helper=Join-Path $cache 'Prepare-LayeredRig.exe'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 "/out:$helper" /r:System.Drawing.dll (Join-Path $PSScriptRoot 'Prepare-LayeredRig.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Helmet preparation compilation failed.' }
    & $helper $projectRoot --helmet-only
    if ($LASTEXITCODE -ne 0) { throw 'Helmet registration failed.' }
}
finally { $env:TEMP=$oldTemp; $env:TMP=$oldTmp }
