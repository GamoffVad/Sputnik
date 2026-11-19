"""Verify and organize existing pets without resampling their references or frames.

python PET/organize.py --packages artifacts/pet-library-published-packages
The downloaded files must match the published release catalog.
"""
from pathlib import Path
from zipfile import ZipFile
import argparse
import hashlib
import io
import json
import shutil

from PIL import Image

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--packages', type=Path, required=True)
    parser.add_argument('--published-catalog', type=Path, default=REPO / 'installer/pets/catalog.json')
    args = parser.parse_args()
    published = {x['id']: x for x in read(args.published_catalog)}
    bundled = {x['id']: x for x in read(REPO / 'wwwroot/assets/pets.json')}
    entries = []
    for folder in sorted(ROOT.iterdir()):
        if not folder.is_dir() or not (folder / 'pet.json').exists():
            continue
        pet = read(folder / 'pet.json')
        pet_id = pet['id']
        assert folder.name == pet_id
        for stage in ['source', 'renders', 'exports']:
            (folder / stage).mkdir(exist_ok=True)
            (folder / stage / '.gitkeep').touch(exist_ok=True)
        with Image.open(folder / 'reference.png') as im:
            assert im.mode == 'RGBA' and im.getchannel('A').getextrema() == (0, 255)
            assert list(im.size) == pet['reference']['size']
            assert hashlib.sha256(im.tobytes()).hexdigest() == pet['reference']['rgbaSha256']
        assert (folder / 'keyframes/start.png').read_bytes() == (folder / 'keyframes/end.png').read_bytes() == (folder / 'reference.png').read_bytes()
        theme = read(folder / 'theme.json')
        if pet_id == 'klyaksa':
            manifest = bundled[pet_id]
            pet['bundled'] = True
            pet['package'] = None
            pet['bundledAssets'] = 'source/bundled'
            target = folder / 'source/bundled'
            target.mkdir(exist_ok=True)
            for clip in manifest['animations']:
                for part in clip.get('sheets', [clip]):
                    source = REPO / 'wwwroot' / part['file']
                    destination = target / Path(part['file']).name
                    if not destination.exists() or sha(destination) != sha(source):
                        shutil.copyfile(source, destination)
            first_clip = manifest['animations'][0]
            first = first_clip.get('sheets', [first_clip])[0]
            with Image.open(REPO / 'wwwroot' / first['file']) as sheet:
                native_reference = sheet.convert('RGBA').crop((0, 0, first_clip['frameWidth'], first_clip['frameHeight']))
            download = 'https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/Sputnik-1.3.14-Windows-10-11-x64.exe'
        else:
            local = folder / 'exports' / f'{pet_id}.pet'
            source = local if pet_id == 'cloth-ninja' and local.exists() else args.packages / f'{pet_id}.pet'
            checksum = sha(source)
            if pet_id in published:
                assert checksum == published[pet_id]['sha256'], (pet_id, 'published hash mismatch')
                assert source.stat().st_size == published[pet_id]['bytes']
            else:
                assert pet_id == 'cloth-ninja'
                assert checksum == read(folder / 'exports/package-qa.json')['packageSha256']
            destination = folder / 'exports' / f'{pet_id}.pet'
            if source.resolve() != destination.resolve():
                if destination.exists():
                    assert sha(destination) == checksum, (pet_id, 'existing local package differs')
                else:
                    shutil.copyfile(source, destination)
            with ZipFile(destination) as archive:
                assert archive.testzip() is None
                manifest = json.loads(archive.read('manifest.json'))
                first_clip = manifest['animations'][0]
                first = first_clip.get('sheets', [first_clip])[0]
                with Image.open(io.BytesIO(archive.read(first['file']))) as sheet:
                    native_reference = sheet.convert('RGBA').crop((0, 0, first_clip.get('frameWidth', sheet.width), first_clip.get('frameHeight', sheet.height)))
            assert manifest['id'] == pet_id
            assert manifest['theme'] == theme
            download = f'https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/{pet_id}.pet'
            pet['bundled'] = False
            pet['package'] = {'file': f'exports/{pet_id}.pet', 'url': download,
                              'sha256': checksum, 'bytes': destination.stat().st_size,
                              'schemaVersion': manifest['schemaVersion'],
                              'compatibleWindows': '1.3.14',
                              'releaseTag': 'pet-cloth-ninja-v1' if pet_id == 'cloth-ninja' else 'v1.3.14'}
        save(folder / 'package-manifest.json', manifest)
        delivery_reference = folder / ('keyframes/delivery-start.png' if pet_id == 'cloth-ninja' else 'reference.png')
        with Image.open(delivery_reference) as reference:
            assert native_reference.size == reference.size and native_reference.tobytes() == reference.convert('RGBA').tobytes(), (pet_id, 'reference differs from current package first frame')
        assert len(pet['animations']) == len(manifest['animations'])
        for i, item in enumerate(pet['animations']):
            path = folder / item['file']
            animation = read(path)
            clip = manifest['animations'][i]
            assert item['name'] == animation['name'] == clip['name'], (pet_id, i, 'animation name mismatch')
            if pet_id != 'cloth-ninja':
                animation['existingClip'] = clip
            animation.setdefault('delivery', {}).update({'packageAnimationIndex': i, 'manifest': '../../package-manifest.json',
                'source': 'bundled-assets' if pet_id == 'klyaksa' else 'pet-package',
                'preserveNativeResolution': True})
            save(path, animation)
        pet['workspaceSchemaVersion'] = 1
        pet['assetsManifest'] = 'package-manifest.json'
        save(folder / 'pet.json', pet)
        entry = {'id': pet_id, 'name': pet['name'], 'folder': pet_id, 'metadata': f'{pet_id}/pet.json',
                 'reference': f'{pet_id}/reference.png', 'referenceSize': pet['reference']['size'],
                 'theme': f'{pet_id}/theme.json', 'animations': len(pet['animations']),
                 'bundled': pet['bundled'], 'downloadUrl': download,
                 'referencePixelsVerified': True, 'matchingReferenceKeyframesVerified': True}
        if pet['package']:
            entry.update(package=f"{pet_id}/{pet['package']['file']}", sha256=pet['package']['sha256'], bytes=pet['package']['bytes'])
        entries.append(entry)
        print(pet_id, len(pet['animations']), 'reference and native package verified', flush=True)
    assert len(entries) == 7
    save(ROOT / 'catalog.json', {'schemaVersion': 1, 'pets': entries})
    audit = {'pets': entries, 'count': 7,
        'allReferencesNativeRgba': True, 'referenceKeyframesByteIdentical': True,
        'allAnimationMetadataLinked': True, 'allSixPublishedPackagesSha256Verified': True}
    save(ROOT / 'validation.json', audit)
    audit_folder = REPO / 'artifacts/cloth-ninja-publication'
    audit_folder.mkdir(parents=True, exist_ok=True)
    save(audit_folder / 'workspace-audit.json', audit)
    table = ['| Питомец | Референс PNG | Анимации | Загрузка |', '| --- | --- | ---: | --- |']
    for entry in entries:
        w, h = entry['referenceSize']
        table.append(f"| [{entry['name']}]({entry['metadata']}) | [{w}×{h}]({entry['reference']}) | {entry['animations']} | [" + ('В приложении' if entry['bundled'] else '.pet') + f"]({entry['downloadUrl']}) |")
        folder = ROOT / entry['id']
        package_note = 'Встроен в установщик; отдельный пакет не требуется.' if entry['bundled'] else f"Готовый локальный пакет: `exports/{entry['id']}.pet`. [Скачать .pet]({entry['downloadUrl']})."
        (folder / 'README.md').write_text(f"# {entry['name']}\n\n[Референс в исходном качестве, {w}×{h}](reference.png) · [Метаданные](pet.json) · [Тема](theme.json) · [Манифест ресурсов](package-manifest.json)\n\n![Референс](reference.png)\n\nАнимаций: {entry['animations']}. {package_note}\n\nОпорные кадры: `keyframes/start.png` и `keyframes/end.png` — точные копии исходного референса.\nОписания движений: `animations/<id>/animation.json`. Рабочие материалы, обработанные кадры и готовые пакеты: `source/`, `renders/`, `exports/`.\n", encoding='utf-8')
    intro = '# Питомцы и исходные референсы\n\nВсе семь питомцев имеют отдельную папку с референсом, темой, опорными кадрами, описаниями анимаций и манифестом готовых ресурсов. [Машиночитаемый каталог](catalog.json) · [Результат проверки структуры](validation.json).\n\n'
    structure = '''\n\n```text
PET/<id>/
  README.md
  pet.json
  reference.png
  theme.json
  package-manifest.json
  keyframes/start.png
  keyframes/end.png
  animations/<id>/animation.json
  source/
  renders/
  exports/<id>.pet
```

Референсы сохранены как RGBA PNG без изменения исходного разрешения. У шести существующих питомцев референс совпадает с первым нативным кадром актуальных ресурсов. У тканевого ниндзя `reference.png` — исходный референс 941×1671 для генерации; `keyframes/delivery-start.png` и `delivery-end.png` — общая зеркальная поза готовых анимаций 960×960. Исходное изображение не заменяется кадром другого размера.

`start.png` и `end.png` побайтово совпадают с основным референсом каждого питомца. Это опорные кадры для будущих анимаций, а не заявление о совпадении концов всех старых роликов. У тихого ниндзя проверены 10 общих концов; у тканевого — 22.

У тканевого ниндзя готовы 11 согласованных анимаций, включая дополнительный танец. Все итоговые кадры зеркальные; сохранены детали, реквизит, эффекты и мягкая тень. [Описание движений](cloth-ninja/ANIMATIONS.md).

Клякса встроена в приложение: её исходные PNG остаются в `wwwroot/assets/klyaksa`, локальные рабочие копии создаются в `PET/klyaksa/source/bundled`. Шесть отдельных `.pet` лежат локально в `exports/` соответствующих питомцев и опубликованы среди файлов релиза рядом с установщиками. В Git хранятся референсы, JSON и инструкции; тяжёлые видео, AE-мастера, рендеры и пакеты исключены из Git.

Для восстановления локальных пакетов скачайте `.pet` из выпуска 1.3.14 в отдельную папку и выполните `python PET/organize.py --packages <папка>`. Скрипт проверяет SHA256, прозрачность и размеры референсов, совпадение опорных кадров и соответствие всех анимаций. Он не масштабирует изображения и не запускает генерацию.
'''
    (ROOT / 'README.md').write_text(intro + '\n'.join(table) + structure, encoding='utf-8')


if __name__ == '__main__':
    main()
