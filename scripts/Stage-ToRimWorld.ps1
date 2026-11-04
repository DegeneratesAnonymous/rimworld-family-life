# Builds the mod for 1.6 and 1.7 and copies it into the game's Mods folder so it can be tested.
# Usage: .\scripts\Stage-ToRimWorld.ps1 [-GameDir "B:\SteamLibrary\steamapps\common\RimWorld"] [-Name WilsFamilyLife]
param(
    [string]$GameDir = "B:\SteamLibrary\steamapps\common\RimWorld",
    [string]$Name = "WilsFamilyLife"
)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dest = Join-Path $GameDir "Mods\$Name"

foreach ($v in "1.7", "1.6") {
    dotnet build (Join-Path $repo "Source\FamilyLife") -c Release -p:GameVersion=$v -v q
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $v" }
}

New-Item -ItemType Directory -Force $dest | Out-Null
foreach ($dir in "About", "Defs", "Patches", "Languages", "Textures", "1.6", "1.7") {
    robocopy (Join-Path $repo $dir) (Join-Path $dest $dir) /MIR /XF PublishedFileId.txt .gitkeep /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed for $dir - is RimWorld still running? Close it (it keeps the mod DLL locked) and run this again." }
}
Copy-Item (Join-Path $repo "LICENSE") $dest -Force
Write-Host "Staged to $dest"
