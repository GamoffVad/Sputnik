"""Append ten native RGBA animations; retain the original five PNG sheets byte for byte."""
from copy import deepcopy
from hashlib import sha256
from pathlib import Path
from zipfile import ZipFile, ZIP_STORED
import argparse
import json

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent.parent
OLD_SHA = 'd39c197d43ad68ddbf44524932ff040e7b6075f2c20c53dc66390e6247e70866'
LIMIT = 512 * 1024 * 1024


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def save(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


def digest(path):
    with path.open('rb') as stream:
        return sha256(stream.read()).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--through', type=int, default=10, help='Build an initial subset of atlases without publishing an incomplete pet.')
    parser.add_argument('--atlases-only', action='store_true', help='Prepare native atlases while alignment QA is still running; do not create a pet package.')
    parser.add_argument('--original', type=Path, default=REPO / 'artifacts/pet-library-published-packages/sand-watchman-clean.pet')
    args = parser.parse_args()
    assert 1 <= args.through <= 10
    assert digest(args.original) == OLD_SHA, 'Original five-animation archive checksum differs'
    plan = read(ROOT / 'new-ten-plan.json')
    alignment_path = ROOT / 'renders/new-ten/alignment.json'
    alignment = read(alignment_path) if alignment_path.exists() else {}
    if args.through == 10 and not args.atlases_only:
        assert alignment.get('approvedForPackaging'), 'Alignment QA has not completed'
    out = ROOT / 'exports'
    atlas_dir = out / 'atlases-new-ten'
    atlas_dir.mkdir(parents=True, exist_ok=True)
    with ZipFile(args.original) as original:
        manifest = json.loads(original.read('manifest.json'))
        assert len(manifest['animations']) == 5
        manifest = deepcopy(manifest)
        manifest.update(schemaVersion=3, name='Песчаный Дозорный',
                        description='Страж в ткани и бронзе: пять прежних и десять новых анимаций. Общая опорная поза, мягкая тень, сохранённые ленты и эффекты.')
        manifest['neutral'] = {'file': 'neutral.png'}
        if alignment.get('neutral', {}).get('viewport'):
            manifest['neutral']['viewport'] = alignment['neutral']['viewport']
        old_viewports = {item['index']: item['viewport'] for item in alignment.get('oldAnimations', [])}
        for index, clip in enumerate(manifest['animations']):
            if index in old_viewports:
                clip['viewport'] = old_viewports[index]
        original_sheets = {part['file']: original.read(part['file'])
                           for clip in manifest['animations'] for part in clip.get('sheets', [clip])}
    new_viewports = {item['id']: item['viewport'] for item in alignment.get('animations', [])}
    report = {'originalPackageSha256': OLD_SHA, 'originalPngBytesPreserved': True,
              'nativeFrameSize': [960, 960], 'fps': 24, 'newFramesPerClip': 96,
              'horizontalFlipAppliedAgain': False, 'clips': []}
    for item in plan['animations'][:args.through]:
        index, clip_id = item['index'], item['id']
        files = sorted((ROOT / 'renders/new-ten' / f'{index:02}-{clip_id}' / 'rgba').glob('frame_*.png'))
        assert len(files) == 96, (clip_id, len(files))
        entry = {'name': item['name'], 'frameWidth': 960, 'frameHeight': 960,
                 'columns': 4, 'frameCount': 96, 'fps': 24, 'sheets': []}
        if clip_id in new_viewports:
            entry['viewport'] = new_viewports[clip_id]
        hashes = []
        for page in range(3):
            filename = f'new-{index:02}-{page}.png'
            path = atlas_dir / filename
            stamp = path.with_suffix('.json')
            source_hashes = [digest(f) for f in files[page * 32:(page + 1) * 32]]
            if not (path.exists() and stamp.exists() and read(stamp).get('sourcePngSha256') == source_hashes):
                atlas = Image.new('RGBA', (3840, 7680))
                for slot in range(32):
                    with Image.open(files[page * 32 + slot]) as frame:
                        assert frame.size == (960, 960) and frame.mode == 'RGBA'
                        atlas.paste(frame, (slot % 4 * 960, slot // 4 * 960))
                atlas.save(path, compress_level=9)
                atlas.close()
                save(stamp, {'sourcePngSha256': source_hashes})
            with Image.open(path) as atlas:
                assert atlas.size == (3840, 7680)
                for slot in range(32):
                    with Image.open(files[page * 32 + slot]) as frame:
                        pixels = np.asarray(frame)
                        x, y = slot % 4 * 960, slot // 4 * 960
                        assert np.array_equal(pixels, np.asarray(atlas.crop((x, y, x + 960, y + 960)))), (clip_id, 'RGBA changed')
                        alpha = pixels[:, :, 3]
                        assert max(alpha[0].max(), alpha[-1].max(), alpha[:, 0].max(), alpha[:, -1].max()) == 0, (clip_id, slot, 'clipped alpha')
                        hashes.append(sha256(pixels.tobytes()).hexdigest())
            entry['sheets'].append({'file': filename, 'frames': 32})
            print(f'{index:02} {filename}: native RGBA verified', flush=True)
        assert hashes[0] == hashes[-1], (clip_id, 'raw endpoints differ')
        assert len(set(hashes)) > 60, (clip_id, 'insufficient motion')
        report['clips'].append({'id': clip_id, 'nativePixelsPreservedInAtlases': True,
                                'frameRgbaSha256': hashes, 'uniqueFrames': len(set(hashes))})
        manifest['animations'].append(entry)
    if args.through < 10 or args.atlases_only:
        save(out / 'partial-packaging-qa.json', report)
        return
    neutral = ROOT / alignment['neutral']['file']
    with Image.open(neutral) as image:
        assert image.mode == 'RGBA' and max(image.size) <= 8192 and image.width * image.height <= 32_000_000
        if alignment['neutral'].get('nativeSize'):
            assert list(image.size) == alignment['neutral']['nativeSize']
        report['neutralNativeSize'] = list(image.size)
        report['neutralOrigin'] = alignment['neutral'].get('origin', alignment['neutral'].get('source', 'shared neutral pose'))
        report['neutralRgbaSha256'] = sha256(np.asarray(image).tobytes()).hexdigest()
    package = out / 'sand-watchman-clean.pet'
    temporary = package.with_suffix('.pet.building')
    with ZipFile(temporary, 'w', compression=ZIP_STORED) as archive:
        archive.writestr('manifest.json', json.dumps(manifest, ensure_ascii=False, indent=2).encode('utf-8'))
        archive.write(neutral, 'neutral.png')
        for filename, contents in original_sheets.items():
            archive.writestr(filename, contents)
        for clip in manifest['animations'][5:]:
            for sheet in clip['sheets']:
                archive.write(atlas_dir / sheet['file'], sheet['file'])
    with ZipFile(temporary) as archive:
        assert archive.testzip() is None and len(archive.infolist()) == 42
        unpacked = sum(item.file_size for item in archive.infolist())
        assert unpacked <= LIMIT, ('Importer limit exceeded; do not downscale', unpacked)
        for filename, contents in original_sheets.items():
            assert archive.read(filename) == contents
    temporary.replace(package)
    report.update(packageSha256=digest(package), packageBytes=package.stat().st_size,
                  unpackedBytes=unpacked, zipEntries=42, oldAnimations=5, newAnimations=10,
                  totalAnimations=15, totalFrames=1560, minimumSputnikVersion='1.3.16')
    save(out / 'manifest.json', manifest)
    save(out / 'package-qa.json', report)
    (out / 'SHA256SUMS.txt').write_text(f"{report['packageSha256']}  sand-watchman-clean.pet\n", encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key != 'clips'}, ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
