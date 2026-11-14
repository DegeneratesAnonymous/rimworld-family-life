# Assembles the six installable packages under dist\ and checks each one's layout. Nothing is copied into the game; use Stage-ToRimWorld.ps1 for that.
#   dist\WilsFamilyLife       the framework package (SpacemanWil.WilsFamilyLife): family records, community posts, and everything the modules plug into
#   dist\WilsEducation        SpacemanWil.WilsEducation (needs the framework)
#   dist\WilsCommunityCare    SpacemanWil.WilsCommunityCare (needs the framework)
#   dist\WilsTreasury         SpacemanWil.WilsTreasury (needs the framework)
#   dist\WilsHospitality      SpacemanWil.WilsHospitality (needs the framework; Treasury enables paid operations)
#   dist\WilsCrafting         SpacemanWil.WilsCrafting (workshop careers; Hospitality enables commercial shops)
# Usage: .\scripts\Build-Packages.ps1 [-NoBuild]
param([switch]$NoBuild)
$ErrorActionPreference = "Stop"
$repo = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $repo "dist"

if (-not $NoBuild) {
    foreach ($v in "1.7", "1.6") {
        foreach ($project in "FamilyLife\FamilyLife.csproj", "Education\WilsEducation.csproj", "Care\WilsCommunityCare.csproj", "Treasury\WilsTreasury.csproj", "Hospitality\WilsHospitality.csproj", "Crafting\WilsCrafting.csproj") {
            dotnet build (Join-Path $repo "Source\$project") -c Release -p:GameVersion=$v -v q
            if ($LASTEXITCODE -ne 0) { throw "Build failed for $project ($v)" }
        }
    }
}

function Copy-Tree($from, $to) {
    robocopy $from $to /MIR /XF PublishedFileId.txt .gitkeep *.json /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copy failed: $from" }
    $global:LASTEXITCODE = 0
}

if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

# the framework package: the repository root's content
$fw = Join-Path $dist "WilsFamilyLife"
foreach ($dir in "About", "Defs", "Patches", "Languages", "Textures", "1.6", "1.7") { Copy-Tree (Join-Path $repo $dir) (Join-Path $fw $dir) }
Copy-Item (Join-Path $repo "LICENSE") $fw -Force

# the module packages
foreach ($pkg in "WilsEducation", "WilsCommunityCare", "WilsTreasury", "WilsHospitality", "WilsCrafting") {
    $dest = Join-Path $dist $pkg; $src = Join-Path $repo "Packages\$pkg"
    foreach ($dir in "About", "Defs", "Patches", "Languages", "1.6", "1.7") { if (Test-Path (Join-Path $src $dir)) { Copy-Tree (Join-Path $src $dir) (Join-Path $dest $dir) } }
    Copy-Item (Join-Path $repo "LICENSE") $dest -Force
}

# ---- checks
$problems = @()
function Need($path, $what) { if (-not (Test-Path $path)) { $script:problems += "missing $what ($path)" } }

foreach ($entry in @(@("WilsFamilyLife", "SpacemanWil.WilsFamilyLife", "FamilyLife.dll"), @("WilsEducation", "SpacemanWil.WilsEducation", "WilsEducation.dll"), @("WilsCommunityCare", "SpacemanWil.WilsCommunityCare", "WilsCommunityCare.dll"), @("WilsTreasury", "SpacemanWil.WilsTreasury", "WilsTreasury.dll"), @("WilsHospitality", "SpacemanWil.WilsHospitality", "WilsHospitality.dll"), @("WilsCrafting", "SpacemanWil.WilsCrafting", "WilsCrafting.dll"))) {
    $dir = Join-Path $dist $entry[0]
    Need (Join-Path $dir "About\About.xml") ("About.xml in " + $entry[0])
    Need (Join-Path $dir "LICENSE") ("licence in " + $entry[0])
    $about = Get-Content (Join-Path $dir "About\About.xml") -Raw
    if ($about -notmatch [regex]::Escape("<packageId>" + $entry[1] + "</packageId>")) { $problems += ($entry[0] + ": wrong package id") }
    foreach ($v in "1.6", "1.7") {
        Need (Join-Path $dir "$v\Assemblies\$($entry[2])") ($entry[2] + " for $v")
        $others = Get-ChildItem (Join-Path $dir "$v\Assemblies") -Filter *.dll -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne $entry[2] }
        if ($others) { $problems += ($entry[0] + " $v ships extra assemblies: " + ($others.Name -join ", ")) }
    }
}
# a module package carries no framework content, and the framework package carries no module content
foreach ($pkg in "WilsEducation", "WilsCommunityCare", "WilsTreasury", "WilsHospitality", "WilsCrafting") {
    $forbidden = if ($pkg -eq "WilsTreasury") { @("Textures") } else { @("Patches", "Textures") }          # the Treasury package carries its own zone patch
    foreach ($f in $forbidden) { if (Test-Path (Join-Path (Join-Path $dist $pkg) $f)) { $problems += "$pkg ships $f, which belongs to the framework" } }
}
$fwDefs = (Get-ChildItem (Join-Path $dist "WilsFamilyLife\Defs") -Recurse -Filter *.xml | Get-Content -Raw) -join "`n"
foreach ($name in "WFL_ClassTeacher", "WFL_OfficeLesson", "WFL_ClinicDoctor", "WFL_CheckedUp") { if ($fwDefs -match "<defName>$name</defName>") { $problems += "framework package defines $name, which a module owns" } }

if ($problems.Count -gt 0) { $problems | ForEach-Object { Write-Host "PACKAGE PROBLEM: $_" -ForegroundColor Red }; throw "Package check failed" }
Write-Host "Packages assembled under $dist and checked: WilsFamilyLife, WilsEducation, WilsCommunityCare, WilsTreasury, WilsHospitality, WilsCrafting (1.6 and 1.7)."
