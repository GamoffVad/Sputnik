import {bindDatePicker,bindTimePicker,formatDate,formatTime,pickerField} from './components/date-time-pickers.js';
import {bindColorPicker} from './components/color-picker.js';
import {bindFontDropdown} from './components/font-dropdown.js';

const $ = (selector, root = document) => root.querySelector(selector);
const main = $('#main');
const dialog = $('#dialog');
const handlers = new Map();
const bridge = window.chrome?.webview;
let serial = 0;
bridge?.addEventListener('message', ({data}) => {
  if (Array.isArray(data.history)) {
    state.messages=data.history;
    if (desktopSection==='chat') render();
    return;
  }
  if (typeof data.section === 'string') {
    openSection(data.section);
    return;
  }
  const request = handlers.get(data.id);
  if (!request) return;
  handlers.delete(data.id);
  data.ok ? request.resolve(data.value) : request.reject(new Error(data.error));
});
function call(action, payload = null) {
  if (!bridge) return Promise.reject(new Error('Откройте приложение Спутник в Windows.'));
  return new Promise((resolve, reject) => {
    const id = String(++serial);
    handlers.set(id, {resolve, reject});
    bridge.postMessage({id, action, payload});
  });
}
const escape = (value = '') => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const id = () => crypto.randomUUID();
const date = value => new Intl.DateTimeFormat('ru-RU', {dateStyle:'medium',timeStyle:'short'}).format(value);
const defaultTheme = {name:'Графит',accent:'#a5e4f6',background:'#181c20',surface:'#23292f'};
let builtins = [];
const routes = [['home','Главная','house'],['pets','Питомцы','heart-pulse'],['notes','Заметки','notebook-pen'],['favorites','Избранное','bookmark'],['reminders','Напоминания','bell'],['chat','Чат','message-circle'],['settings','Настройки','settings']];
let state = {active:'hudi',themed:true,notes:[],favorites:[],reminders:[],messages:[]};
let pets = builtins;
let groups = [];
let glyphs = new Map();
let route = 'home', tab = 'pets', currentNote, kind = 'site', motion = 0, paused = matchMedia('(prefers-reduced-motion: reduce)').matches;
let quick = false, menu = false, hidden = false, busy = false, chatError = '', noticeId = null, dismissed = new Set(), draft = '';
let model = {};
let desktopSection = null, desktopMotions = [], autoStart = false;
let shareChannels = [];
let appearanceFonts = ['Segoe UI'];
let hotkeys = {sections:[],bindings:{}};
let sectionRevision = 0;
let subpage = false;
let noteEditing = false;
const pageNumbers = new Map();
let saveTimer, feedbackTimer;
const activePet = () => pets.find(p => p.id === state.active) ?? builtins[0];
const glyph = key => glyphs.get(key)?.svg ?? '';
function feedback(text) {
  $('#feedback').textContent = text;
  clearTimeout(feedbackTimer);
  feedbackTimer = setTimeout(() => { $('#feedback').textContent = ''; }, 4800);
}
function save() {
  call('save', structuredClone(state)).catch(error => feedback(error.message));
}
function go(page, settingsTab = tab) {
  route = page; tab = settingsTab; menu = false; quick = false;
  render(); main.focus({preventScroll:true});
}
const activeTheme = () => state.appearanceMode==='custom' && state.customTheme ? state.customTheme :
  state.themed ? activePet().theme : defaultTheme;
