export function openPicker(control, draw, fit = false) {
  const field=control.closest('[data-picker-field]');
  const panel=field.querySelector('.picker-popover');
  const listeners=new AbortController();
  const options={signal:listeners.signal};
  const reopen=control.onclick;
  let sizeObserver;
  function close(restoreFocus=true) {
    listeners.abort();
    sizeObserver?.disconnect();
    panel.hidden=true;
    panel.replaceChildren();
    if(fit)panel.removeAttribute('style');
    control.setAttribute('aria-expanded','false');
    control.onclick=reopen;
    if(restoreFocus)control.focus();
  }
  document.addEventListener('pointerdown',event=>{if(!field.contains(event.target))close(false);},options);
  document.addEventListener('keydown',event=>{
    if(event.key!=='Escape')return;
    event.preventDefault();event.stopPropagation();close();
  },{...options,capture:true});
  document.addEventListener('focusin',event=>{if(!field.contains(event.target))close(false);},options);
  const observer=new MutationObserver(()=>{if(!field.isConnected)close(false);});
  observer.observe(field.closest('main,dialog'),{childList:true,subtree:true});
  listeners.signal.addEventListener('abort',()=>observer.disconnect(),{once:true});
  control.setAttribute('aria-expanded','true');
  if(fit)panel.style.position='fixed';
  panel.hidden=false;
  control.onclick=()=>close();
  draw(panel,close);
  if(fit){
    function place() {
      if(panel.hidden||!control.isConnected)return;
      const anchor=control.getBoundingClientRect();
      if(panel.classList.contains('model-popover'))panel.style.width=Math.min(anchor.width,innerWidth-16)+'px';
      panel.style.maxHeight=Math.max(40,innerHeight-16)+'px';
      panel.style.overflowY='auto';
      const bounds=panel.getBoundingClientRect();
      panel.style.inset='auto';
      panel.style.left=Math.max(8,Math.min(anchor.left,innerWidth-bounds.width-8))+'px';
      const below=anchor.bottom+5;
      const top=below+bounds.height<=innerHeight-8?below:anchor.top-bounds.height-5;
      panel.style.top=Math.max(8,Math.min(top,innerHeight-bounds.height-8))+'px';
    }
    place();
    sizeObserver=new ResizeObserver(place);
    sizeObserver.observe(panel);
    window.addEventListener('resize',place,options);
    document.addEventListener('scroll',place,{...options,capture:true});
  }
}
