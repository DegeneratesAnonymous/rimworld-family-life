# Builds every assembly for both game versions, runs the tests (which inspect the built assemblies), proves the modules' pure code builds on its own, assembles and checks the
# packages, and checks the Harmony patches. Usage: .\scripts\Test.ps1
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot

# 1. The framework, then each module against the framework's public surface only, for both versions. A module that reaches for something internal fails here.
foreach ($v in "1.7", "1.6") {
    foreach ($project in "FamilyLife\FamilyLife.csproj", "Education\WilsEducation.csproj", "Care\WilsCommunityCare.csproj") {
        dotnet build (Join-Path $repo "Source\$project") -c Release -p:GameVersion=$v -v q
        if ($LASTEXITCODE -ne 0) { throw "Build failed for $project ($v)" }
    }
}

# 2. The tests, including the ones that read the built assemblies and the package layout.
dotnet test (Join-Path $repo "Source\FamilyLife.Tests\FamilyLife.Tests.csproj")
if ($LASTEXITCODE -ne 0) { throw "Tests failed" }

# 3. Isolation proof: each module's pure code must build against the shared pure assembly alone (public members only).
foreach ($m in "Education", "Care") {
    dotnet build (Join-Path $repo "Source\Isolation\$m.Domain.csproj") -v q
    if ($LASTEXITCODE -ne 0) { throw "$m.Domain no longer builds on its own: a module is using something internal to the shared code" }
}

# 4. Assemble the three installable packages and check their layout.
& (Join-Path $PSScriptRoot "Build-Packages.ps1") -NoBuild

# 5. The Harmony patches still match the game.
& (Join-Path $PSScriptRoot "Check-Patches.ps1")
if ($LASTEXITCODE -ne 0) { throw "Patch check failed" }
Write-Host "Tests passed, the modules build in isolation, all three assemblies build for both versions, the packages check out, and all patches match the game."
