"""Derive an isolated schema-3 import fixture from bundled public assets."""
import base64,json,sys,zipfile
from pathlib import Path
source,output=map(Path,sys.argv[1:])
with zipfile.ZipFile(source) as old, zipfile.ZipFile(output,'w',zipfile.ZIP_STORED) as new:
    manifest=json.loads(old.read('manifest.json'))
    manifest['id']='mac-import-fixture'
    manifest['schemaVersion']=3
    manifest['neutral']={'file':'neutral.png','blendFrames':8}
    manifest['animations'][0]['viewport']={'x':0,'y':0,'width':384,'height':384}
    new.writestr('manifest.json',json.dumps(manifest,ensure_ascii=False))
    new.writestr('neutral.png',base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=='))
    for clip in manifest['animations']:
        new.writestr(clip['file'],old.read(clip['file']))
