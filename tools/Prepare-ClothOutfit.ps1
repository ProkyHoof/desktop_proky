$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $projectRoot '.tools\outfit'
if ([IO.Path]::GetPathRoot($cache) -ieq 'C:\') { throw 'Outfit build files must stay off C:.' }
[IO.Directory]::CreateDirectory($cache) | Out-Null
$oldTemp=$env:TEMP; $oldTmp=$env:TMP; $env:TEMP=$cache; $env:TMP=$cache
try {
    $helper=Join-Path $cache 'Prepare-ClothOutfit.exe'
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:exe /codepage:65001 "/out:$helper" /r:System.Drawing.dll (Join-Path $PSScriptRoot 'Prepare-ClothOutfit.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Outfit registration compilation failed.' }
    & $helper $projectRoot
    if ($LASTEXITCODE -ne 0) { throw 'Outfit registration failed.' }
}
finally { $env:TEMP=$oldTemp; $env:TMP=$oldTmp }
