"""Create reference workspaces from existing PNG assets, without resampling.

Requires Pillow. Run from repository root:
python PET/create_workspaces.py --ninja-package <quiet-ninja.pet> --new-reference <PNG>
Existing files are protected unless --overwrite is supplied.
"""
from pathlib import Path
from zipfile import ZipFile
from PIL import Image
import argparse
import hashlib
import io
import json
import shutil

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "PET"
IDEAS = [('stubborn-knot', 'Узел с характером', 'Тянет пояс; узел неожиданно поддаётся, ниндзя отшатывается, разворачивается и завязывает его заново.', 'random-idle'), ('secret-sneeze', 'Секретный чих', 'Сдерживает чих, зажимает лицо ладонями, мощно чихает всем телом и подпрыгивает.', 'random-idle'), ('unexpected-wind', 'Ветер не по плану', 'Упирается ногами и наклоняется против порыва; ветер стихает, он едва не падает вперёд.', 'random-idle'), ('hero-landing', 'Геройское приземление', 'Прыжок с разворотом, низкая посадка и два смешных подскока для равновесия.', 'random-idle'), ('feint', 'Обманный манёвр', 'Показывает в одну сторону, бросается в другую, тормозит и невозмутимо возвращается.', 'random-idle'), ('invisible-wall', 'Невидимая стена', 'Шагает, упирается ладонями в невидимое препятствие, ощупывает его и протискивается боком.', 'random-idle'), ('forbidden-dance', 'Танец запретной школы', 'Быстрые движения пятками и носками, скользящий шаг, оборот на одной ноге и строгая стойка.', 'random-idle'), ('deep-bow', 'Слишком почтительный поклон', 'Кланяется, перевешивает головой, делает перекат и вскакивает, будто так задумано.', 'random-idle'), ('playful-spark', 'Искра-озорница', 'Ловит маленькую светящуюся искру: прыгает, хлопает ладонями, приседает; искра ускользает.', 'random-idle'), ('paper-boomerang', 'Бумажный бумеранг', 'Бросает бумажную звёздочку, складывает руки, замечает её возвращение и ловит за спиной.', 'random-idle')]


def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def workspace(pet, reference, origin, clips, *, concept=False):
    folder = OUT / pet["id"]
    if folder.exists() and not ARGS.overwrite:
        raise FileExistsError(f"Refusing to replace {folder}; use --overwrite intentionally")
    folder.mkdir(exist_ok=True)
    for name in ("source", "renders", "exports"):
        stage = folder / name
        stage.mkdir(exist_ok=True)
        (stage / ".gitkeep").touch()
    target = folder / "reference.png"
    if isinstance(reference, Path):
        shutil.copyfile(reference, target)
    else:
        reference.save(target, compress_level=6)
    with Image.open(target) as image:
        size = list(image.size)
        rgba = image.convert("RGBA")
        alpha = rgba.getchannel("A").getextrema()
        pixels = hashlib.sha256(rgba.tobytes()).hexdigest()
    if alpha != (0, 255):
        raise ValueError(f"Reference lacks usable transparency: {target}")
    keys = folder / "keyframes"
    keys.mkdir(exist_ok=True)
    for name in ("start.png", "end.png"):
        shutil.copyfile(target, keys / name)
    assert (keys / "start.png").read_bytes() == (keys / "end.png").read_bytes()
    write_json(folder / "theme.json", pet["theme"])
    animations = []
    for index, clip in enumerate(clips):
        action_id = clip.get("id", f"action-{index + 1:02}")
        animations.append({"id": action_id, "name": clip["name"], "file": f"animations/{action_id}/animation.json"})
        write_json(folder / "animations" / action_id / "animation.json", {
            "id": action_id, "name": clip["name"],
            "status": "idea" if concept else "existing-animation",
            "movement": clip.get("movement", "Существующая анимация; исходные кадры сохранены."),
            "proposedTrigger": clip.get("trigger"),
            "rendering": "Персонаж, необходимые реквизит и эффекты, мягкая тень; камера неподвижна.",
            "existingClip": None if concept else clip,
            "generation": {
                "reference": "../../reference.png", "firstFrame": "../../keyframes/start.png",
                "lastFrame": "../../keyframes/end.png", "requested": False,
                "sameCharacterScale": True, "fixedCamera": True, "keepFullShadow": True,
                "keepAccessories": True, "doNotDownscale": True, "identicalEndpointPixels": True,
            },
        })
    package = None if pet["id"] == "klyaksa" or concept else {
        "url": f"https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/{pet['id']}.pet"
    }
    write_json(folder / "pet.json", {
        "id": pet["id"], "name": pet["name"], "status": "reference-and-ideas" if concept else "available",
        "character": pet.get("character", "Характер и внешность сохраняются по референсу и существующим анимациям."),
        "reference": {"file": "reference.png", "size": size, "rgbaSha256": pixels, "origin": origin},
        "keyframes": {"start": "keyframes/start.png", "end": "keyframes/end.png", "filesIdentical": True},
        "theme": "theme.json", "package": package, "animations": animations,
    })
    print(pet["id"], size, len(animations), "reference and matching keyframes")


parser = argparse.ArgumentParser()
parser.add_argument("--ninja-package", type=Path, required=True)
parser.add_argument("--new-reference", type=Path, required=True)
parser.add_argument("--overwrite", action="store_true")
ARGS = parser.parse_args()
catalog = json.loads((ROOT / "installer/pets/bundled-source.json").read_text(encoding="utf-8-sig"))
for pet in catalog:
    clip = pet["animations"][0]
    first = (clip.get("sheets") or [clip])[0]
    with Image.open(ROOT / "wwwroot" / first["file"]) as sheet:
        width, height = clip.get("frameWidth", sheet.width), clip.get("frameHeight", sheet.height)
        reference = sheet.convert("RGBA").crop((0, 0, width, height))
    workspace(pet, reference, "Первый кадр исходного PNG без масштабирования", pet["animations"])
for package in (ROOT / "pets/knight-watchman.pet", ARGS.ninja_package):
    with ZipFile(package) as archive:
        pet = json.loads(archive.read("manifest.json"))
        clip = pet["animations"][0]
        first = (clip.get("sheets") or [clip])[0]
        with Image.open(io.BytesIO(archive.read(first["file"]))) as sheet:
            reference = sheet.convert("RGBA").crop((0, 0, clip["frameWidth"], clip["frameHeight"]))
    workspace(pet, reference, "Первый кадр .pet без масштабирования", pet["animations"])
workspace({
    "id": "cloth-ninja", "name": "Тканевый ниндзя", "character": "Маленький ниндзя, который старается выглядеть невозмутимым, но постоянно попадает в забавные ситуации. Молчаливый, любопытный, ловкий и немного озорной. Движения быстрые и точные, с короткой комической развязкой.",
    "theme": {"name": "Уголь и канат", "accent": "#AA8A78", "background": "#171B23", "surface": "#272D38"},
}, ARGS.new_reference, "Изображение пользователя: удалён фон и выступающая слева часть меча", [
    {"id": action_id, "name": name, "movement": movement, "trigger": trigger} for action_id, name, movement, trigger in IDEAS
], concept=True)
