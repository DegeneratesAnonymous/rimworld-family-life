# Runs the domain tests and builds both game versions. Usage: .\scripts\Test.ps1
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $repo "Source\FamilyLife.Tests\FamilyLife.Tests.csproj")
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }
foreach ($v in "1.7", "1.6") {
    dotnet build (Join-Path $repo "Source\FamilyLife\FamilyLife.csproj") -c Release -p:GameVersion=$v -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $v" }
}
& (Join-Path $PSScriptRoot "Check-Patches.ps1")
if ($LASTEXITCODE -ne 0) { throw "Patch check failed" }
Write-Host "Tests passed, both versions build, and all patches match the game."
