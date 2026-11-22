"""Validate the shared bundled library; imported pets stay in the user's library."""
import json
from pathlib import Path
import sys
assets = Path(sys.argv[1]).resolve()
catalog = assets / "pets.json"
pets = json.loads(catalog.read_text(encoding="utf-8-sig"))
if [pet["id"] for pet in pets] != ["klyaksa"]:
    raise SystemExit("Expected the shared Klyaksa-only bundled library")
for pet in pets:
    for clip in pet["animations"]:
        for part in clip.get("sheets", [clip]):
            path = (assets.parent / part["file"]).resolve()
            if not path.is_relative_to(assets) or not path.is_file():
                raise SystemExit(f"Invalid sprite sheet: {part['file']}")
catalog.write_text(json.dumps(pets, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")