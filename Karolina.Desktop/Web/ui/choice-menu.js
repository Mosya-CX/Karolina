/** Upward popovers for the composer; native selects remain the state contract. */
export function createChoiceMenus() {
    const nonce = document.querySelector('meta[name="karolina-session"]').content;
    const style = document.createElement('style'); style.nonce = nonce; document.head.append(style);
    let opened = null;
    const entries = ['model', 'effort', 'access'].map(id => {
        const select=document.getElementById(id), wrapper=document.createElement('span'),button=document.createElement('button'),caption=document.createElement('span'),menu=document.createElement('div');
        wrapper.className='choice-control';select.classList.add('choice-native');select.tabIndex=-1;
        button.type='button';button.className='choice-button';button.dataset.kNative='true';button.setAttribute('aria-haspopup','listbox');button.setAttribute('aria-expanded','false');button.setAttribute('aria-label',select.getAttribute('aria-label'));caption.className='choice-caption';button.append(caption,document.createTextNode(' ▴'));
        menu.id=id+'ChoiceMenu';menu.className='choice-menu';menu.setAttribute('popover','manual');menu.setAttribute('role','listbox');menu.setAttribute('aria-label',select.getAttribute('aria-label'));button.setAttribute('aria-controls',menu.id);
        select.before(wrapper);wrapper.append(select,button);document.body.append(menu);
        const signature=()=>JSON.stringify([select.disabled,select.value,[...select.options].map(o=>[o.value,o.textContent,o.disabled])]);
        let openedSignature='';
        function close() {if(menu.matches(':popover-open')) menu.hidePopover();button.setAttribute('aria-expanded','false');if(opened?.menu===menu)opened=null;}
        function sync() {caption.textContent=select.selectedOptions[0]?.textContent || '请选择';button.disabled=select.disabled;if(select.disabled)close();}
        function position() {const rect=button.getBoundingClientRect();style.textContent=`#${menu.id}{left:${Math.min(rect.left,Math.max(8,innerWidth-320))}px;bottom:${innerHeight-rect.top+8}px;width:${Math.min(360,Math.max(220,rect.width))}px;max-height:${Math.max(80,rect.top-24)}px;}`;}
        function open() {
            if(select.disabled)return;opened?.close();menu.replaceChildren();
            for(const option of select.options) {const item=document.createElement('button');item.type='button';item.dataset.kNative='true';item.setAttribute('role','option');item.setAttribute('aria-selected',String(option.selected));item.disabled=option.disabled;item.textContent=option.textContent;item.title=option.title;item.onclick=()=>{select.value=option.value;select.dispatchEvent(new Event('change',{bubbles:true}));sync();close();button.focus();};menu.append(item);}
            position();openedSignature=signature();menu.showPopover();button.setAttribute('aria-expanded','true');opened={menu,close,position};(menu.querySelector('[aria-selected="true"]:not(:disabled)')||menu.querySelector('button:not(:disabled)'))?.focus();
        }
        button.onclick=()=>menu.matches(':popover-open')?close():open();button.onkeydown=e=>{if(['ArrowUp','ArrowDown'].includes(e.key)){e.preventDefault();open();}};
        menu.onkeydown=e=>{if(e.key==='Escape'){e.preventDefault();close();button.focus();}else if(['ArrowUp','ArrowDown','Home','End'].includes(e.key)){e.preventDefault();const options=[...menu.querySelectorAll('button:not(:disabled)')],current=options.indexOf(document.activeElement);const index=e.key==='Home'?0:e.key==='End'?options.length-1:(current+(e.key==='ArrowUp'?-1:1)+options.length)%options.length;options[index]?.focus();}};
        const observer=new MutationObserver(()=>{sync();if(menu.matches(':popover-open')&&signature()!==openedSignature)close();});observer.observe(select,{childList:true,subtree:true,attributes:true,characterData:true});select.addEventListener('change',sync);sync();
        return{sync,dispose(){observer.disconnect();close();menu.remove();}};
    });
    const outside=e=>{if(opened&&!opened.menu.contains(e.target)&&!e.target.closest('.choice-control'))opened.close();};
    const resize=()=>opened?.position();document.addEventListener('pointerdown',outside);window.addEventListener('resize',resize);
    return{sync:()=>entries.forEach(e=>e.sync()),dispose(){entries.forEach(e=>e.dispose());style.remove();document.removeEventListener('pointerdown',outside);window.removeEventListener('resize',resize);}};
}
