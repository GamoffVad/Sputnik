import {openPicker} from './picker-popover.js';

export function bindFontDropdown(control, fonts, initial, change) {
  let value=initial;
  const names=[...new Set([initial,...fonts])];
  function paint() {
    control.querySelector('span').textContent=value;
    control.style.fontFamily=JSON.stringify(value);
  }
  paint();
  control.onclick=()=>openPicker(control,(panel,close)=>{
    panel.innerHTML='<input class="font-search" aria-label="Поиск шрифта" placeholder="Найти шрифт"><div class="font-options" role="group" aria-label="Установленные шрифты"></div>';
    const search=panel.querySelector('input'), list=panel.lastElementChild;
    function draw() {
      const query=search.value.trim().toLocaleLowerCase();
      list.replaceChildren();
      for(const name of names.filter(name=>name.toLocaleLowerCase().includes(query))){
        const option=document.createElement('button');
        option.type='button';option.className='font-option';option.style.fontFamily=JSON.stringify(name);
        option.setAttribute('aria-label',name);
        option.setAttribute('aria-pressed',String(name===value));
        const label=document.createElement('span');
        label.textContent=name;
        option.append(label);
        option.onclick=()=>{value=name;paint();change(name);close();};
        list.append(option);
      }
      if(!list.children.length){const empty=document.createElement('p');empty.textContent='Шрифты не найдены';list.append(empty);}
    }
    search.oninput=draw;
    panel.onkeydown=event=>{
      if(event.target===search&&event.key==='Enter')event.preventDefault();
      if(!['ArrowDown','ArrowUp'].includes(event.key))return;
      const buttons=[...list.querySelectorAll('button')], index=buttons.indexOf(document.activeElement);
      if(!buttons.length)return;
      event.preventDefault();
      buttons[Math.max(0,Math.min(buttons.length-1,index+(event.key==='ArrowDown'?1:-1)))].focus();
    };
    draw();
    search.focus({preventScroll:true});
  },true);
}
