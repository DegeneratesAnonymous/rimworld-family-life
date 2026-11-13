# Builds the packages for 1.6 and 1.7 and copies them into the game's Mods folder so they can be tested.
# Usage: .\scripts\Stage-ToRimWorld.ps1 [-GameDir "B:\SteamLibrary\steamapps\common\RimWorld"] [-Only WilsEducation,WilsCommunityCare] [-FrameworkOnly]
#   -FrameworkOnly   stages only the framework (the "Framework alone" load test; remove the module folders from Mods first, or they stay loaded)
#   -Only            stage just these packages (WilsFamilyLife, WilsEducation, WilsCommunityCare, WilsTreasury); the others already in Mods are left untouched
# Each package is a Mods folder of its own with its own About.xml. RimWorld must be closed: it keeps the DLLs locked.
param(
    [string]$GameDir = "B:\SteamLibrary\steamapps\common\RimWorld",
    [string[]]$Only,
    [switch]$FrameworkOnly
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "Build-Packages.ps1")
$dist = Join-Path $repo "dist"
$packages = @("WilsFamilyLife", "WilsEducation", "WilsCommunityCare", "WilsTreasury", "WilsHospitality")
if ($FrameworkOnly) { $packages = @("WilsFamilyLife") } elseif ($Only) { $packages = $Only }
foreach ($pkg in $packages) {
    $dest = Join-Path $GameDir "Mods\$pkg"
    robocopy (Join-Path $dist $pkg) $dest /MIR /XF PublishedFileId.txt /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed for $pkg - is RimWorld still running? Close it (it keeps the mod DLL locked) and run this again." }
    Write-Host "Staged $pkg to $dest"
}
$global:LASTEXITCODE = 0