function theme(t = activeTheme(), font = state.font) {
  for (const key of ['accent','background','surface']) document.documentElement.style.setProperty('--'+key,t[key]);
  const ink = color => {
    const rgb = color.match(/[0-9a-f]{2}/gi).map(hex => parseInt(hex,16)/255)
      .map(channel => channel <= .04045 ? channel/12.92 : ((channel+.055)/1.055)**2.4);
    return rgb[0]*.2126+rgb[1]*.7152+rgb[2]*.0722 > .179 ? '#000' : '#fff';
  };
  const style=document.documentElement.style;
  style.setProperty('--accent-ink',ink(t.accent));
  style.setProperty('--ink',ink(t.surface));
  style.setProperty('--field-ink',ink(t.background));
  style.setProperty('--app-font',JSON.stringify(font||'Segoe UI'));
  document.documentElement.style.colorScheme=ink(t.surface)==='#000'?'light':'dark';
}
function button(title, attributes = '', icon = '') { return '<button '+attributes+'>'+glyph(icon)+'<span>'+escape(title)+'</span></button>'; }
function head(title, action = '') { return '<header class="page-head"><h1>'+escape(title)+'</h1>'+action+'</header>'; }
function empty(text) { return '<p>'+escape(text)+'</p>'; }
function pageOf(items, key, size, paintItem) {
  const count = Math.max(1, Math.ceil(items.length / size));
  const page = Math.min(pageNumbers.get(key) ?? (key==='chat'? count-1 : 0), count-1);
  const markup = items.slice(page*size, (page+1)*size).map(paintItem).join('');
  function step(target,label,direction) {
    const caption=key==='chat'||key==='motion'||key.startsWith('reminder')?'<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+(direction<0?'m12 5-5 5 5 5':'m8 5 5 5-5 5')+'"/></svg>':label;
    return '<button type="button" aria-label="'+label+'" title="'+label+'" data-page-key="'+key+'" data-page="'+target+'"'+(target<0||target>=count?' disabled':'')+'>'+caption+'</button>';
  }
  const bar = count===1?'':'<nav class="pager" aria-label="'+(key==='chat'?'Страницы переписки':'Страницы')+'">'+step(page-1,'Назад',-1)+'<span>'+ (page+1)+' / '+count+'</span>'+step(page+1,'Далее',1)+'</nav>';
  return {markup,bar};
}
const shortPanel = () => innerHeight < 430;
const pageRows = (regular, compact) => innerHeight<315?2:shortPanel()?compact:regular;
function petList() {
  const pet=activePet();
  return '<div class="pet-controls"><div class="pet-dropdown field"><label for="pet-select">Активный питомец</label><button type="button" class="choice" id="pet-select" aria-controls="pet-options" aria-expanded="false"><span>'+escape(pet.name)+'</span></button><div class="pet-options" id="pet-options" role="group" aria-label="Питомцы" hidden></div></div>'+button('Добавить питомца','class="primary" id="import-pet"')+'</div>';
}
function bindPetList() {
  const control=$('#pet-select',main);
  if(!control)return;
  const options=$('#pet-options',main);
  function showPage() {
    const page=pageOf(pets,'pets',innerHeight<300?3:5,p=>'<button type="button" class="pet-option" data-pet="'+pets.indexOf(p)+'" aria-pressed="'+(p===activePet())+'"><span>'+escape(p.name)+'</span>'+(p===activePet()?'<span aria-hidden="true">✓</span>':'')+'</button>');
    options.innerHTML=page.markup+page.bar;
  }
  control.onclick=()=>{
    if(!options.hidden){closePetList();return;}
    const size=innerHeight<300?3:5;
    pageNumbers.set('pets',Math.floor(pets.indexOf(activePet())/size));
    showPage();
    options.hidden=false;
    control.setAttribute('aria-expanded','true');
    $('[aria-pressed="true"]',options)?.focus();
  };
  options.onclick=event=>{
    const pager=event.target.closest('[data-page]');
    if(!pager)return;
    event.stopPropagation();
    pageNumbers.set('pets',Number(pager.dataset.page));
    showPage();
    $('.pet-option',options).focus();
  };
  options.onkeydown=event=>{
    if(!['ArrowDown','ArrowUp','Home','End'].includes(event.key))return;
    const rows=[...options.querySelectorAll('.pet-option')];
    const index=rows.indexOf(document.activeElement);
    const next=event.key==='Home'?0:event.key==='End'?rows.length-1:Math.max(0,Math.min(rows.length-1,index+(event.key==='ArrowDown'?1:-1)));
    if(!rows.length)return;
    event.preventDefault();
    rows[next].focus();
  };
}
function closePetList(restoreFocus=false) {
  const options=$('#pet-options:not([hidden])',main);
  if(!options)return false;
  options.hidden=true;
  const control=$('#pet-select',main);
  control.setAttribute('aria-expanded','false');
  if(restoreFocus)control.focus();
  return true;
}
function home() {
  const p=activePet();
  const upcoming=state.reminders.filter(r=>!r.done).sort((a,b)=>a.due-b.due).slice(0,3);
  const pinned=state.notes.find(n=>n.pinned);
  return head(p.name,button(paused?'Продолжить':'Пауза','id="pause"','play'))+
    '<div class="home"><div class="stack"><section class="panel"><div class="between"><h2>Ближайшие</h2>'+button('Открыть','class="subtle" data-go="reminders"')+'</div>'+
    (upcoming.map(r=>'<div class="list-row">'+glyph('bell')+'<span>'+escape(r.title)+'<br><small class="muted">'+date(r.due)+'</small></span></div>').join('')||empty('Ближайших напоминаний нет.'))+'</section>'+
    '<section class="panel"><div class="between"><h2>Заметки</h2>'+button('Открыть','class="subtle" data-go="notes"')+'</div>'+(pinned?button(pinned.title,'class="list-row" data-note-open="'+pinned.id+'"','notebook-pen'):empty('Закрепите заметку, чтобы видеть её здесь.'))+'</section>'+
    '<section class="panel"><h2>Быстрый запуск</h2>'+(state.favorites.slice(0,4).map(item=>button(item.title,'class="list-row" data-open="'+item.id+'"',item.icon)).join('')||button('Добавить в избранное','class="list-row" data-go="favorites"','bookmark'))+'</section></div>'+
    '<section class="stage" aria-label="Питомец">'+button('Меню питомца','class="menu-trigger" id="pet-menu" aria-expanded="'+menu+'"','settings')+
    (hidden?button('Показать питомца','id="show-pet"'):'<canvas class="sprite'+(p.id==='hudi'?' hudi-idle':'')+'" id="active-sprite" data-paused="'+paused+'" aria-label="'+escape(p.name)+'" role="img"></canvas>')+
    '<div class="stage-shortcuts">'+[['notes','Заметки','notebook-pen'],['favorites','Избранное','globe'],['quick','Быстрый чат','message-circle']].map(([action,title,icon])=>'<button class="sphere" data-go="'+action+'" title="'+title+'" aria-label="'+title+'">'+glyph(icon)+'</button>').join('')+'</div>'+
    (menu?contextMenu():'')+'</section></div><div class="panel" style="margin-top:15px">'+composer()+'</div>'+(quick?'<section class="panel quick"><div class="between"><h2>Быстрый чат</h2>'+button('Закрыть','class="subtle" id="quick-close"')+'</div>'+chatFeed()+composer()+button('Открыть полный чат','class="subtle" data-go="chat"')+'</section>':'');
}
function contextMenu() {
  const rows=[['Быстрый чат','data-go="quick"','message-circle'],['Открыть чат','data-go="chat"','message-circle'],['Заметки','data-go="notes"','notebook-pen'],['Сайты и программы','data-go="favorites"','link'],['Напоминания','data-go="reminders"','bell'],['Движения','id="motion"','play'],['Питомцы','data-go="pets"','heart-pulse'],['Внешний вид','data-setting="theme"','palette'],['Модель и API','data-setting="model"','cpu'],['Управление','data-setting="pets"','settings'],['Закрыть питомца','id="hide-pet"','lock']];
  const hints={'Быстрый чат':'3 клика','Заметки':'Ctrl + 2 клика'};
  return '<div class="context" aria-label="Управление питомцем"><div class="context-title">'+escape(activePet().name)+'</div>'+rows.map(([title,attrs,icon],index)=>
    (index===5||index===10?'<hr>':'')+'<button '+attrs+'>'+glyph(icon)+'<span>'+escape(title)+'</span>'+(hints[title]?'<small>'+hints[title]+'</small>':'')+'</button>').join('')+'</div>';
}
function notesPage() {
  const q = window.noteQuery ?? '';
  const notes=state.notes.filter(n=>(n.title+' '+n.body).toLowerCase().includes(q.toLowerCase())).sort((a,b)=>Number(b.pinned)-Number(a.pinned));
  const note=state.notes.find(n=>n.id===currentNote);
  if (noteEditing && note) return head('Заметка',button('К списку','class="subtle" id="notes-back"'))+
    '<div class="form"><label>Название<input id="note-title" maxlength="100" value="'+escape(note.title)+'"></label><label>Текст<textarea id="note-body" rows="7" maxlength="50000">'+escape(note.body)+'</textarea></label><div class="actions">'+button(note.pinned?'Открепить':'Закрепить','id="pin-note"','bookmark')+button('Удалить','id="remove-note"')+'</div></div>';
  const page=pageOf(notes,'notes',pageRows(7,4),n=>button(n.title||'Без названия','class="list-row truncate" data-note-open="'+n.id+'"',n.pinned?'bookmark':'file-text'));
  return head('Заметки',button('Новая заметка','class="primary" id="add-note"'))+'<div class="stack"><input id="note-search" type="search" aria-label="Поиск заметок" placeholder="Поиск заметок" value="'+escape(q)+'"><div class="list">'+(page.markup||empty('Заметки не найдены.'))+'</div>'+page.bar+'</div>';
}
function favoritesPage() {
  const items=state.favorites.filter(item=>item.kind===kind);
  const page=pageOf(items,kind,pageRows(4,3),item=>'<div class="list-row favorite-row"><span class="sphere" aria-hidden="true">'+favoriteGlyph(item.siteIcon,item.icon,item.monochrome)+'</span><span class="clip-text" title="'+escape(item.target)+'">'+escape(item.title)+'<small class="muted">'+escape(item.target)+'</small></span><div class="favorite-actions">'+iconAction(item.kind==='site'?'Открыть сайт':'Запустить программу','open','data-open="'+escape(item.id)+'"')+iconAction('Изменить ярлык','edit','data-edit-favorite="'+escape(item.id)+'"')+'</div></div>');
  return '<header class="page-head"><h1>Избранное</h1>'+iconAction('Добавить ярлык','add','id="add-favorite" class="primary"')+'</header><div class="tabs" role="tablist" aria-label="Тип избранного">'+button('Сайты','role="tab" aria-selected="'+(kind==='site')+'" data-kind="site"','globe')+button('Приложения','role="tab" aria-selected="'+(kind==='app')+'" data-kind="app"','cpu')+'</div><section class="panel list">'+(page.markup||empty(kind==='site'?'Сохраните сайт для быстрого доступа.':'Добавьте программу Windows для быстрого запуска.'))+'</section>'+page.bar;
}
const actionIcons={
  open:'M12 4h6v6M18 4l-9 9M10 5H5a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1v-5',
  edit:'m12 5 3 3M4 16l4-.7L17 6a1 1 0 0 0 0-1.4L15.4 3a1 1 0 0 0-1.4 0L5 12z',
  add:'M10 4v12M4 10h12',save:'m4 10 4 4 8-8',cancel:'m5 5 10 10M15 5 5 15',
  remove:'M3 5h14M7 5V3h6v2M5 5l1 12h8l1-12M8 8v6m4-6v6',
  folder:'M3 6h5l2 2h7v8H3z',
  snooze:'M10 6v5h4 M18 10a8 8 0 1 1-16 0 8 8 0 0 1 16 0z',
  back:'M16 10H4 M9 5l-5 5 5 5',
  shuffle:'M3 5h2c4 0 5 10 10 10h2 M14 12l3 3-3 3 M3 15h2c1 0 2-1 3-2 M11 7c1-1 2-2 4-2h2 M14 2l3 3-3 3',
  pause:'M7 4v12 M13 4v12',
  play:'M6 4l10 6-10 6z',
};
function iconAction(label, icon, attrs, type='button') {
  return '<span class="favorite-action"><button type="'+type+'" aria-label="'+escape(label)+'" '+attrs+'>'+
    (actionIcons[icon]?'<svg viewBox="0 0 20 20" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="'+actionIcons[icon]+'"/></svg>':glyph(icon))+
    '</button><span class="action-tip" aria-hidden="true">'+escape(label)+'</span></span>';
}
function favoriteGlyph(siteIcon,icon,monochrome=false) {
  if(!/^[a-f0-9]{64}\.png$/i.test(siteIcon??''))return glyph(icon);
  const source='https://icons.sputnik.local/'+siteIcon;
  return monochrome?'<span class="site-icon site-icon-mask" aria-hidden="true" style="--icon-mask:url('+source.slice(0,-4)+'-mono.png)"></span>':'<img class="site-icon" src="'+source+'" alt="">';
}
const monoOption = '<label class="icon-mono-option"><input name="monochrome" type="checkbox">Монохромный</label>';
function siteIconStyle(input, paint, initial, useAutomatic, control, initialMonochrome) {
  let key=initial??'', revision=0;
  function render() {
    const attempt=++revision;
    const automatic=useAutomatic();
    control.disabled=!automatic;
    control.closest('label').hidden=!automatic;
    paint(key,false);
    if(!key||!automatic||!control.checked)return Promise.resolve(false);
    return call('siteIconMask',key).catch(()=>null).then(mask=>{
      const applied=typeof mask==='string'&&attempt===revision;
      if(applied)paint(key,true);
      return applied;
    });
  }
  if(control){
    control.checked=!!initialMonochrome;
    control.onchange=render;
  }
  const preview=siteIconPreview(input,value=>{key=value;if(control)render();else paint(key,false);},initial,useAutomatic);
  if(control)render();
  return {
    update:preview.update,
    async value() {
      const siteIcon=await preview.value();
      const monochrome=!!(control?.checked&&siteIcon&&useAutomatic()&&await render());
      return {siteIcon,monochrome};
    }
  };
}
function siteIconPreview(input, paint, initial, useAutomatic) {
  let key=initial??'', revision=0, timer;
  let pending=Promise.resolve(key);
  function update() {
    clearTimeout(timer);
    timer=null;
    const attempt=++revision;
    const url=input.value.trim();
    key='';paint(key);
    if(!useAutomatic()||!/^https?:\/\//i.test(url))return pending=Promise.resolve('');
    pending=call('siteIcon',url).catch(()=>null).then(result=>{
      if(attempt===revision&&useAutomatic()&&input.value.trim()===url){key=result??'';paint(key);}
      return key;
    });
    return pending;
  }
  input.addEventListener('input',()=>{
    clearTimeout(timer);revision++;key='';
    paint(key);
    if(useAutomatic())timer=setTimeout(update,250);
  });
  return {
    async value(){if(!useAutomatic())return '';if(timer){clearTimeout(timer);timer=null;return update();}return pending;},
    update,
  };
}
function editFavoriteRow(row,item) {
  if(!row||!item)return;
  if($('.favorite-inline',main)){
    render();
    row=$('[data-edit-favorite="'+CSS.escape(item.id)+'"]',main)?.closest('.favorite-row');
    if(!row)return;
  }
  const application=item.kind==='app';
  let icon=item.icon;
  row.innerHTML='<form class="favorite-inline"><div class="favorite-action favorite-icon-slot">'+iconAction('Выбрать значок',icon|| (application?'cpu':'globe'),'id="edit-favorite-icon" class="sphere"')+'</div><div class="favorite-inputs"><input name="title" aria-label="Название" maxlength="80" required value="'+escape(item.title)+'"><div class="favorite-address"><input name="target" aria-label="'+(application?'Файл приложения':'Адрес сайта')+'" '+(application?'readonly':'type="url" required')+' value="'+escape(item.target)+'">'+(application?iconAction('Выбрать приложение','folder','data-inline-app'):'')+'</div></div><div class="favorite-actions">'+iconAction('Сохранить','save','class="primary"','submit')+iconAction('Отменить','cancel','data-inline-cancel')+iconAction('Удалить ярлык','remove','data-inline-delete')+'</div></form>';
  const form=$('form',row);
  const iconButton=$('#edit-favorite-icon',form);
  let automatic=item.kind==='site'&&(item.iconSource==='site'||(!item.iconSource&&(!item.icon||item.icon==='globe')));
  if(!application)$('.favorite-inputs',form).insertAdjacentHTML('beforeend',monoOption);
  const preview=siteIconStyle(form.elements.target,(key,mono)=>iconButton.innerHTML=favoriteGlyph(key,icon||(application?'cpu':'globe'),mono),item.siteIcon,()=>automatic,form.elements.monochrome,item.monochrome);
  if(automatic&&!item.siteIcon)preview.update();
  function close(){render();$('[data-edit-favorite="'+CSS.escape(item.id)+'"]',main)?.focus();}
  $('[data-inline-cancel]',form).onclick=close;
  $('[data-inline-delete]',form).onclick=()=>{state.favorites=state.favorites.filter(f=>f!==item);save();close();};
  $('[data-inline-app]',form)?.addEventListener('click',async()=>{
    try {const path=await call('pickApp');if(path)form.elements.target.value=path;}
    catch(error){feedback(error.message);}
  });
  iconButton.onclick=()=>choicePage(iconButton,area=>{
    const page=$('.choice-page',main);
    $('h2',page).textContent='Значки';
    const back=()=>$('[data-choice-back]',page).click();
    fillIconPicker(area,automatic?'':icon,key=>{automatic=!key;icon=key||'globe';preview.update();back();},!application);
    $('#icon-search',area)?.focus();
  });
  form.onkeydown=event=>{if(event.key==='Escape'){event.stopPropagation();event.preventDefault();close();}};
  form.onsubmit=async event=>{
    event.preventDefault();
    const title=form.elements.title.value.trim(), target=form.elements.target.value.trim();
    if(!title||!target){feedback('Заполните название и адрес.');return;}
    if(!application&&!/^https?:\/\//i.test(target)){feedback('Введите адрес HTTP или HTTPS.');return;}
    const submit=$('button[type=submit]',form);
    submit.disabled=true;
    const iconStyle=await preview.value();
    if(!form.isConnected)return;
    submit.disabled=false;
    if(form.elements.target.value.trim()!==target||form.elements.title.value.trim()!==title)return;
    Object.assign(item,{title,target,icon,...iconStyle,iconSource:automatic?'site':'library'});save();close();
  };
  form.elements.title.focus();
}
const minute = 60000;
const repeatOptions = [
  ['none','Без повтора'], ['5','Каждые 5 минут'], ['10','Каждые 10 минут'],
  ['15','Каждые 15 минут'], ['20','Каждые 20 минут'], ['30','Каждые 30 минут'],
  ['60','Каждый час'], ['120','Каждые 2 часа'],
  ['180','Каждые 3 часа'], ['daily','Каждый день'], ['weekly','Каждую неделю'],
  ['custom','Свой интервал'],
];
function repeatLabel(reminder) {
  if(reminder.repeat==='custom')return 'Интервал: '+reminder.repeatMinutes+' мин';
  return repeatOptions.find(([key])=>key===reminder.repeat)?.[1]??'Без повтора';
}
function remindersPage() {
  const items=[...state.reminders].sort((a,b)=>Number(a.done)-Number(b.done)||a.due-b.due);
  const page=pageOf(items,'reminders',pageRows(6,4),r=>'<div class="list-row">'+glyph('bell')+'<span class="clip-text" title="'+escape(r.title)+'">'+escape(r.title)+'<small class="muted">'+date(r.due)+(r.done?' · Выполнено':r.repeat&&r.repeat!=='none'?' · '+escape(repeatLabel(r)):'')+'</small></span><div class="favorite-actions">'+iconAction('Изменить напоминание','edit','data-edit-reminder="'+escape(r.id)+'"')+(r.done?'':iconAction('Готово','save','data-done="'+escape(r.id)+'"'))+'</div></div>');
  return head('Напоминания',iconAction('Добавить напоминание','add','class="primary" id="add-reminder"'))+'<section class="panel list">'+(page.markup||empty('Добавьте напоминание, чтобы HUDI показал его в нужное время.'))+'</section>'+page.bar;
}
function composer() {
  return '<form class="compose"><input aria-label="Сообщение модели" maxlength="10000" placeholder="Спросить '+escape(activePet().name)+'…" value="'+escape(draft)+'" required><button type="submit" class="primary compose-send" aria-label="Отправить" title="Отправить (Enter)"'+(busy?' disabled':'')+'><svg viewBox="0 0 18 16" aria-hidden="true" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M2 8h13M9 2l6 6-6 6"/></svg></button></form>';
}
function chatStatus() {
  if(chatError)return '<p class="chat-status chat-error" role="alert">'+escape(chatError)+'</p>';
  return busy?'<p class="chat-status" role="status">Модель отвечает…</p>':'';
}
function chatFeed() {
  if (desktopSection) {
    const lineWidth=Math.max(20,Math.floor((innerWidth-88)/13));
    const linesPerPage=innerHeight<315?5:innerHeight<390?7:11;
    const chunks=state.messages.slice(-40).flatMap(m=>{
      const lines=m.content.split(/\r?\n/).flatMap(line=>{
        const units=Array.from(line);
        if(!units.length)return [''];
        const segments=[];
        for(let start=0;start<units.length;){
          const edge=Math.min(start+lineWidth,units.length);
          const space=edge<units.length?units.lastIndexOf(' ',edge):-1;
          const end=space>start+lineWidth/2?space+1:edge;
          segments.push(units.slice(start,end).join(''));
          start=end;
        }
        return segments;
      });
      const parts=[];
      for(let i=0;i<lines.length;i+=linesPerPage) parts.push({role:m.role,content:lines.slice(i,i+linesPerPage).join('\n')});
      return parts;
    });
    const page=pageOf(chunks,'chat',1,m=>'<div class="bubble '+(m.role==='user'?'user':'')+'">'+escape(m.content)+'</div>');
    return {...page,markup:'<div class="feed" aria-live="polite">'+(page.markup||'<p class="chat-empty">С чего начнём?</p>')+'</div>'};
  }
  const history=state.messages.slice(-40);
  return '<div class="feed" aria-live="polite">'+(history.map(m=>'<div class="bubble '+(m.role==='user'?'user':'')+'">'+escape(m.content)+'</div>').join('')||empty('С чего начнём?'))+(busy?empty('Модель отвечает…'):'')+(chatError?'<p role="alert">'+escape(chatError)+'</p>':'')+'</div>';
}
function desktopChat() {
  const history=chatFeed();
  return '<section class="chat-window"><header class="chat-header"><h1>'+escape('Чат с '+activePet().name)+'</h1>'+history.bar+'</header>'+history.markup+'<footer class="chat-footer">'+chatStatus()+composer()+'</footer></section>';
}
function desktopQuick() {
  const history=chatFeed();
  return head('Быстрый чат',button('Полный чат','class="subtle" data-section="chat"'))+history.markup+history.bar+chatStatus()+composer();
}
function settings(contentOnly = false) {
  const links=[['pets','Питомцы'],['theme','Оформление'],['model','Администрирование']];
  let content;
  if(tab==='theme') content=appearancePage();
  else if(tab==='model') content='<section class="panel"><h2>Подключение LLM</h2><form id="model-form" class="form"><label>Базовый URL<input name="url" type="url" placeholder="http://localhost:11434/v1" value="'+escape(model.url)+'" required></label><label>ID модели<input name="model" value="'+escape(model.model)+'" required></label><label>API-ключ<input name="key" type="password" autocomplete="off" placeholder="'+(model.hasKey?'Ключ сохранён':'Введите ключ, если нужен')+'"></label><div class="toggles"><label><input name="clearKey" type="checkbox">Удалить сохранённый ключ</label></div><div class="actions"><button type="button" id="test-model">Проверить подключение</button><button class="primary">Сохранить</button></div><p id="model-status" role="status"></p></form></section>';
  else content=petList();
  return contentOnly?content:head('Настройки')+'<div class="tabs" role="tablist" aria-label="Настройки">'+links.map(([key,label])=>button(label,'role="tab" aria-selected="'+(tab===key)+'" data-setting="'+key+'"')).join('')+'</div>'+content;
}
const appearanceModes=[['pet','Палитра питомца'],['custom','Своя палитра'],['default','Графит']];
const colorFields=[['background','Фон'],['surface','Поверхности'],['accent','Акцент']];
const desktopSizes=[['petScale','Размер питомца',60,160],['sphereScale','Размер сфер',60,160],['sphereSpread','Расстояние между сферами',0,100]];
function appearancePage() {
  const mode=state.appearanceMode==='custom'?'custom':state.themed?'pet':'default';
  const colors=state.customTheme??activeTheme();
  const font=state.font??'Segoe UI';
  return '<form class="form" id="appearance-form"><div class="field">Цветовая схема'+makeChoice('appearance-mode','Цветовая схема',appearanceModes,mode)+'</div>'+
    '<div class="appearance-colors"'+(mode!=='custom'?' hidden':'')+'>'+colorFields.map(([key,label])=>
      '<div class="field" data-picker-field><span>'+label+'</span><button type="button" class="color-trigger" id="appearance-'+key+'" aria-label="'+label+'" aria-expanded="false" aria-controls="appearance-'+key+'-popup"><i aria-hidden="true"></i><span>'+escape(colors[key])+'</span></button><div class="picker-popover appearance-popover color-popover" id="appearance-'+key+'-popup" hidden></div></div>').join('')+'</div>'+
    '<div class="field" data-picker-field><span>Шрифт</span><button type="button" class="choice" id="appearance-font" aria-label="Шрифт" aria-controls="appearance-font-popup" aria-expanded="false"><span>'+escape(font)+'</span></button><div class="picker-popover appearance-popover font-popover" id="appearance-font-popup" hidden></div></div>'+
    '<div class="desktop-scales">'+desktopSizes.map(([key,label,min,max])=>{
      const percent=state[key]??100;
      return '<label class="desktop-scale" for="'+key+'"><span>'+label+'</span><output for="'+key+'">'+percent+'%</output><input type="range" id="'+key+'" min="'+min+'" max="'+max+'" step="5" value="'+percent+'" aria-label="'+label+'" aria-valuetext="'+percent+'%"></label>';
    }).join('')+'</div>'+
    '<div class="actions"><button type="button" id="appearance-reset" title="Вернуть стандартное оформление и размеры">Сбросить</button><p id="appearance-status" role="status"></p></div></form>';
}
function bindAppearance() {
  const form=$('#appearance-form',main);
  if(!form)return;
  let mode=state.appearanceMode==='custom'?'custom':state.themed?'pet':'default';
  let font=state.font??'Segoe UI';
  const colorBox=$('.appearance-colors',form);
  const colors={...(state.customTheme??activeTheme())};
  const status=$('#appearance-status',form);
  const commit=()=>{
    Object.assign(state,{appearanceMode:mode,themed:mode==='pet',font});
    if(mode==='custom')state.customTheme={...colors};
    theme();
    queueAppearanceSave(status);
  };
  bindChoice($('#appearance-mode',form),appearanceModes,mode,key=>{mode=key;colorBox.hidden=key!=='custom';commit();});
  for(const [key] of colorFields)
    bindColorPicker($('#appearance-'+key,form),colors[key],value=>{colors[key]=value;commit();});
  bindFontDropdown($('#appearance-font',form),appearanceFonts,font,value=>{font=value;commit();});
  for(const [key] of desktopSizes) {
    const slider=$('#'+key,form);
    slider.addEventListener('input',()=>{
      state[key]=slider.valueAsNumber;
      slider.previousElementSibling.value=slider.value+'%';
      slider.setAttribute('aria-valuetext',slider.value+'%');
      queueAppearanceSave(status);
    });
  }
  form.onsubmit=event=>event.preventDefault();
  $('#appearance-reset',form).onclick=()=>{
    Object.assign(state,{appearanceMode:'pet',themed:true,font:'Segoe UI',customTheme:null,petScale:100,sphereScale:100,sphereSpread:100});
    render();
    queueAppearanceSave($('#appearance-status',main));
  };
}
let appearanceSaveTimer, appearanceRevision=0, appearanceStatus;
function queueAppearanceSave(status) {
  ++appearanceRevision;
  appearanceStatus=status;
  if(appearanceSaveTimer)return;
  appearanceSaveTimer=setTimeout(async ()=>{
    appearanceSaveTimer=null;
    const revision=appearanceRevision;
    try {
      await call('save',structuredClone(state));
      if(revision===appearanceRevision&&appearanceStatus?.isConnected)appearanceStatus.textContent='Оформление сохранено';
    } catch(error) {
      if(revision===appearanceRevision)feedback(error.message);
    }
  },120);
}
const imageCache = new Map();
function imageOf(url) {
  if (!imageCache.has(url)) imageCache.set(url,new Promise((resolve,reject)=>{
    const image=new Image(); image.onload=()=>resolve(image); image.onerror=()=>reject(new Error('Не удалось прочитать кадры питомца.')); image.src=url;
  }).catch(error=>{imageCache.delete(url);throw error;}));
  return imageCache.get(url);
}
function paint(canvas, image, clip, frame = 0) {
  const w=clip.frameWidth??image.width, h=clip.frameHeight??image.height, cols=clip.columns??1;
  const targetWidth=Math.max(1,Math.round(canvas.clientWidth*devicePixelRatio));
  const targetHeight=Math.max(1,Math.round(canvas.clientHeight*devicePixelRatio));
  if(canvas.width!==targetWidth) canvas.width=targetWidth;
  if(canvas.height!==targetHeight) canvas.height=targetHeight;
  const ctx=canvas.getContext('2d');
  const scale=Math.min(canvas.width/w,canvas.height/h);
  ctx.clearRect(0,0,canvas.width,canvas.height);
  ctx.drawImage(image,frame%cols*w,Math.floor(frame/cols)*h,w,h,(canvas.width-w*scale)/2,(canvas.height-h*scale)/2,w*scale,h*scale);
}
function draw(canvas, clip, animate = false, allowMotion = () => !paused) {
  if(!canvas || !clip) return;
  imageOf(clip.file).then(image=>{
    let lastTime, elapsed = 0, previous = -1;
    function frame(time) {
      if(!canvas.isConnected) return;
      if(animate && lastTime!==undefined && allowMotion()) elapsed+=Math.min(time-lastTime,150);
      lastTime=time;
      const index=Math.floor(elapsed*clip.fps/1000)%clip.frameCount;
      if(index!==previous || Math.round(canvas.clientWidth*devicePixelRatio)!==canvas.width || Math.round(canvas.clientHeight*devicePixelRatio)!==canvas.height) {
        paint(canvas,image,clip,index); previous=index;
      }
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  }).catch(error=>feedback(error.message));
}
function render() {
  theme();
  if (desktopSection) {
    if (!subpage) renderSection();
    return;
  }
  $('#nav').innerHTML=routes.slice(0,-1).map(([key,title,icon])=>button(title,'class="nav-button '+(route===key?'active':'')+'" data-go="'+key+'" aria-label="'+title+'"'+(route===key?' aria-current="page"':''),icon)).join('');
  const settingsLink=$('.settings-link');
  settingsLink.innerHTML=glyph('settings')+'<span>Настройки</span>';
  settingsLink.setAttribute('aria-label','Настройки');
  settingsLink.classList.toggle('active',route==='settings');
  const pages={home,pets:()=>head('Библиотека питомцев')+petList(),notes:notesPage,favorites:favoritesPage,reminders:remindersPage,settings,chat:()=>head('Чат с '+activePet().name)+ '<section class="panel">'+chatFeed()+composer()+'</section>'};
  main.innerHTML=pages[route]();
  bindPetList();
  bindAppearance();
  draw($('#active-sprite'),activePet().animations[motion],true);
  for(const feed of main.querySelectorAll('.feed')) feed.scrollTop=feed.scrollHeight;
  showNotice();
}

function renderSection() {
  const movements=pageOf(desktopMotions.map((name,index)=>({name,index})),'motion',innerHeight<270?3:5,({name,index})=>button(name,'class="list-row truncate" data-playback="clip" data-index="'+index+'"','play'));
  const pages={
    notes:notesPage,favorites:favoritesPage,reminders:remindersPage,
    pets:()=>head('Питомцы')+petList(),
    chat:desktopChat,
    quick:desktopQuick,
    motion:()=>head('Движения','<div class="motion-toolbar" role="group" aria-label="Управление воспроизведением">'+iconAction('Случайный порядок','shuffle','class="primary" data-playback="shuffle"')+iconAction('Пауза','pause','data-playback="pause"')+iconAction('Продолжить','play','data-playback="resume"')+'</div>')+'<div class="list motion-list">'+movements.markup+'</div>'+movements.bar,
    manage:()=>head('Управление')+'<section class="panel toggles"><label><input type="checkbox" id="autostart" '+(autoStart?'checked':'')+'>Запускать питомца при входе в Windows</label></section>'+button('Горячие клавиши','id="manage-hotkeys"','settings'),
    theme:()=>head('Внешний вид')+settings(true),
    model:()=>head('Модель и API')+settings(true),
    sharing:sharingPage,
  };
  main.innerHTML=(pages[desktopSection]??pages.quick)();
  bindPetList();
  bindAppearance();
  showNotice();
}

async function openSection(section) {
  const revision=++sectionRevision;
  document.body.classList.add('compact');
  if(dialog.open) dialog.close();
  subpage=false;
  noteEditing=false;
  desktopSection=section;
  route=section;
  if(section==='model'||section==='theme') tab=section;
  try {
    if(section==='motion') desktopMotions=await call('desktopMotions');
    if(section==='manage') autoStart=await call('autoStart');
    if(section==='theme') appearanceFonts=(await call('appearanceFonts'))??appearanceFonts;
    if(section==='sharing') shareChannels=await call('shareChannels');
    if(revision!==sectionRevision) return;
    render();
    main.scrollTop=0;
    if(section==='quick') main.querySelector('.compose input')?.focus();
  } catch(error){feedback(error.message);}
}
function sharingPage() {
  const page=pageOf(shareChannels,'sharing',pageRows(6,4),(channel,index)=>button(channel.name,
    'class="list-row truncate" data-share-index="'+shareChannels.indexOf(channel)+'"','link'));
  return head('Каналы отправки',button('Добавить','class="primary" id="add-share-channel"'))+
    '<div class="list">'+(page.markup||empty('Добавьте канал для отправки заметок.'))+'</div>'+page.bar;
}
function shortcutName(shortcut) {
  if(!shortcut?.key)return '';
  const labels=[[2,'Ctrl'],[1,'Alt'],[4,'Shift'],[8,'Win']];
  const key=shortcut.key>=112?'F'+(shortcut.key-111):String.fromCharCode(shortcut.key);
  return [...labels.filter(([flag])=>shortcut.modifiers&flag).map(([,label])=>label),key].join(' + ');
}
async function openHotkeys() {
  hotkeys=await call('hotkeys');
  const view=openDialog('Горячие клавиши','<div class="list" id="hotkey-list"></div><p id="hotkey-status" role="status"></p>');
  const list=$('#hotkey-list',view), status=$('#hotkey-status',view);
  pageNumbers.set('hotkeys',0);
  function drawHotkeys() {
    const page=pageOf(hotkeys.sections,'hotkeys',pageRows(7,5),item=>'<div class="hotkey-row between"><span>'+escape(item.title)+'</span>'+button(shortcutName(hotkeys.bindings[item.action])||'Назначить','type="button" data-bind-key="'+item.action+'"')+'</div>');
    list.innerHTML=page.markup+page.bar;
  }
  list.onclick=event=>{
    const pager=event.target.closest('[data-page-key]');
    if(pager){event.stopPropagation();pageNumbers.set('hotkeys',Number(pager.dataset.page));drawHotkeys();return;}
    const target=event.target.closest('[data-bind-key]');
    if(!target)return;
    if($('.hotkey-capture',list)){
      drawHotkeys();
      $('[data-bind-key="'+target.dataset.bindKey+'"]',list)?.click();
      return;
    }
    const section=target.dataset.bindKey;
    const row=target.parentElement;
    target.hidden=true;
    const capture=document.createElement('input');
    capture.className='hotkey-capture';capture.placeholder='Нажмите сочетание';
    capture.setAttribute('aria-label','Новое сочетание для '+row.firstElementChild.textContent);
    capture.readOnly=true;
    const clear=document.createElement('button');
    clear.type='button';clear.textContent='Снять';
    row.append(capture,clear);
    capture.focus();
    async function assign(modifiers,key) {
      clear.disabled=true;capture.disabled=true;
      try {
        await call('setHotkey',{section,modifiers,key});
        if(key)hotkeys.bindings[section]={modifiers,key};
        else delete hotkeys.bindings[section];
        status.textContent='Сочетание сохранено';
        drawHotkeys();
        $('[data-bind-key="'+section+'"]',list)?.focus();
      } catch(error){status.textContent=error.message;clear.disabled=false;capture.disabled=false;capture.focus();}
    }
    clear.onclick=()=>assign(0,0);
    capture.onkeydown=e=>{
      if(e.key==='Tab')return;
      e.preventDefault();
      e.stopPropagation();
      if(e.key==='Escape'){drawHotkeys();$('[data-bind-key="'+section+'"]',list)?.focus();return;}
      const code=e.code;
      const key=/^Key[A-Z]$/.test(code)?code.charCodeAt(3):/^Digit\d$/.test(code)?code.charCodeAt(5):/^F\d{1,2}$/.test(code)?111+Number(code.slice(1)):0;
      if(!key)return;
      const modifiers=(e.altKey?1:0)+(e.ctrlKey?2:0)+(e.shiftKey?4:0)+(e.metaKey?8:0);
      if(!modifiers){status.textContent='Добавьте Ctrl, Alt, Shift или Win.';return;}
      assign(modifiers,key);
    };
  };
  drawHotkeys();
}
function editShareChannel(index) {
  const existing=shareChannels[index];
  const view=openDialog(existing?'Изменить канал':'Добавить канал',
    '<form id="share-channel-form" class="form"><label>Название<input name="name" required maxlength="40" value="'+escape(existing?.name)+'"></label><label>Ссылка для отправки<input name="template" required maxlength="1800" value="'+escape(existing?.template)+'"></label><p>Укажите ссылку HTTPS или mailto с параметром <code>{text}</code>. Для темы письма используйте <code>{title}</code>.</p><p id="share-channel-error" role="alert"></p><div class="actions"><button class="primary">Сохранить</button>'+(existing?button('Удалить','type="button" id="remove-share-channel"'):'')+'</div></form>');
  const form=$('#share-channel-form',view);
  async function update(next) {
    const controls=[...form.querySelectorAll('button')];
    for(const control of controls)control.disabled=true;
    try {await call('saveShareChannels',next);shareChannels=next;closeView();}
    catch(error){$('#share-channel-error',form).textContent=error.message;for(const control of controls)control.disabled=false;}
  }
  form.onsubmit=event=>{
    event.preventDefault();
    const next=[...shareChannels];
    next[index]={name:form.elements.name.value.trim(),template:form.elements.template.value.trim()};
    update(next);
  };
  $('#remove-share-channel',form)?.addEventListener('click',()=>update(shareChannels.filter((_,i)=>i!==index)));
}
function openDialog(title, content, iconBack=false) {
  if (desktopSection) {
    subpage=true;
    main.innerHTML=head(title,iconBack?iconAction('Назад','back','data-close'):button('Назад','class="subtle" data-close'))+content;
    return main;
  }
  if(dialog.open) dialog.close();
  dialog.innerHTML='<div class="page-head"><h2 id="dialog-title">'+escape(title)+'</h2>'+(iconBack?iconAction('Закрыть','cancel','data-close'):button('Закрыть','class="subtle" data-close'))+'</div>'+content;
  dialog.showModal();
  return dialog;
}
function closeView() {
  if(dialog.open) dialog.close();
  subpage=false;
  render();
}
function makeChoice(id, label, options, value) {
  return '<button type="button" class="choice" id="'+id+'" aria-label="'+escape(label)+'" data-label="'+escape(label)+'" aria-expanded="false"><span>'+escape(options.find(([key])=>key===value)?.[1])+'</span></button>';
}
function bindChoice(control, options, initial, change) {
  let value=initial;
  control.onclick=()=>choicePage(control,area=>{
    const size=innerHeight<290?4:innerHeight<380?8:12;
    const pageKey=control.id+'-choices';
    pageNumbers.set(pageKey,Math.floor(Math.max(0,options.findIndex(([key])=>key===value))/size));
    function renderOptions(){
      const page=pageOf(options.map(([key,label],index)=>({key,label,index})),pageKey,size,({key,label,index})=>'<button type="button" data-option="'+index+'" aria-pressed="'+(key===value)+'"><span>'+escape(label)+'</span>'+(key===value?'<span aria-hidden="true">✓</span>':'')+'</button>');
      area.innerHTML='<div class="choices">'+page.markup+'</div>'+page.bar;
    }
    renderOptions();
    area.onclick=event=>{
      const pager=event.target.closest('[data-page]');
      if(pager){event.stopPropagation();pageNumbers.set(pageKey,Number(pager.dataset.page));renderOptions();area.querySelector('[data-option]')?.focus();return;}
      const choice=event.target.closest('[data-option]');
      if(!choice)return;
      const [key,label]=options[Number(choice.dataset.option)];
      value=key;
      control.firstElementChild.textContent=label;
      area.parentElement.querySelector('[data-choice-back]').click();
      change(key);
    };
  });
}
function choicePage(control, content) {
  const host=control.closest('main, dialog');
  const nodes=[...host.children].filter(element=>!element.hidden);
  for(const element of nodes)element.hidden=true;
  control.setAttribute('aria-expanded','true');
  const page=document.createElement('section');
  page.className='choice-page';
  page.innerHTML='<header class="page-head"><h2>'+escape(control.dataset.label)+'</h2>'+(control.id==='reminder-repeat'?iconAction('Назад','back','data-choice-back'):'<button type="button" class="subtle" data-choice-back>Назад</button>')+'</header><div></div>';
  const area=page.lastElementChild;
  const back=$('[data-choice-back]',page);
  const close=()=>{page.remove();for(const element of nodes)element.hidden=false;control.setAttribute('aria-expanded','false');control.focus();};
  back.onclick=close;
  host.append(page);
  content(area,close);
  if(!area.contains(document.activeElement)) (area.querySelector('[aria-pressed="true"]')??back).focus();
  return page;
}
function selectPet(p, useTheme=true) {
  state.active=p.id;
  if(state.appearanceMode!=='custom'){state.appearanceMode=useTheme?'pet':'default';state.themed=useTheme;}
  motion=0;hidden=false;save();render();
}
async function importPet() {
  const p=await call('selectPet');
  if(!p) return;
  const motions=p.animations.map((clip,index)=>[index,clip.name]);
  const themes=[['pet',p.theme.name],['default','Графит']];
  const dialog=openDialog('Импорт питомца','<div class="split import-layout"><section class="panel import-preview"><canvas class="sprite" aria-label="'+escape(p.name)+'"></canvas></section><section class="stack"><h2 title="'+escape(p.description)+'">'+escape(p.name)+'</h2><div class="field">Анимация'+makeChoice('preview-motion','Анимация',motions,0)+'</div><div class="frames"></div><div class="field">Оформление'+makeChoice('preview-theme','Оформление',themes,'pet')+'</div><button class="primary" id="install">Загрузить</button></section></div>');
  const scene=$('.import-preview',dialog);
  const frames=$('.frames',dialog);
  let motionIndex=0, useTheme=true;
  let previewRevision=0;
  function preview() {
    const revision=++previewRevision;
    const clip=p.animations[motionIndex];
    const canvas=scene.querySelector('canvas').cloneNode();
    scene.replaceChildren(canvas); draw(canvas,clip,true);
    const samples=Math.min(clip.frameCount,4);
    frames.innerHTML='<canvas width="65" height="65" style="width:65px;height:65px"></canvas>'.repeat(samples);
    imageOf(clip.file).then(image=>{
      if (revision!==previewRevision || !frames.isConnected) return;
      Array.from(frames.children).forEach((c,i)=>paint(c,image,clip,Math.floor(i*clip.frameCount/samples)));
    }).catch(error=>feedback(error.message));
  }
  bindChoice($('#preview-motion',dialog),motions,0,key=>{motionIndex=key;preview();});
  bindChoice($('#preview-theme',dialog),themes,'pet',key=>{useTheme=key==='pet';tint();});
  function tint() {const colors=useTheme?p.theme:defaultTheme;scene.style.background=colors.background;scene.style.borderColor=colors.accent;}
  tint(); preview();
  $('#install',dialog).onclick=async event=>{
    const control=event.currentTarget;control.disabled=true;
    try {const installed=await call('installPet',p.token);pets=[...pets,installed];closeView();selectPet(installed,useTheme);go('pets');}
    catch(error){feedback(error.message);control.disabled=false;}
  };
}
function fillIconPicker(picker, icon, select, allowAutomatic=false) {
  const categories=[['','Все категории'],...groups.map(g=>[g.name,g.name])];
  picker.innerHTML='<div class="stack"><div class="icon-filters"><input type="text" placeholder="Поиск значков" aria-label="Поиск значков" id="icon-search">'+makeChoice('icon-group','Категория',categories,'')+'</div><div class="between"><small class="muted" id="icon-count" aria-live="polite"></small>'+(allowAutomatic?button('Значок сайта','type="button" id="auto-icon"'+(!icon?' aria-pressed="true"':''),'globe'):'')+'</div><div class="chooser icon-library"></div></div>';
  const search=$('#icon-search',picker), grid=$('.chooser',picker);
  let category='';
  function update() {
    const query=search.value.trim().toLowerCase();
    const found=groups.filter(g=>!category||g.name===category).flatMap(g=>g.icons).filter(i=>(i.label+' '+i.id).toLowerCase().includes(query));
    $('#icon-count',picker).textContent=found.length+' из '+glyphs.size;
    grid.innerHTML=found.map(i=>'<button type="button" data-glyph="'+i.id+'" aria-pressed="'+(i.id===icon)+'" title="'+escape(i.label)+'"><span class="sphere" aria-hidden="true">'+i.svg+'</span><span>'+escape(i.label)+'</span></button>').join('');
    grid.scrollTop=0;
  }
  search.oninput=update;
  bindChoice($('#icon-group',picker),categories,'',key=>{category=key;update();});
  $('#auto-icon',picker)?.addEventListener('click',()=>select(''));
  grid.onclick=event=>{const item=event.target.closest('[data-glyph]');if(item)select(item.dataset.glyph);};
  update();
  search.focus();
}
function openFavorite(existing) {
  let icon=existing?.icon ?? (kind==='site'?'globe':'cpu');
  let automatic=kind==='site';
  const dialog=openDialog(existing?'Изменить избранное':'Добавить в избранное','<div id="favorite-fields"><form class="form" id="favorite-form"><label>Название<input name="title" required maxlength="80" value="'+escape(existing?.title)+'"></label><label>'+(kind==='site'?'Адрес сайта':'Файл приложения')+'<input name="target" '+(kind==='site'?'type="url"':'readonly')+' required value="'+escape(existing?.target)+'"></label>'+(kind==='app'?'<button type="button" id="choose-app">Выбрать приложение</button>':'')+'<div class="actions"><span class="sphere" id="chosen-icon">'+glyph(icon)+'</span><button type="button" id="choose-icon">Библиотека значков</button></div><div class="actions"><button class="primary">Сохранить</button>'+(existing?'<button type="button" id="remove-favorite">Удалить</button>':'')+'</div></form></div><div id="icon-picker" hidden></div>');
  const form=$('#favorite-form',dialog);
  const target=form.elements.target;
  if(kind==='site')$('#choose-icon',dialog).parentElement.insertAdjacentHTML('beforeend',monoOption);
  const preview=siteIconStyle(target,(key,mono)=>$('#chosen-icon',dialog).innerHTML=favoriteGlyph(key,icon,mono),'',()=>automatic,form.elements.monochrome,false);
  $('#choose-app',dialog)?.addEventListener('click',async()=>{
    try {const path=await call('pickApp'); if(path) target.value=path;}
    catch(error){feedback(error.message);}
  });
  $('#choose-icon',dialog).onclick=()=>{
    const picker=$('#icon-picker',dialog);
    const fields=$('#favorite-fields',dialog);
    fields.hidden=true;
    picker.hidden=false;
    const heading=$('.page-head h1, .page-head h2',dialog);
    const label=heading.textContent;
    heading.textContent='Значки';
    const backButton=$('[data-close]',dialog);
    const backLabel=backButton.textContent;
    backButton.innerHTML='<span>К форме</span>';
    function back(){heading.textContent=label;backButton.innerHTML='<span>'+escape(backLabel)+'</span>';backButton.onclick=null;picker.hidden=true;fields.hidden=false;$('#choose-icon',dialog).focus();}
    backButton.onclick=event=>{event.stopPropagation();back();};
    fillIconPicker(picker,automatic?'':icon,key=>{automatic=!key;icon=key||'globe';preview.update();back();},kind==='site');
  };
  form.onsubmit=async event=>{
    event.preventDefault();
    const title=form.elements.title.value.trim();
    const value=target.value.trim();
    if(!title||!value){feedback('Заполните название и адрес.');return;}
    if(kind==='site'&&!/^https?:\/\//i.test(value)){feedback('Введите адрес HTTP или HTTPS.');return;}
    const submit=$('button.primary',form);
    submit.disabled=true;
    const iconStyle=await preview.value();
    if(!form.isConnected)return;
    submit.disabled=false;
    if(target.value.trim()!==value||form.elements.title.value.trim()!==title)return;
    const item={id:existing?.id??id(),kind,title,target:value,icon,...iconStyle,iconSource:automatic?'site':'library'};
    if(existing) Object.assign(existing,item); else state.favorites.push(item);
    save();closeView();
  };
  $('#remove-favorite',dialog)?.addEventListener('click',()=>{state.favorites=state.favorites.filter(f=>f.id!==existing.id);save();closeView();});
}
function openReminder(existing) {
  const initial=new Date(existing?.due??Date.now()+600000);
  let repeat=existing?.repeat??'none';
  let day=initial;
  let hours=initial.getHours(), minutes=initial.getMinutes();
  const dialog=openDialog(existing?'Изменить напоминание':'Новое напоминание','<form class="form" id="reminder-form"><label>Название<input name="title" maxlength="120" required value="'+escape(existing?.title)+'"></label><div class="icon-filters">'+pickerField('reminder-day','Дата',formatDate(initial),'date')+pickerField('reminder-time','Время',formatTime(initial),'time')+'</div><div class="icon-filters"><div class="field">Частота повтора'+makeChoice('reminder-repeat','Частота повтора',repeatOptions,repeat)+'</div><label id="custom-repeat"'+(repeat==='custom'?'':' hidden')+'>Интервал в минутах<input name="repeatMinutes" inputmode="numeric" pattern="[0-9]+" value="'+escape(existing?.repeatMinutes??10)+'"></label></div><div class="actions">'+iconAction('Сохранить','save','class="primary"','submit')+(existing?iconAction('Удалить напоминание','remove','id="remove-reminder"'):'')+'</div></form>',true);
  const form=$('#reminder-form',dialog);
  const custom=$('#custom-repeat',form);
  const interval=form.elements.repeatMinutes;
  function syncRepeat() {
    custom.hidden=repeat!=='custom';
    interval.disabled=custom.hidden;
    interval.required=!custom.hidden;
  }
  bindChoice($('#reminder-repeat',form),repeatOptions,repeat,key=>{repeat=key;syncRepeat();if(key==='custom')interval.focus();});
  syncRepeat();
  bindDatePicker($('#reminder-day',form),initial,value=>{day=value;});
  bindTimePicker($('#reminder-time',form),initial,(h,m)=>{hours=h;minutes=m;});
  form.onsubmit=event=>{
    event.preventDefault();
    const title=form.elements.title.value.trim();
    if(!title){feedback('Укажите название напоминания.');return;}
    const scheduled=new Date(day.getFullYear(),day.getMonth(),day.getDate(),hours,minutes);
    if(scheduled.getHours()!==hours||scheduled.getMinutes()!==minutes){feedback('Это время недоступно в текущем часовом поясе.');return;}
    const due=scheduled.getTime();
    const repeatMinutes=Number(interval.value);
    if(repeat==='custom'&&(!Number.isSafeInteger(repeatMinutes)||repeatMinutes<1||!Number.isFinite(new Date(due+repeatMinutes*minute).getTime()))){
      feedback('Укажите допустимый интервал целым числом минут.');interval.focus();return;
    }
    const item={id:existing?.id??id(),title,due,repeat,done:false};
    if(repeat==='custom')item.repeatMinutes=repeatMinutes;
    if(existing) {
      Object.assign(existing,item);
      delete existing.scheduled;
      if(repeat!=='custom')delete existing.repeatMinutes;
    } else state.reminders.push(item);
    dismissed.delete(item.id);save();closeView();
  };
  $('#remove-reminder',dialog)?.addEventListener('click',()=>{state.reminders=state.reminders.filter(r=>r.id!==existing.id);save();closeView();});
}
function nextOccurrence(reminder, now) {
  const scheduled=reminder.scheduled??reminder.due;
  if(reminder.repeat==='daily'||reminder.repeat==='weekly') {
    const next=new Date(scheduled), days=reminder.repeat==='daily'?1:7;
    const steps=Math.max(1,Math.floor((now-scheduled)/(86400000*days)));
    next.setDate(next.getDate()+steps*days);
    while(next.getTime()<=now) next.setDate(next.getDate()+days);
    return next.getTime();
  }
  const minutes=reminder.repeat==='custom'?reminder.repeatMinutes:Number(reminder.repeat);
  if(!Number.isSafeInteger(minutes)||minutes<1)return null;
  const interval=minutes*minute;
  const next=scheduled+interval*Math.max(1,Math.floor((now-scheduled)/interval)+1);
  return Number.isFinite(new Date(next).getTime())?next:null;
}
function done(key, dismiss=false) {
  const reminder=state.reminders.find(r=>r.id===key);
  if(!reminder) return;
  const next=nextOccurrence(reminder,Date.now());
  if(next===null&&dismiss){dismissed.add(key);showNotice();return;}
  if(next===null)reminder.done=true;
  else {
    reminder.due=next;
    delete reminder.scheduled;
  }
  save();render();
}
function showNotice() {
  const r=state.reminders.filter(r=>!r.done&&r.due<=Date.now()&&!dismissed.has(r.id)).sort((a,b)=>a.due-b.due)[0];
  const next=r?.id??null;
  if(next===noticeId) return;
  noticeId=next;
  $('#notice').innerHTML=r?'<section class="panel stack"><div class="between"><span class="muted">Напоминание</span>'+iconAction('Закрыть','cancel','data-dismiss="'+escape(r.id)+'"')+'</div><h2>'+escape(r.title)+'</h2><p>'+date(r.due)+'</p><div class="actions">'+iconAction('Готово','save','class="primary" data-done="'+escape(r.id)+'"')+iconAction('Отложить на 10 мин','snooze','data-snooze="'+escape(r.id)+'"')+'</div></section>':'';
}
async function send(form) {
  if (busy) return;
  const value=form.querySelector('input').value.trim();
  if(!value) return;
  if(route==='home') quick=true;
  draft='';chatError='';busy=true;pageNumbers.delete('chat');render();
  try {await call('chat',value);}
  catch(error){chatError=error.message;}
  finally{busy=false;pageNumbers.delete('chat');render();}
}
const modelValues=form=>Object.fromEntries([...new FormData(form)].map(([k,v])=>[k,k==='clearKey'?true:v]));
document.addEventListener('submit',event=>{
  const form=event.target;
  if(form.matches('.compose')){event.preventDefault();send(form);}
  if(form.id==='model-form'){
    event.preventDefault();
    const status=$('#model-status',form);
    call('saveModel',modelValues(form)).then(()=>call('getModel')).then(value=>{model=value;form.elements.key.value='';form.elements.clearKey.checked=false;form.elements.key.placeholder=model.hasKey?'Ключ сохранён':'Введите ключ, если нужен';status.textContent='Настройки сохранены';}).catch(error=>{status.textContent=error.message;});
  }
});
document.addEventListener('input',event=>{
  const input=event.target;
  if(input.closest('.compose')) draft=input.value;
  if(input.id==='note-title'||input.id==='note-body'){
    const note=state.notes.find(n=>n.id===currentNote);
    note[input.id==='note-title'?'title':'body']=input.value;
    if(input.id==='note-title') {const label=$('[data-note-open="'+note.id+'"] span');if(label) label.textContent=input.value||'Без названия';}
    clearTimeout(saveTimer);saveTimer=setTimeout(save,230);
  }
  if(input.id==='note-search') {
    const caret=input.selectionStart;
    window.noteQuery=input.value;pageNumbers.delete('notes');render();
    const search=$('#note-search');search.focus();search.setSelectionRange(caret,caret);
  }
  if(input.id==='theme-toggle'){state.themed=input.checked;save();render();}
  if(input.id==='autostart')call('setAutoStart',input.checked).then(()=>{autoStart=input.checked;}).catch(error=>{input.checked=autoStart;feedback(error.message);});
});
document.addEventListener('click',async event=>{
  const b=event.target.closest('button');
  if(!b) return;
  const d=b.dataset;
  try {
    if(d.playback){await call('desktopPlayback',{command:d.playback,index:Number(d.index??0)});feedback('Движение обновлено');return;}
    if(d.section){await call('openSection',d.section);return;}
    if(d.pageKey){
      pageNumbers.set(d.pageKey,Number(d.page));render();
      if(d.pageKey==='chat'){
        const direction=b.getAttribute('aria-label');
        const pager=$('.pager',main);
        (pager?.querySelector('button[aria-label="'+direction+'"]:enabled')??pager?.querySelector('button:enabled'))?.focus();
      }
      return;
    }
    if('close' in d){closeView();return;}
    if(d.go==='quick'){quick=true;menu=false;render();$('.quick input')?.focus();return;}
    if(d.go){go(d.go);return;}
    if(d.setting){go('settings',d.setting);return;}
    if(d.pet!==undefined){selectPet(pets[Number(d.pet)]);$('#pet-select',main)?.focus();return;}
    if(d.shareIndex!==undefined){editShareChannel(Number(d.shareIndex));return;}
    if(d.kind){kind=d.kind;render();return;}
    if(d.noteOpen){currentNote=d.noteOpen;noteEditing=true;go('notes');return;}
    if(d.open){const item=state.favorites.find(x=>x.id===d.open);await call('open',{kind:item.kind,target:item.target});return;}
    if(d.done){done(d.done);return;}
    if(d.dismiss){done(d.dismiss,true);return;}
    if(d.snooze){const r=state.reminders.find(x=>x.id===d.snooze);r.scheduled??=r.due;r.due=Date.now()+10*minute;save();render();return;}
    if(d.editFavorite){editFavoriteRow(b.closest('.favorite-row'),state.favorites.find(x=>x.id===d.editFavorite));return;}
    if(d.editReminder){openReminder(state.reminders.find(x=>x.id===d.editReminder));return;}
    switch (b.id) {
      case 'import-pet': await importPet();break;
      case 'manage-hotkeys': await openHotkeys();break;
      case 'add-share-channel':editShareChannel(shareChannels.length);break;
      case 'add-favorite':openFavorite();break;
      case 'add-reminder':openReminder();break;
      case 'add-note': {
        const note={id:id(),title:'',body:'',pinned:false};
        currentNote=note.id;noteEditing=true;window.noteQuery='';pageNumbers.delete('notes');state.notes.unshift(note);save();render();$('#note-title').focus();break;
      }
      case 'notes-back':noteEditing=false;render();break;
      case 'pin-note': {const n=state.notes.find(x=>x.id===currentNote);n.pinned=!n.pinned;save();render();break;}
      case 'remove-note':state.notes=state.notes.filter(x=>x.id!==currentNote);currentNote=null;noteEditing=false;save();render();break;
      case 'pet-menu':menu=!menu;render();break;
      case 'pause': {
        paused=!paused;
        b.querySelector('span').textContent=paused?'Продолжить':'Пауза';
        const sprite=$('#active-sprite');
        if(sprite)sprite.dataset.paused=String(paused);
        break;
      }
      case 'hide-pet':hidden=true;menu=false;render();break;
      case 'show-pet':hidden=false;render();break;
      case 'quick-close':quick=false;render();break;
      case 'motion': {
        openDialog('Движения',activePet().animations.map((c,i)=>button(c.name,'class="list-row" data-motion="'+i+'"','play')).join(''));
        for(const action of dialog.querySelectorAll('[data-motion]'))action.onclick=()=>{motion=Number(action.dataset.motion);dialog.close();menu=false;render();};
        break;
      }
      case 'test-model': {
        const f=b.form;if(!f.reportValidity())break;
        const status=$('#model-status',f);b.disabled=true;status.textContent='Проверяю…';
        try{status.textContent=await call('testModel',modelValues(f));}
        catch(error){status.textContent=error.message;}
        finally{b.disabled=false;}break;
      }
    }
  } catch(error){feedback(error.message);}
});
document.addEventListener('contextmenu',event=>{if(event.target.id==='active-sprite'){event.preventDefault();menu=!menu;render();}});
document.addEventListener('click',event=>{if(event.target.id!=='active-sprite')return;if(event.ctrlKey&&event.detail===2)go('notes');else if(event.detail===3){quick=true;render();$('.quick input')?.focus();}});
document.addEventListener('keydown',event=>{
  if(event.key!=='Escape')return;
  if(closePetList(true)){event.preventDefault();return;}
  const choice=$('.choice-page [data-choice-back]');
  if(choice){event.preventDefault();choice.click();return;}
  if(dialog.open)return;
  if($('#icon-picker:not([hidden])',main))$('[data-close]',main).click();
  else if(subpage)closeView();
  else if(noteEditing){noteEditing=false;render();}
  else if(menu||quick){menu=false;quick=false;render();}
});
document.addEventListener('pointerdown',event=>{if(!event.target.closest('.pet-dropdown'))closePetList();});
window.addEventListener('pagehide',()=>{if(saveTimer)save();});
window.addEventListener('resize',()=>{
  if(!subpage&&!noteEditing&&['chat','quick','favorites','notes','reminders','motion','pets'].includes(desktopSection))
    renderSection();
});
try {
  [groups,model,builtins] = await Promise.all([fetch('assets/icon-catalog.json').then(res=>res.json()),call('getModel'),fetch('assets/pets.json').then(res=>res.json())]);
  glyphs=new Map(groups.flatMap(group=>group.icons).map(icon=>[icon.id,icon]));
  const [saved,installed]=await Promise.all([call('load'),call('pets')]);
  if(saved)state={...state,...saved};
  pets=[...builtins,...installed];
  const first=await call('ready');
  if(first) await openSection(first); else render();
  setInterval(showNotice,1000);
} catch(error){main.textContent=error.message;}
