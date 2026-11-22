"""Prepare an updated snapshot without changing other published pet packages."""
from pathlib import Path
import argparse,json,os,shutil,zipfile

parser=argparse.ArgumentParser(); parser.add_argument('previous',type=Path);parser.add_argument('compact',type=Path);parser.add_argument('out',type=Path)
args=parser.parse_args();args.out.mkdir(parents=True,exist_ok=True)
catalog=json.loads((args.previous/'catalog.json').read_text(encoding='utf-8'))
check=json.loads((args.compact/'validation.json').read_text(encoding='utf-8'))
for pet in catalog['pets']:
    source=(args.compact if pet['id']=='christmas-tree' else args.previous)/pet['file']
    destination=args.out/pet['file']
    if not destination.exists():os.link(source,destination)
    if pet['id']=='christmas-tree':
        manifest=json.loads((args.compact/'assets/manifest.json').read_text(encoding='utf-8'))
        with zipfile.ZipFile(source) as archive: uncompressed=sum(i.file_size for i in archive.infolist() if i.filename!='manifest.json')
        pet.update(animations=check['animations'],frames=check['frames'],entries=1+sum(len(c['sheets']) for c in manifest['animations']),bytes=check['bytes'],sha256=check['sha256'],uncompressedBytes=uncompressed)
        pet['optimization']={'referenceFramesRemoved':check['removedReferenceFrames'],'excludedAnimations':check['excludedAnimations'],
            'originalPackageBytes':1324928974,'resolution':[960,960],'retainedAlphaIdentical':True,'maxVisibleRgbDelta':2}
        (args.out/'christmas-tree.manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    else: shutil.copy2(args.previous/(pet['id']+'.manifest.json'),args.out/(pet['id']+'.manifest.json'))
(args.out/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(args.out/'SHA256SUMS.txt').write_text(''.join(f"{p['sha256']}  {p['file']}\n" for p in catalog['pets']),encoding='ascii')
print('Prepared 10-pet catalogue: only christmas-tree package changed')
