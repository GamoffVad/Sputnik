import {openPicker} from './picker-popover.js';

const valid = /^#[0-9a-f]{6}$/i;
const presets = ['#181c20','#2b2434','#ead8ff','#a5e4f6','#77c4a3','#f2ba78','#f29aa3','#f4f6fb'];

export function bindColorPicker(control, initial, change) {
  let value=initial;
  const swatch=control.querySelector('i'), caption=control.querySelector('span');
  function paint() {swatch.style.backgroundColor=value;caption.textContent=value.toUpperCase();}
  paint();
  control.onclick=()=>openPicker(control,(panel,close)=>{
    panel.innerHTML='<div class="color-presets" role="group" aria-label="Готовые цвета"></div>'+
      '<div class="color-sliders">'+['Красный','Зелёный','Синий'].map((label,i)=>
        '<label><span>'+label+'</span><input type="range" min="0" max="255" step="1" data-channel="'+i+'"></label>').join('')+'</div>'+
      '<div class="color-value"><label>HEX<input class="color-hex" spellcheck="false" maxlength="7" autocomplete="off"></label><button type="button" aria-label="Закрыть выбор цвета" title="Закрыть">×</button></div>';
    const palette=panel.querySelector('.color-presets');
    const hex=panel.querySelector('.color-hex');
    const channels=[...panel.querySelectorAll('[data-channel]')];
    for(const color of presets){
      const sample=document.createElement('button');
      sample.type='button';sample.title=color;
      sample.setAttribute('aria-label',color);
      sample.style.backgroundColor=color;
      sample.onclick=()=>{setColor(color);sync();};
      palette.append(sample);
    }
    function setColor(color) {
      if(value.toLowerCase()===color.toLowerCase())return;
      value=color.toUpperCase();
      paint();change(value);
    }
    function syncSliders() {
      const rgb=value.slice(1).match(/../g).map(channel=>parseInt(channel,16));
      channels.forEach((slider,i)=>{
        slider.value=rgb[i];
        const low=[...rgb],high=[...rgb];
        low[i]=0;high[i]=255;
        slider.style.setProperty('--track-start','rgb('+low.join(',')+')');
        slider.style.setProperty('--track-end','rgb('+high.join(',')+')');
      });
      for(const sample of palette.children)sample.setAttribute('aria-pressed',String(sample.title.toLowerCase()===value.toLowerCase()));
    }
    function sync(){hex.value=value.toUpperCase();hex.removeAttribute('aria-invalid');syncSliders();}
    for(const slider of channels)slider.oninput=()=>{
      setColor('#'+channels.map(item=>Number(item.value).toString(16).padStart(2,'0')).join(''));
      sync();
    };
    hex.oninput=()=>{
      if(valid.test(hex.value)){hex.removeAttribute('aria-invalid');setColor(hex.value);syncSliders();}
      else hex.setAttribute('aria-invalid','true');
    };
    hex.onkeydown=event=>{if(event.key==='Enter'){event.preventDefault();if(valid.test(hex.value))close();}};
    panel.querySelector('.color-value button').onclick=()=>close();
    sync();
    hex.focus({preventScroll:true});
  },true);
}
