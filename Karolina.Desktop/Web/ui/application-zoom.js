/** Scale Markdown only; native window, navigation, composer and TOC stay fixed. */
export function registerMarkdownZoom(toast) {
    const key='karolina-markdown-scale';let factor=1;
    try { const saved=Number(localStorage.getItem(key));if(saved>=.6&&saved<=2)factor=saved; } catch {}
    const style=document.createElement('style');style.nonce=document.querySelector('meta[name="karolina-session"]').content;document.head.append(style);
    function paint() {
        const selectors='#markdown,#toolManual,#drawerBody .markdown';
        style.textContent=`${selectors}{font-size:${15*factor}px;} :is(${selectors}) h1{font-size:${29*factor}px;} :is(${selectors}) h2{font-size:${21*factor}px;} :is(${selectors}) h3{font-size:${17*factor}px;} :is(${selectors}) h4{font-size:${15*factor}px;} :is(${selectors}) h5,:is(${selectors}) h6{font-size:${14*factor}px;} :is(${selectors}) table{font-size:${13*factor}px;}`;
        document.documentElement.dataset.markdownZoom=String(factor);
        const button=document.getElementById('resetMarkdownZoom');if(button)button.textContent=`正文 ${Math.round(factor*100)}%`;
    }
    function change(delta) {factor=delta===0?1:Math.max(.6,Math.min(2,Math.round((factor+delta*.1)*10)/10));paint();try{localStorage.setItem(key,String(factor));}catch{toast('阅读缩放本次有效，浏览器未允许保存偏好');}}
    const readingTarget=e=>e.target?.closest?.('#markdown,#toolManual,#drawerBody .markdown');
    const wheel=e=>{if(!e.ctrlKey || !e.deltaY)return;e.preventDefault();if(readingTarget(e))change(e.deltaY<0?1:-1);};
    const keyboard=e=>{if(!e.ctrlKey || !['+','=','-','0'].includes(e.key))return;if(e.target?.matches?.('input,textarea'))return;e.preventDefault();if(readingTarget(e)||['requirement','plan','rule'].includes(document.body.dataset.page))change(e.key==='0'?0:e.key==='-'?-1:1);};
    document.addEventListener('wheel',wheel,{passive:false,capture:true});document.addEventListener('keydown',keyboard,true);
    const reset=document.getElementById('resetMarkdownZoom');if(reset)reset.onclick=()=>change(0);paint();
    return()=>{document.removeEventListener('wheel',wheel,true);document.removeEventListener('keydown',keyboard,true);style.remove();};
}
