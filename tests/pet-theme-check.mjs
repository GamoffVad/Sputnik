import fs from 'node:fs';
import vm from 'node:vm';
import assert from 'node:assert/strict';

const source=fs.readFileSync('wwwroot/app.js','utf8');
const selection=source.slice(source.indexOf('function selectPet('),source.indexOf('async function importPet()',source.indexOf('function selectPet(')));
const palette=source.match(/const activeTheme = [\s\S]*?;/)[0];
const pet={id:'christmas-tree',theme:JSON.parse(fs.readFileSync('PET/christmas-tree/theme.json','utf8'))};
const custom={accent:'#D3AE63',background:'#211C16',surface:'#30271F'};
const context=vm.createContext({state:{appearanceMode:'custom',themed:false,customTheme:custom},
  activePet:()=>pet,defaultTheme:{accent:'#A5E4F6'},save(){},render(){},motion:7,hidden:true});
vm.runInContext(selection+'\n'+palette,context);
for(const enabled of [true,false,true]){
  context.enabled=enabled;
  vm.runInContext('selectPet(activePet(),enabled)',context);
  assert.equal(context.state.active,'christmas-tree');
  assert.equal(context.state.appearanceMode,enabled?'pet':'default');
  assert.equal(context.state.themed,enabled);
  assert.equal(vm.runInContext('activeTheme().accent',context),enabled?'#91B879':'#A5E4F6');
  assert.equal(context.state.customTheme,custom);
}
const toggle=source.split('\n').find(line=>line.includes("if(input.id==='theme-toggle')"));
context.input={id:'theme-toggle',checked:true};context.state.appearanceMode='custom';
vm.runInContext(toggle,context);
assert.equal(vm.runInContext('activeTheme().accent',context),'#91B879');
context.input.checked=false;vm.runInContext(toggle,context);
assert.equal(context.state.appearanceMode,'default');
console.log('PASS macOS pet palette: explicit selection and checkbox override custom mode; tree accent applied, saved custom colors retained.');
