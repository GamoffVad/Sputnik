import {openPicker} from './picker-popover.js';

const dateFormat = new Intl.DateTimeFormat('ru-RU', {dateStyle:'medium'});
const monthFormat = new Intl.DateTimeFormat('ru-RU', {month:'long',year:'numeric'});
const dayNames = ['Пн','Вт','Ср','Чт','Пт','Сб','Вс'];
const pad = number => String(number).padStart(2,'0');

export function formatDate(value) { return dateFormat.format(value); }
export function formatTime(value) { return pad(value.getHours())+':'+pad(value.getMinutes()); }

export function pickerField(id,label,value,type) {
  const path=type==='date'?'M4 6h12v11H4z M4 9h12 M7 4v4 M13 4v4 M7 12h2 M11 12h2':'M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0z M10 6v4l3 2';
  return '<div class="field picker-field" data-picker-field><span>'+label+'</span><div class="picker-input"><span id="'+id+'-value">'+value+'</span><button type="button" id="'+id+'" aria-label="Выбрать '+(type==='date'?'дату':'время')+'" aria-describedby="'+id+'-value" aria-controls="'+id+'-popover" aria-expanded="false"><svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+path+'"/></svg></button></div><div class="picker-popover" id="'+id+'-popover" hidden></div></div>';
}

export function bindDatePicker(control, initial, change) {
  let selected=new Date(initial);
  control.onclick=()=>openPicker(control,(area,close)=>{
    let cursor=new Date(selected);
    function draw() {
      const year=cursor.getFullYear(), month=cursor.getMonth();
      const offset=(new Date(year,month,1).getDay()+6)%7;
      const days=new Date(year,month+1,0).getDate();
      const numbers=Array.from({length:days},(_,index)=>{
        const day=index+1, value=new Date(year,month,day);
        return '<button type="button" data-day="'+day+'" tabindex="'+(day===cursor.getDate()?0:-1)+'" aria-label="'+formatDate(value)+'" aria-pressed="'+(value.toDateString()===selected.toDateString())+'">'+day+'</button>';
      });
      area.innerHTML='<div class="calendar-heading between"><button type="button" data-month="-1" aria-label="Предыдущий месяц">‹</button><strong aria-live="polite">'+monthFormat.format(cursor)+'</strong><button type="button" data-month="1" aria-label="Следующий месяц">›</button></div><div class="calendar" role="group" aria-label="Дни месяца">'+dayNames.map(day=>'<span>'+day+'</span>').join('')+'<i aria-hidden="true"></i>'.repeat(offset)+numbers.join('')+'</div>';
    }
    function moveMonth(delta) {
      const year=cursor.getFullYear(), month=cursor.getMonth()+delta;
      cursor=new Date(year,month,Math.min(cursor.getDate(),new Date(year,month+1,0).getDate()));
    }
    function focusDay() { area.querySelector('[data-day="'+cursor.getDate()+'"]').focus(); }
    area.onclick=event=>{
      const month=event.target.closest('[data-month]');
      if(month){
        moveMonth(Number(month.dataset.month));
        draw();
        area.querySelector('[data-month="'+month.dataset.month+'"]').focus();
        return;
      }
      const day=event.target.closest('[data-day]');
      if(!day)return;
      selected=new Date(cursor.getFullYear(),cursor.getMonth(),Number(day.dataset.day));
      control.previousElementSibling.textContent=formatDate(selected);
      change(new Date(selected));
      close();
    };
    area.onkeydown=event=>{
      const day=event.target.closest('[data-day]');
      if(!day)return;
      const daySteps={ArrowLeft:-1,ArrowRight:1,ArrowUp:-7,ArrowDown:7};
      cursor.setDate(Number(day.dataset.day));
      if(event.key in daySteps)cursor.setDate(cursor.getDate()+daySteps[event.key]);
      else if(event.key==='PageUp'||event.key==='PageDown')moveMonth((event.key==='PageUp'?-1:1)*(event.shiftKey?12:1));
      else return;
      event.preventDefault();
      draw();focusDay();
    };
    draw();focusDay();
  });
}

export function bindTimePicker(control, initial, change) {
  let hours=initial.getHours(), minutes=initial.getMinutes();
  control.onclick=()=>openPicker(control,(area,close)=>{
    area.innerHTML='<div class="time-picker"><div class="time-parts">'+
      [['hours','Часы',hours],['minutes','Минуты',minutes]].map(([key,label,value])=>
        '<div class="time-part"><label>'+label+'<input name="'+key+'" inputmode="numeric" maxlength="2" value="'+pad(value)+'"></label><div class="time-steps"><button type="button" data-part="'+key+'" data-step="-1" aria-label="'+label+' −1">−</button><button type="button" data-part="'+key+'" data-step="1" aria-label="'+label+' +1">+</button></div></div>').join('')+
      '</div><p role="alert" class="picker-error"></p><div class="actions time-confirm"><button type="button" class="primary" data-apply-time aria-label="Применить время" title="Применить время">✓</button></div></div>';
    const form=area.querySelector('.time-picker');
    const hour=form.querySelector('[name=hours]'), minute=form.querySelector('[name=minutes]');
    function step(field,delta) {
      const limit=field.name==='hours'?24:60;
      field.value=pad(((Number(field.value)||0)+delta+limit)%limit);
    }
    form.onclick=event=>{
      const stepButton=event.target.closest('[data-step]');
      if(stepButton)step(stepButton.dataset.part==='hours'?hour:minute,Number(stepButton.dataset.step));
      if(event.target.closest('[data-apply-time]'))applyTime();
    };
    form.onkeydown=event=>{
      if(event.target.tagName!=='INPUT')return;
      if(event.key==='Enter'){event.preventDefault();applyTime();return;}
      if(!['ArrowUp','ArrowDown'].includes(event.key))return;
      event.preventDefault();
      step(event.target,event.key==='ArrowUp'?1:-1);
    };
    function applyTime() {
      const nextHours=Number(hour.value), nextMinutes=Number(minute.value);
      if(!/^\d{1,2}$/.test(hour.value)||!/^\d{1,2}$/.test(minute.value)||nextHours>23||nextMinutes>59){form.querySelector('.picker-error').textContent='Часы от 00 до 23, минуты от 00 до 59.';return;}
      hours=nextHours;minutes=nextMinutes;
      control.previousElementSibling.textContent=pad(hours)+':'+pad(minutes);
      change(hours,minutes);
      close();
    }
    hour.focus();
    hour.select();
  });
}
