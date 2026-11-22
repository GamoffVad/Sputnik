"""Preview every retained frame before/after on black and white at desktop size."""
from pathlib import Path
from fractions import Fraction
import json,sys
import av
import numpy as np
from PIL import Image,ImageDraw,ImageFont

source,out=map(Path,sys.argv[1:])
old=json.loads((source/'pet.json').read_text(encoding='utf-8-sig'))
new=json.loads((out/'assets/manifest.json').read_text(encoding='utf-8'))
def frames(root,clip):
    for sheet in clip['sheets']:
        with Image.open(root/sheet['file']) as atlas:
            for i in range(sheet['frames']):
                yield atlas.crop(((i%4)*960,(i//4)*960,(i%4+1)*960,(i//4+1)*960)).convert('RGBA')
container=av.open(str(out/f"all-{len(new['animations'])}-black-white-comparison.mp4"),'w')
stream=container.add_stream('libx264',rate=24);stream.width=620;stream.height=656;stream.pix_fmt='yuv420p'
stream.options={'crf':'15','preset':'fast','threads':'2'}
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',16)
index=0;seams=[]
for clip in new['animations']:
    original=next(c for c in old['animations'] if c['name']==clip['name'])
    prior=frames(source,original)
    for _ in range(9):next(prior)
    first=None;last=None
    for local,(before,after) in enumerate(zip(prior,frames(out/'assets',clip))):
        canvas=Image.new('RGB',(620,656),(34,34,34));draw=ImageDraw.Draw(canvas)
        draw.text((6,4),clip['name'],font=font,fill='white')
        draw.text((6,23),'До сжатия',font=font,fill='white');draw.text((316,23),'После сжатия',font=font,fill='white')
        for row,bg in enumerate((0,255)):
            for col,image in enumerate((before,after)):
                base=Image.new('RGBA',(960,960),(bg,bg,bg,255));base.alpha_composite(image)
                base=base.convert('RGB').resize((310,310),Image.Resampling.LANCZOS)
                canvas.paste(base,(col*310,36+row*310))
        rendered=np.array(canvas)
        if first is None:first=rendered.copy()
        last=rendered
        frame=av.VideoFrame.from_ndarray(rendered,format='rgb24');frame.pts=index;frame.time_base=Fraction(1,24)
        for packet in stream.encode(frame):container.mux(packet)
        index+=1
    seams.append({'name':clip['name'],'firstLastMeanRgbDeltaAt310':float(np.abs(first[36:].astype(float)-last[36:].astype(float)).mean())})
    print('Previewed',clip['name'],flush=True)
for packet in stream.encode():container.mux(packet)
container.close()
assert index==sum(c['frameCount'] for c in new['animations'])
decoded=0
with av.open(str(out/f"all-{len(new['animations'])}-black-white-comparison.mp4")) as video:
    for frame in video.decode(video=0):decoded+=1
assert decoded==index
(out/'preview-report.json').write_text(json.dumps({'framesWritten':index,'framesDecoded':decoded,'clips':seams},ensure_ascii=False,indent=2),encoding='utf-8')
print(f'PASS preview: all {index} frames decode',flush=True)
