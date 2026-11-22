"""Prepare bundled pets without duplicating the shared Windows/macOS assets."""
import json
from pathlib import Path
import sys
from zipfile import ZipFile

assets, package = map(Path, sys.argv[1:3])
catalog = assets / "pets.json"
pets = [p for p in json.loads(catalog.read_text(encoding="utf-8-sig"))
        if p["id"] not in ("hudi", "knight-watchman")]
pets.sort(key=lambda p: p["id"] != "klyaksa")
with ZipFile(package) as archive:
    knight = json.loads(archive.read("manifest.json"))
    if knight.pop("schemaVersion") != 1 or knight["id"] != "knight-watchman":
        raise SystemExit("Unexpected Knight package")
    destination = assets / knight["id"]
    destination.mkdir(exist_ok=True)
    for clip in knight["animations"]:
        name = clip["file"]
        if Path(name).name != name or not name.endswith(".png"):
            raise SystemExit("Invalid Knight asset path")
        (destination / name).write_bytes(archive.read(name))
        clip["file"] = f"assets/{knight['id']}/{name}"
    pets.append(knight)
if not pets or pets[0]["id"] != "klyaksa":
    raise SystemExit("Required bundled pet missing")
for pet in pets:
    for clip in pet["animations"]:
        for part in clip.get("sheets", [clip]):
            path = (assets.parent / part["file"]).resolve()
            if not path.is_relative_to(assets.resolve()) or not path.is_file():
                raise SystemExit(f"Invalid sprite sheet: {part['file']}")
catalog.write_text(json.dumps(pets, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
(assets / "hudi.png").unlink(missing_ok=True)
