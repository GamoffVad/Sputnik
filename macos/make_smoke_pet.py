"""Create deterministic segmented RGBA import/update fixtures using only stdlib."""
import json
from pathlib import Path
import struct
import sys
import zipfile
import zlib
source, output = map(Path, sys.argv[1:])
catalog = json.loads(source.read_text(encoding="utf-8-sig"))
assert [p['id'] for p in catalog] == ['klyaksa']
def png(width, height, tint):
    def chunk(kind, data):
        return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind+data))
    rows = b''.join(b'\0' + bytes(v for x in range(width) for v in
        (tint, x*7 % 256, y*13 % 256, (x+y)*31 % 256)) for y in range(height))
    return b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', width, height, 8, 6, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b'')
def create(path, updated):
    manifest = {'schemaVersion':3, 'id':'mac-import-fixture',
        'name':'Updated import fixture' if updated else 'Import fixture',
        'theme':catalog[0]['theme'], 'neutral':{'file':'neutral.png', 'blendFrames':8}, 'animations':[]}
    assets = {} if updated else {'neutral.png':png(4,4,150), 'clip-neutral.png':png(4,4,170)}
    frames = 127 if updated else 145
    if updated:
        manifest['schemaVersion'] = 2
        manifest.pop('neutral')
    for clip in range(15):
        sheets = []
        for page, start in enumerate(range(0,frames,16)):
            count = min(16,frames-start)
            name = f'motion-{clip}-{page}.png'
            assets[name] = png(16, ((count+3)//4)*4, 220 if updated else 90)
            sheets.append({'file':name, 'frames':count})
        animation = {'name':f'Motion {clip+1}', 'frameWidth':4, 'frameHeight':4, 'columns':4,
            'frameCount':frames, 'fps':24, 'sheets':sheets, 'viewport':{'x':0,'y':0,'width':4,'height':4}}
        if clip == 0 and not updated: animation['neutral'] = {'file':'clip-neutral.png', 'blendFrames':0}
        manifest['animations'].append(animation)
    path.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(path,'w',zipfile.ZIP_STORED) as archive:
        archive.writestr('manifest.json',json.dumps(manifest))
        for name, data in assets.items(): archive.writestr(name,data)
    assert len(assets)+1 == (121 if updated else 153)
create(output,False)
create(output.with_name(output.stem+'-update.pet'),True)
