param(
    [string]$GameDir = $env:ADOFAI_DIR
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

if (-not $GameDir) {
    foreach ($c in @(
        'D:\SteamLibrary\steamapps\common\A Dance of Fire and Ice',
        'C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice',
        'C:\Program Files\Steam\steamapps\common\A Dance of Fire and Ice'
    )) {
        if (Test-Path (Join-Path $c 'A Dance of Fire and Ice.exe')) { $GameDir = $c; break }
    }
}
if (-not $GameDir -or -not (Test-Path (Join-Path $GameDir 'A Dance of Fire and Ice.exe'))) {
    throw "Game directory not found. Pass -GameDir 'D:\...\A Dance of Fire and Ice' or set ADOFAI_DIR."
    }

$managed = Join-Path $GameDir 'A Dance of Fire and Ice_Data\Managed'
$harmony = Join-Path $GameDir 'UMM\Core\0Harmony.dll'
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe (C# compiler) not found.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$cscArgs = @('/target:library', '/nologo', "/out:$dist\HitError.dll",
    "/reference:$managed\netstandard.dll",
    "/reference:$managed\RDTools.dll",
    "/reference:$managed\Assembly-CSharp.dll",
    "/reference:$managed\Assembly-CSharp-firstpass.dll",
    "/reference:$harmony",
    (Join-Path $root 'src\HitError.cs'))
Get-ChildItem $managed -Filter 'UnityEngine*.dll' | ForEach-Object {     $cscArgs += "/reference:$($_.FullName)" }

& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "compile failed ($LASTEXITCODE)" }

Copy-Item (Join-Path $root 'Info.json') (Join-Path $dist 'Info.json') -Force
Write-Host "OK -> $dist\HitError.dll"
