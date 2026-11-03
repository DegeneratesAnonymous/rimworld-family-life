# Offline check that every Harmony patch still matches the game's methods (target exists, parameter names match).
# It reads the assemblies without loading or running the game. Build both versions first (Test.ps1 does).
param([string]$GameDir = "B:\SteamLibrary\steamapps\common\RimWorld")
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$nuget = Join-Path $env:USERPROFILE ".nuget\packages"
dotnet build (Join-Path $repo "tools\PatchCheck\PatchCheck.csproj") -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "PatchCheck build failed" }

Write-Host "1.7 against the installed game"
$h7 = Join-Path $nuget "lib.harmony\3.0.0-rc.3\lib\net10.0"
dotnet run --no-build --project (Join-Path $repo "tools\PatchCheck") -- (Join-Path $repo "1.7\Assemblies\FamilyLife.dll") System.Private.CoreLib $GameDir $h7
if ($LASTEXITCODE -ne 0) { throw "1.7 patch check failed" }

Write-Host "1.6 against the 1.6 reference assemblies"
$ref = Join-Path $nuget "krafs.rimworld.ref\1.6.4871\ref\net472"
$fx = (Get-ChildItem (Join-Path $nuget "microsoft.netframework.referenceassemblies.net472") -Recurse -Filter mscorlib.dll | Select-Object -First 1).DirectoryName
$h6 = Join-Path $nuget "lib.harmony\2.4.3\lib\net472"
dotnet run --no-build --project (Join-Path $repo "tools\PatchCheck") -- (Join-Path $repo "1.6\Assemblies\FamilyLife.dll") mscorlib $ref $fx $h6
if ($LASTEXITCODE -ne 0) { throw "1.6 patch check failed" }
