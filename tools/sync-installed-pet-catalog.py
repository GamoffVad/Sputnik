"""Export the visible installed pet library; publish assets only, never user state."""
import argparse
import copy
import hashlib
import json
import re
import shutil
import zipfile
from pathlib import Path
from PIL import Image

def save(path, data):
    path.parent.mkdir(parents=True,exist_ok=True)
    path.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')

def sha(path):
    with path.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()

def export(args):
    out=args.out;out.mkdir(parents=True,exist_ok=True)
    wwwroot=args.installed_root/'wwwroot'
    builtin=json.loads((wwwroot/'assets/pets.json').read_text(encoding='utf-8-sig'))
    pets={p['id']:(p,wwwroot,True) for p in builtin}
    for path in sorted((args.data_root/'pets').glob('*/pet.json')):
        p=json.loads(path.read_text(encoding='utf-8-sig'))
        assert p['id']==path.parent.name
        if p['id'] not in pets:pets[p['id']]=(p,path.parent,False)
    result=[]
    for ident,(original,base,bundled) in sorted(pets.items()):
        assert re.fullmatch(r'[a-z0-9][a-z0-9-]{0,48}',ident)
        manifest={k:copy.deepcopy(original[k]) for k in ('id','name','description','theme','displayScale','neutral','animations') if k in original}
        manifest['schemaVersion']=3 if manifest.get('neutral') else 2
        inputs={};dimensions={}
        def asset(obj):
            relative=obj['file']
            path=(base/relative).resolve()
            assert path.is_relative_to(base.resolve()) and path.is_file() and path.suffix.lower()=='.png'
            if relative not in inputs:
                name=f'asset-{len(inputs):03d}.png'
                with Image.open(path) as im:
                    assert im.width<=8192 and im.height<=8192 and im.width*im.height<=32_000_000
                    dimensions[relative]=im.size
                inputs[relative]={'name':name,'path':path,'bytes':path.stat().st_size,'sha256':sha(path)}
            obj['file']=inputs[relative]['name']
        if manifest.get('neutral'):asset(manifest['neutral'])
        for clip in manifest['animations']:
            if clip.get('neutral'):asset(clip['neutral'])
            for sheet in clip.get('sheets',[clip]):
                original_file=sheet['file'];asset(sheet)
                w,h=dimensions[original_file]
                count=sheet.get('frames',clip['frameCount'])
                assert (w,h)==(clip['frameWidth']*clip['columns'],clip['frameHeight']*((count+clip['columns']-1)//clip['columns']))
            assert not clip.get('sheets') or sum(s['frames'] for s in clip['sheets'])==clip['frameCount']
        dest=out/f'{ident}.pet'
        with zipfile.ZipFile(dest,'w',zipfile.ZIP_STORED,allowZip64=True) as z:
            def entry(name):
                info=zipfile.ZipInfo(name,(2026,11,22,0,0,0));info.compress_type=zipfile.ZIP_STORED
                return info
            z.writestr(entry('manifest.json'),json.dumps(manifest,ensure_ascii=False,indent=2).encode('utf-8'))
            for item in inputs.values():
                with item['path'].open('rb') as source,z.open(entry(item['name']),'w',force_zip64=True) as target:
                    shutil.copyfileobj(source,target,1024*1024)
        with zipfile.ZipFile(dest) as z:
            for item in inputs.values():
                with z.open(item['name']) as source:
                    assert hashlib.file_digest(source,'sha256').hexdigest()==item['sha256']
                assert sha(item['path'])==item['sha256'],'Installed asset changed during snapshot'
        record={'id':ident,'name':manifest['name'],'animations':len(manifest['animations']),
                'frames':sum(c['frameCount'] for c in manifest['animations']),
                'bundled':bundled,'file':dest.name,'bytes':dest.stat().st_size,'sha256':sha(dest),
                'entries':len(inputs)+1,'sourceAssetsByteIdentical':True,
                'uncompressedBytes':sum(a['bytes'] for a in inputs.values()),
                'downloadUrl':f'https://github.com/GamoffVad/Sputnik/releases/download/{args.tag}/{dest.name}'}
        result.append(record)
        save(out/f'{ident}.manifest.json',manifest)
        print(json.dumps(record,ensure_ascii=False),flush=True)
    save(out/'catalog.json',{'schemaVersion':1,'snapshotDate':'2026-11-22','releaseTag':args.tag,'pets':result})
    (out/'SHA256SUMS.txt').write_text(''.join(f"{p['sha256']}  {p['file']}\n" for p in result),encoding='ascii')
    return result

def update_catalog(args):
    data=json.loads((args.out/'catalog.json').read_text(encoding='utf-8'))
    root=args.catalog_root
    wanted={p['id'] for p in data['pets']}
    removed=[]
    for path in root.iterdir():
        if path.is_dir() and (path/'pet.json').exists() and path.name not in wanted:
            assert path.resolve().parent==root.resolve()
            shutil.rmtree(path);removed.append(path.name)
    entries=[]
    for record in data['pets']:
        ident=record['id'];folder=root/ident;folder.mkdir(exist_ok=True)
        manifest=json.loads((args.out/f'{ident}.manifest.json').read_text(encoding='utf-8'))
        with zipfile.ZipFile(args.out/record['file']) as z:
            def frame(clip,index):
                local=index
                for sheet in clip.get('sheets',[{'file':clip['file'],'frames':clip['frameCount']}] if 'sheets' not in clip else clip['sheets']):
                    if local<sheet['frames']:
                        import io
                        image=Image.open(io.BytesIO(z.read(sheet['file']))).convert('RGBA')
                        w,h=clip['frameWidth'],clip['frameHeight'];x,y=(local%clip['columns'])*w,(local//clip['columns'])*h
                        return image.crop((x,y,x+w,y+h))
                    local-=sheet['frames']
                raise ValueError('Missing frame')
            first=frame(manifest['animations'][0],0)
            last=frame(manifest['animations'][0],manifest['animations'][0]['frameCount']-1)
            first.save(folder/'reference.png')
            (folder/'keyframes').mkdir(exist_ok=True)
            first.save(folder/'keyframes/start.png');last.save(folder/'keyframes/end.png')
        save(folder/'theme.json',manifest['theme']);save(folder/'package-manifest.json',manifest)
        animations=folder/'animations'
        if animations.exists():
            assert animations.resolve().parent==folder.resolve();shutil.rmtree(animations)
        items=[]
        for index,clip in enumerate(manifest['animations']):
            action=f'action-{index+1:02d}'
            save(animations/action/'animation.json',{'id':action,'name':clip['name'],'status':'installed-animation',
                 'existingClip':clip,'delivery':{'packageAnimationIndex':index,'manifest':'../../package-manifest.json',
                 'preserveNativeResolution':True,'sourceAssetBytesVerified':True},
                 'runtimeReferenceBlending':bool(clip.get('neutral',manifest.get('neutral'))),
                 'durationSeconds':clip['frameCount']/clip['fps']})
            items.append({'id':action,'name':clip['name'],'file':f'animations/{action}/animation.json'})
        same=first.tobytes()==last.tobytes()
        save(folder/'pet.json',{'id':ident,'name':record['name'],'status':'available','bundled':record['bundled'],
             'reference':{'file':'reference.png','size':list(first.size),'origin':'First native frame of the currently installed animation'},
             'theme':'theme.json','keyframes':{'start':'keyframes/start.png','end':'keyframes/end.png','filesIdentical':same},
             'package':{'file':f'exports/{record["file"]}','url':record['downloadUrl'],'sha256':record['sha256'],
                        'bytes':record['bytes'],'schemaVersion':manifest['schemaVersion'],'releaseTag':args.tag},
             'animations':items,'workspaceSchemaVersion':1,'assetsManifest':'package-manifest.json'})
        for name in ('source','renders','exports'):
            path=folder/name;path.mkdir(exist_ok=True);(path/'.gitkeep').touch()
        (folder/'SHA256SUMS.txt').write_text(f"{record['sha256']}  {record['file']}\n",encoding='ascii')
        (folder/'README.md').write_text(f"# {record['name']}\n\n[Скачать `.pet`]({record['downloadUrl']}) · [Манифест](package-manifest.json) · [Тема](theme.json)\n\n"
            f"Актуальная установленная версия: **{record['animations']} анимаций, {record['frames']} кадров**. "
            +("Ёлка оптимизирована без изменения разрешения и альфа-канала оставленных кадров; RGB отличается максимум на 2/255. Референсные вставки и повторный поклон удалены. [Параметры оптимизации](optimization.json).\n\n" if record.get('optimization') else
            "PNG-ресурсы перенесены побайтово, без изменения размера, цвета и прозрачности.\n\n")+
            "Импорт: **Питомцы → плюс → файл `.pet` → Установить питомца**. Для полного набора рекомендуется Windows-версия Спутника 1.3.28.27 или новее.\n\n"
            +('Клякса также встроена в приложение.\n\n' if record['bundled'] else '')
            +('Общий референс и переходные подстановки отключены. В пакете только исходные кадры движений.\n\n' if ident=='black-haired-doll' else '')
            +'\n'.join(f"{i+1}. {a['name']}" for i,a in enumerate(manifest['animations']))+'\n',encoding='utf-8')
        entries.append({**record,'folder':ident,'metadata':f'{ident}/pet.json','reference':f'{ident}/reference.png',
                        'referenceSize':list(first.size),'theme':f'{ident}/theme.json','package':f'{ident}/exports/{record["file"]}'})
    save(root/'catalog.json',{'schemaVersion':1,'snapshotDate':'2026-11-22','releaseTag':args.tag,'pets':entries})
    save(root/'validation.json',{'schemaVersion':1,'count':len(entries),'installedLibraryMatchesCatalog':True,
         'removedPets':removed,'allPackageAssetsByteVerified':True,'minimumWindowsVersion':'1.3.28.27',
         'pets':[{k:p[k] for k in ('id','animations','frames','sha256','bytes','entries')} for p in entries]})
    text='# Питомцы «Спутника»\n\nКаталог соответствует установленной библиотеке на 22 ноября 2026 года: **10 питомцев**, включая встроенную Кляксу.\n\n'
    text+='[Спутник для Windows](https://github.com/GamoffVad/Sputnik/releases/tag/v1.3.28.27) · [Спутник для macOS](https://github.com/GamoffVad/Sputnik/releases/tag/macos-v0.2.5) · [Каталог JSON](catalog.json) · [Проверка](validation.json)\n\n'
    text+='Скачайте `.pet`, затем выберите **Питомцы → плюс → файл → Установить питомца**. Все PNG перенесены из установленной библиотеки побайтово. Настройки, заметки и данные подключения в пакеты не входят.\n\n'
    text+='| Питомец | Анимации | Кадры | Скачать |\n| --- | ---: | ---: | --- |\n'
    text+=''.join(f"| [{p['name']}]({p['id']}/README.md) | {p['animations']} | {p['frames']} | [.pet]({p['downloadUrl']}) |\n" for p in entries)
    text+='\nДля больших наборов используйте Windows-версию 1.3.28.27 или macOS 0.2.5 и новее. В macOS проверены сегментированные RGBA-листы, обновление питомца и пакеты без референса на Intel и Apple Silicon; каждый большой пакет отдельно на Mac не проверялся.\n\n'
    if any(p.get('optimization') for p in entries):text+='Ёлка уменьшена на 43,5%, содержит 14 движений без референсных вставок и повторного поклона. [Подробности](christmas-tree/README.md).\n\n'
    text+='У брюнетки отключены референтные вставки и смешивания. В каждом её движении сохранены 97 исходных кадров.\n\nТяжёлые `.pet` публикуются в [едином выпуске библиотеки](https://github.com/GamoffVad/Sputnik/releases/tag/'+args.tag+'); в Git хранятся манифесты, референсы и описания. Старые выпуски доступны в истории.\n'
    (root/'README.md').write_text(text,encoding='utf-8')

if __name__=='__main__':
    p=argparse.ArgumentParser();p.add_argument('--installed-root',type=Path,required=True);p.add_argument('--data-root',type=Path,required=True)
    p.add_argument('--out',type=Path,required=True);p.add_argument('--tag',default='pets-2026-11-22');p.add_argument('--catalog-root',type=Path)
    args=p.parse_args()
    if args.catalog_root:update_catalog(args)
    else:export(args)
