"""Remove documented reference blends and compact RGBA atlases without resizing.

Only the 18 frames altered by process_uniform.py's edge<9 block are removed.
Every remaining frame retains its exact alpha and at most 2/255 RGB error.
The installed source is read only. Output must be outside the installed folder.
"""
import argparse, copy, hashlib, json, math, zipfile
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
import numpy as np
from PIL import Image, ImageDraw
from skimage.metrics import structural_similarity

def sha(path):
    with path.open('rb') as stream: return hashlib.file_digest(stream,'sha256').hexdigest()

def build(source, out):
    assert source.resolve() != out.resolve() and not out.resolve().is_relative_to(source.resolve())
    assets=out/'assets'; assets.mkdir(parents=True,exist_ok=True)
    original=json.loads((source/'pet.json').read_text(encoding='utf-8-sig'))
    assert original['id']=='christmas-tree' and len(original['animations'])==15
    ref_hashes={}
    for value in [original.get('neutral')]+[c.get('neutral') for c in original['animations']]:
        if value:
            ref_hashes[value['file']]=hashlib.sha256(Image.open(source/value['file']).convert('RGBA').tobytes()).hexdigest()
    def process(task):
        ci,clip=task
        assert (clip['frameWidth'],clip['frameHeight'],clip['columns'],clip['frameCount'],clip['fps'])==(960,960,4,145,24)
        source_hashes={s['file']:sha(source/s['file']) for s in clip['sheets']}
        retained=[]; pages=[]; alpha_checks=[]; rgba_checks=[]; max_error=0; sample_metrics=[]; frame_index=0
        preview=Image.new('RGB',(310*6,310*4))
        samples=[9,33,57,81,105,135]
        def flush():
            name=f'motion-{ci+1:02}-{len(pages):02}.png'
            atlas=Image.new('RGBA',(3840,960*math.ceil(len(retained)/4)))
            for k,arr in enumerate(retained): atlas.paste(Image.fromarray(arr),((k%4)*960,(k//4)*960))
            atlas.save(assets/name,compress_level=9)
            with Image.open(assets/name) as decoded:
                for k,arr in enumerate(retained):
                    cell=np.array(decoded.crop(((k%4)*960,(k//4)*960,(k%4+1)*960,(k//4+1)*960)))
                    assert hashlib.sha256(cell.tobytes()).hexdigest()==rgba_checks[-len(retained)+k]
                    assert hashlib.sha256(cell[...,3].tobytes()).hexdigest()==alpha_checks[-len(retained)+k]
            pages.append({'file':name,'frames':len(retained)})
            retained.clear()
        for entry in clip['sheets']:
            with Image.open(source/entry['file']) as atlas:
                for local in range(entry['frames']):
                    index=frame_index; frame_index+=1
                    if index<9 or index>=136: continue
                    raw=np.array(atlas.crop(((local%4)*960,(local//4)*960,(local%4+1)*960,(local//4+1)*960)).convert('RGBA'))
                    assert hashlib.sha256(raw.tobytes()).hexdigest() not in ref_hashes.values(), 'Unexpected reference inside source motion'
                    compact=raw.copy(); rgb=compact[...,:3]
                    rgb[:]=np.minimum(((rgb.astype(np.uint16)+2)//4)*4,255).astype(np.uint8)
                    rgb[compact[...,3]==0]=0
                    visible=raw[...,3]>0
                    error=int(np.abs(raw[visible,:3].astype(np.int16)-compact[visible,:3].astype(np.int16)).max())
                    assert error<=2 and np.array_equal(raw[...,3],compact[...,3])
                    max_error=max(max_error,error)
                    alpha_checks.append(hashlib.sha256(raw[...,3].tobytes()).hexdigest())
                    rgba_checks.append(hashlib.sha256(compact.tobytes()).hexdigest())
                    if index in samples:
                        col=samples.index(index)
                        for row,bg in enumerate((0,255)):
                            pair=[]
                            for arr in (raw,compact):
                                base=Image.new('RGBA',(960,960),(bg,bg,bg,255)); base.alpha_composite(Image.fromarray(arr)); pair.append(base.convert('RGB'))
                            for version,img in enumerate(pair): preview.paste(img.resize((310,310),Image.Resampling.LANCZOS),(col*310,(row*2+version)*310))
                            if index==57:
                                a,b=map(np.array,pair)
                                sample_metrics.append({'background':bg,'ssim960':float(structural_similarity(a,b,channel_axis=2,data_range=255)),
                                    'psnr960':float(10*np.log10(255**2/max(float(np.mean((a.astype(float)-b.astype(float))**2)),1e-20)))})
                    retained.append(compact)
                    if len(retained)==32: flush()
        if retained: flush()
        assert len(rgba_checks)==127 and sum(p['frames'] for p in pages)==127
        for name,digest in source_hashes.items(): assert sha(source/name)==digest, 'Source changed'
        preview.save(out/f'motion-{ci+1:02}-comparison.jpg',quality=95)
        updated=copy.deepcopy(clip); updated.pop('neutral',None); updated.pop('file',None)
        updated['frameCount']=127; updated['sheets']=pages
        report={'name':clip['name'],'sourceFrames':145,'frames':127,'retainedSourceRange':[9,135],
            'removedSourceIndices':list(range(9))+list(range(136,145)),
            'alphaIdenticalForAllRetainedFrames':True,'maxVisibleRgbDelta':max_error,
            'sourceFiles':source_hashes,'retainedRgbaSha256':rgba_checks,'retainedAlphaSha256':alpha_checks,
            'sampleMetrics':sample_metrics}
        print(f'{ci+1}/15: 127 frames, exact alpha, max RGB delta {max_error}',flush=True)
        return updated,report
    with ThreadPoolExecutor(max_workers=2) as pool: results=list(pool.map(process,enumerate(original['animations'])))
    manifest={k:copy.deepcopy(v) for k,v in original.items() if k not in ('animations','neutral','schemaVersion')}
    manifest['schemaVersion']=2; manifest['animations']=[c for c,r in results]
    manifest['description']='15 движений ёлки без референсных вставок и смешивания; исходное разрешение и точная прозрачность.'
    (assets/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    package=out/'christmas-tree.pet'
    with zipfile.ZipFile(package,'w',zipfile.ZIP_STORED) as archive:
        archive.write(assets/'manifest.json','manifest.json')
        for clip in manifest['animations']:
            for sheet in clip['sheets']: archive.write(assets/sheet['file'],sheet['file'])
    with zipfile.ZipFile(package) as archive: assert archive.testzip() is None and len(archive.namelist())==61
    report={'animations':15,'originalFrames':2175,'frames':1905,'removedReferenceFrames':270,
        'frameSize':[960,960],'fps':24,'runtimeReferenceBlending':False,'bytes':package.stat().st_size,'sha256':sha(package),
        'alphaIdenticalForAllRetainedFrames':True,'maxVisibleRgbDelta':2,'clips':[r for c,r in results]}
    (out/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='clips'},ensure_ascii=False),flush=True)

def remove_duplicate_bow(out):
    """Keep the greeting bow; omit the second forward-bending motion."""
    assets=out/'assets'
    manifest=json.loads((assets/'manifest.json').read_text(encoding='utf-8'))
    report=json.loads((out/'validation.json').read_text(encoding='utf-8'))
    excluded='Поворот с поклоном'
    assert len(manifest['animations']) in (14,15)
    manifest['animations']=[c for c in manifest['animations'] if c['name']!=excluded]
    assert len(manifest['animations'])==14
    manifest['description']='14 движений ёлки без референсных вставок, смешивания и повторного поклона; исходное разрешение и точная прозрачность.'
    (assets/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    package=out/'christmas-tree.pet'
    with zipfile.ZipFile(package,'w',zipfile.ZIP_STORED) as archive:
        archive.write(assets/'manifest.json','manifest.json')
        for clip in manifest['animations']:
            for sheet in clip['sheets']: archive.write(assets/sheet['file'],sheet['file'])
    with zipfile.ZipFile(package) as archive: assert archive.testzip() is None and len(archive.namelist())==57
    report.update(animations=14,frames=1778,removedReferenceFrames=252,excludedAnimation=excluded,
        excludedAnimationSourceFrames=145,bytes=package.stat().st_size,sha256=sha(package))
    report['clips']=[c for c in report['clips'] if c['name']!=excluded]
    (out/'validation.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='clips'},ensure_ascii=False),flush=True)

if __name__=='__main__':
    parser=argparse.ArgumentParser(); parser.add_argument('source',type=Path); parser.add_argument('out',type=Path)
    args=parser.parse_args(); build(args.source,args.out); remove_duplicate_bow(args.out)
