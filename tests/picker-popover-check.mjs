import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

let height=80,resize,disconnected=false;
const events=new Map();
const panel={hidden:true,style:{},classList:{contains:name=>name==='model-popover'},replaceChildren(){},removeAttribute(){this.style={};},
  getBoundingClientRect(){return {width:Number.parseFloat(this.style.width)||300,height};}};
const field={querySelector:()=>panel,closest:()=>({}),contains:()=>true,isConnected:true};
const attributes={};
const control={isConnected:true,onclick(){},closest:()=>field,setAttribute:(name,value)=>attributes[name]=value,focus(){},
  getBoundingClientRect:()=>({left:60,top:240,bottom:270,width:300})};
const context={AbortController,innerWidth:400,innerHeight:300,
  document:{addEventListener:(name,fn)=>events.set(name,fn)},window:{addEventListener:(name,fn)=>events.set(name,fn)},
  MutationObserver:class {observe(){}disconnect(){}},ResizeObserver:class {constructor(callback){resize=callback;}observe(){}disconnect(){disconnected=true;}}};
const source=fs.readFileSync('wwwroot/components/picker-popover.js','utf8').replace('export function','function');
vm.runInNewContext(source+'\nthis.openPicker=openPicker;',context);
let close;context.openPicker(control,(_,done)=>{close=done;},true);
assert.equal(attributes['aria-expanded'],'true');assert.equal(panel.hidden,false);
assert.equal(panel.style.position,'fixed');assert.equal(panel.style.width,'300px');
height=260;resize();
let top=Number.parseFloat(panel.style.top);
assert(top>=8&&top+height<=292,'loaded catalog must remain fully visible');
context.innerWidth=240;events.get('resize')();
assert(Number.parseFloat(panel.style.width)<=224,'popover exceeds narrow viewport');
close();assert.equal(panel.hidden,true);assert.equal(attributes['aria-expanded'],'false');assert(disconnected);
console.log('PASS picker popover: click opens fixed overlay, async catalog resize stays within viewport, narrow width and observer cleanup.');
