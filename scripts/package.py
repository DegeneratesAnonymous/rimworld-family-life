#!/usr/bin/env python3
"""Assemble the five installable packages from already built assemblies; never stages into RimWorld."""
from pathlib import Path
import hashlib
import json
import subprocess
import shutil
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
PACKAGES = {"WilsFamilyLife": "FamilyLife", "WilsEducation": "WilsEducation", "WilsCommunityCare": "WilsCommunityCare", "WilsTreasury": "WilsTreasury", "WilsHospitality": "WilsHospitality"}

def main():
    dest = ROOT / "dist" / "hospitality-test"
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True)
    for package, assembly in PACKAGES.items():
        src = ROOT if package == "WilsFamilyLife" else ROOT / "Packages" / package
        out = dest / package
        for name in ("About", "Defs", "Patches", "Languages", "Textures", "1.6", "1.7"):
            if (src / name).is_dir():
                shutil.copytree(src / name, out / name, ignore=shutil.ignore_patterns("*.json", ".gitkeep", "PublishedFileId.txt"))
        shutil.copy2(ROOT / "LICENSE", out / "LICENSE")
        assert ET.parse(out / "About/About.xml").findtext("packageId") == "SpacemanWil." + package
        for version in ("1.6", "1.7"):
            folder = out / version / "Assemblies"
            assert sorted(f.name for f in folder.glob("*.dll")) == [assembly + ".dll"], f"Incorrect assembly layout in {folder}"
        for f in out.rglob("*.xml"):
            ET.parse(f)
    shutil.copy2(ROOT / "docs/HOSPITALITY-TESTING.md", dest / "README-TESTING.md")
    shutil.copy2(ROOT / "docs/COOKING-HOSPITALITY.md", dest / "IMPLEMENTATION.md")
    manifest = {
        "schema": 1,
        "source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "worktree_dirty": bool(subprocess.check_output(["git", "status", "--porcelain"], cwd=ROOT, text=True).strip()),
        "game_versions": ["1.6", "1.7"],
        "packages": list(PACKAGES),
        "sha256": {file.relative_to(dest).as_posix(): hashlib.sha256(file.read_bytes()).hexdigest()
                   for file in sorted(dest.rglob("*")) if file.is_file()},
    }
    (dest / "BUILD-INFO.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    archive = ROOT / "dist" / "WilsCookingHospitality-test.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
        for file in sorted(dest.rglob("*")):
            if file.is_file():
                z.write(file, file.relative_to(dest))
    print(f"Checked five packages for RimWorld 1.6 and 1.7: {archive}")

if __name__ == "__main__":
    main()
