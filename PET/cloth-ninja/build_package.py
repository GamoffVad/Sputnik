"""Losslessly package the approved native, mirrored RGBA masters for Sputnik."""
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import argparse
import hashlib
import json

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
SIZE = 960
COUNT = 96
PER_SHEET = 32
COLUMNS = 4
COMMON = 'f3901c6c9fa84a725cc86de7e3799627b6e58d779af0e3213b4e98209abe66ac'
LIMIT = 256 * 1024 * 1024


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, obj):
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--through', type=int, default=11)
    args = parser.parse_args()
    assert 1 <= args.through <= 11
    pet = read(ROOT / 'pet.json')
    assert len(pet['animations']) == 11
    out = ROOT / 'exports'
    atlases = out / 'atlases'
    atlases.mkdir(parents=True, exist_ok=True)
    manifest = {
        'schemaVersion': 2, 'id': 'cloth-ninja', 'name': 'Тканевый ниндзя',
        'description': 'Ловкий и озорной тканевый ниндзя: 11 согласованных анимаций, мягкая тень, сохранённые детали и эффекты. Общие начальные и конечные кадры.',
        'displayScale': 1.3, 'theme': read(ROOT / 'theme.json'), 'animations': [],
    }
    report = {'nativeFrameSize': [SIZE, SIZE], 'horizontalFlip': True,
              'fps': 24, 'framesPerClip': COUNT, 'commonEndpointRgbaSha256': COMMON,
              'losslessAtlasPacking': True, 'clips': []}
    for index, item in enumerate(pet['animations'][:args.through], 1):
        approval = read(ROOT / item['file'])
        assert approval.get('userApproved'), f'Unapproved clip {index}'
        files = sorted((ROOT / 'renders' / f'approved-{index}' / 'rgba-flipped').glob('frame_*.png'))
        assert len(files) == COUNT, (index, len(files))
        hashes = []
        entry = {'name': item['name'], 'frameWidth': SIZE, 'frameHeight': SIZE,
                 'columns': COLUMNS, 'frameCount': COUNT, 'fps': 24, 'sheets': []}
        source_checksums = [hashlib.sha256(f.read_bytes()).hexdigest() for f in files]
        for page in range(3):
            filename = f'clip-{index-1}-{page}.png'
            path = atlases / filename
            stamp = path.with_suffix('.json')
            inputs = source_checksums[page * PER_SHEET:(page + 1) * PER_SHEET]
            cache_ok = path.exists() and stamp.exists() and read(stamp).get('sourcePngSha256') == inputs
            if not cache_ok:
                atlas = Image.new('RGBA', (SIZE * COLUMNS, SIZE * (PER_SHEET // COLUMNS)))
                for slot in range(PER_SHEET):
                    with Image.open(files[page * PER_SHEET + slot]) as im:
                        assert im.size == (SIZE, SIZE) and im.mode == 'RGBA'
                        atlas.paste(im, ((slot % COLUMNS) * SIZE, (slot // COLUMNS) * SIZE))
                atlas.save(path, compress_level=9)
                atlas.close()
                save(stamp, {'sourcePngSha256': inputs})
            with Image.open(path) as decoded:
                assert decoded.size == (3840, 7680) and decoded.mode == 'RGBA'
                for slot in range(PER_SHEET):
                    with Image.open(files[page * PER_SHEET + slot]) as im:
                        original = np.asarray(im)
                        x = slot % COLUMNS * SIZE
                        y = slot // COLUMNS * SIZE
                        recovered = np.asarray(decoded.crop((x, y, x + SIZE, y + SIZE)))
                        assert np.array_equal(original, recovered), (index, page, slot, 'pixels changed')
                        alpha = original[:, :, 3]
                        assert not max(int(alpha[0].max()), int(alpha[-1].max()), int(alpha[:, 0].max()), int(alpha[:, -1].max())), (index, page, slot, 'clipped alpha')
                        hashes.append(hashlib.sha256(original.tobytes()).hexdigest())
            entry['sheets'].append({'file': filename, 'frames': PER_SHEET})
            print(f'{index:02} {filename}: {path.stat().st_size} bytes; all 32 RGBA frames exact', flush=True)
        assert hashes[0] == hashes[-1] == COMMON, (index, 'endpoint mismatch')
        assert len(set(hashes)) > 60, (index, 'insufficient animated frames')
        manifest['animations'].append(entry)
        report['clips'].append({'id': item['id'], 'name': item['name'], 'frames': COUNT,
                                'uniqueFrames': len(set(hashes)), 'frameRgbaSha256': hashes,
                                'firstLastRgbaSha256': COMMON, 'nativePixelsPreservedInAtlases': True})
    if args.through != 11:
        save(out / 'partial-packaging-qa.json', report)
        print('Partial atlas build complete; final pet requires all 11 clips.', flush=True)
        return
    text = json.dumps(manifest, ensure_ascii=False, indent=2) + '\n'
    package = out / 'cloth-ninja.pet'
    save(out / 'manifest.json', manifest)
    with ZipFile(package, 'w', compression=ZIP_STORED) as archive:
        archive.writestr('manifest.json', text.encode('utf-8'))
        for clip in manifest['animations']:
            for sheet in clip['sheets']:
                archive.write(atlases / sheet['file'], sheet['file'])
    with ZipFile(package) as archive:
        assert archive.testzip() is None
        assert len(archive.infolist()) == 34
        unpacked = sum(i.file_size for i in archive.infolist())
        assert unpacked <= LIMIT, ('package exceeds importer limit', unpacked, LIMIT)
    report.update({'totalFrames': COUNT * 11, 'endpointCount': 22,
                   'all121CrossClipJoinsPixelIdentical': True, 'zipEntries': 34,
                   'unpackedBytes': unpacked, 'packageBytes': package.stat().st_size,
                   'packageSha256': hashlib.sha256(package.read_bytes()).hexdigest(),
                   'package': str(package), 'theme': manifest['theme']})
    save(out / 'package-qa.json', report)
    (out / 'SHA256SUMS.txt').write_text(f"{report['packageSha256']}  cloth-ninja.pet\n", encoding='utf-8')
    print(json.dumps({k: v for k, v in report.items() if k != 'clips'}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
