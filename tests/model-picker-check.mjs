import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const source=fs.readFileSync('wwwroot/app.js','utf8');
const code=source.slice(source.indexOf('function modelValues('),source.indexOf("document.addEventListener('submit'",source.indexOf('function modelValues(')));
function fixture(reply) {
  const element=(value='')=>({value,disabled:false,checked:false,hidden:false,required:false,addEventListener(){},focus(){}});
  const form={isConnected:true,elements:{url:element('https://api.deepseek.com/v1'),key:element('synthetic-test-key'),provider:element(),clearKey:element(),model:element(),manualModel:element()}};
  const nodes={'#model-form':form,'#manual-model':element(),'#model-picker':element(),'#model-choice':element(),'#model-status':element()};
  const calls=[];
  const context={main:{},model:{},$:selector=>nodes[selector],modelProviderFor:()=>'',openPicker(){},
    FormData:class {constructor(form){this.entries=Object.entries(form.elements).filter(([key,value])=>!value.disabled&&key!=='provider'&&(key!=='clearKey'||value.checked)).map(([key,value])=>[key,value.value]);}[Symbol.iterator](){return this.entries[Symbol.iterator]();}},
    call:async(method,args)=>{calls.push({method,args});return reply(args,form);}};
  vm.runInNewContext(code+'\nbindModelSettings();',context);
  return {form,calls,nodes};
}
let f=fixture(()=>({models:['deepseek-flash','deepseek-v4-pro']}));
await f.form.ensureModel();
assert.equal(f.form.elements.model.value,'deepseek-flash');
assert.equal(f.nodes['#model-choice'].textContent,'deepseek-flash');
assert.equal(f.calls.length,1);assert.equal(f.calls[0].args.key,'synthetic-test-key');
await f.form.ensureModel();assert.equal(f.calls.length,1,'selected model should not reload catalog');
f=fixture(()=>({models:[]}));await assert.rejects(()=>f.form.ensureModel(),/вручную/);
assert.equal(f.form.elements.manualModel.disabled,false);
f=fixture(()=>{throw new Error('HTTP 401');});await assert.rejects(()=>f.form.ensureModel(),/401/);
assert.equal(f.form.elements.model.value,'');
f=fixture((_,form)=>{form.elements.key.value='changed';return {models:['a']};});
await assert.rejects(()=>f.form.ensureModel(),/Параметры изменились/);
assert.equal(f.form.elements.model.value,'');
console.log('PASS model picker: catalog-based selection without model ID, selected model preserved, empty catalog/manual entry, actual API errors and changed credentials rejected. No network calls.');
