import { layoutGraph } from './project-graph-layout.js';
import { graphCategory } from './project-graph-category.js';
import { projectPoint, fitCamera, panCamera, rotateCamera, zoomCamera, pickNode } from './project-graph-camera.js';
import { createGraphRenderer } from './project-graph-renderer.js';
export { layoutGraph, graphCategory, projectPoint };

export function mountGraphView(container, graph, { onNode }) {
    const byId = new Map(graph.nodes.map(n => [n.id, n])), links = new Map();
    for (const e of graph.edges) for (const id of [e.from,e.to]) { if (!links.has(id)) links.set(id,[]); links.get(id).push(e); }
    const frame = document.createElement('div'); frame.className = 'graph-canvas-frame graph-overview-frame';
    const controls = document.createElement('div'); controls.className = 'graph-canvas-controls';
    const stage = document.createElement('div'); stage.className = 'graph-3d-stage';
    const canvas = document.createElement('canvas'); canvas.className = 'graph-canvas graph-overview-canvas graph-3d-canvas'; canvas.tabIndex = 0;
    canvas.setAttribute('aria-label','全量三维工程图，左键平移，右键或Shift加左键旋转，滚轮缩放，点击节点查看详情');
    Object.assign(canvas.dataset, { nodes: graph.nodes.length, edges: graph.edges.length, dimensions: 3, layoutState: 'loading' });
    const labels = document.createElement('div'); labels.className = 'graph-3d-labels'; labels.setAttribute('aria-hidden','true');
    const hint = document.createElement('p'); hint.className = 'graph-canvas-hint'; hint.textContent = '正在后台计算三维关联布局…';
    stage.append(canvas,labels); frame.append(controls,stage,hint); container.append(frame);
    const abort = new AbortController(), signal = abort.signal;
    const camera = { yaw: .35, pitch: .2, distance: 1000, target: { x: 0, y: 0, z: 0 } };
    let layout = null, renderer = null, worker = null, width = 1, height = 1, dpr = 1, selected = null, hovered = null, pendingLocate = null;
    let projected = [], drag = null, queued = null, fitted = false, destroyed = false, light = false;
    function colors() { light = document.documentElement.dataset.themeMode === 'light' || getComputedStyle(container).colorScheme === 'light'; }
    function fail(message) { if (destroyed) return; canvas.dataset.layoutState = 'error'; hint.textContent = '三维图不可用：' + message; retry.hidden = false; }
    function neighbors() { const set = new Set([selected]); for (const e of links.get(selected) || []) { set.add(e.from); set.add(e.to); } return set; }
    function drawLabels(related) {
        labels.replaceChildren(); const candidates = [], occupied = [];
        for (const p of projected) if (p.visible && p.x >= 0 && p.x <= width && p.y >= 0 && p.y <= height) {
            const priority = p.id === selected ? 0 : p.id === hovered ? 1 : related.has(p.id) ? 2 : 3;
            if (priority < 2 || priority === 2 && p.size > 5 || p.size > 12) candidates.push({ ...p, priority });
        }
        candidates.sort((a,b) => a.priority - b.priority || a.depth - b.depth);
        for (const p of candidates) {
            const n = byId.get(p.id), full = n.kind === 'C# 类型' ? n.title.split('.').pop() : n.title;
            const title = full.length > 28 ? full.slice(0,27) + '…' : full;
            const box = { x: p.x + p.size / 2 + 4, y: p.y - 8, width: title.length * 7 + 10, height: 18 };
            if (p.priority && occupied.some(b => box.x < b.x+b.width && box.x+box.width > b.x && box.y < b.y+b.height && box.y+box.height > b.y)) continue;
            occupied.push(box); const label = document.createElement('span'); label.className = p.priority === 0 ? 'selected' : ''; label.textContent = title;
            label.style.left = box.x + 'px'; label.style.top = box.y + 'px'; labels.append(label);
        }
    }
    function draw() {
        queued = null; if (!layout || !renderer || destroyed || !stage.clientWidth || !stage.clientHeight) return;
        const related = neighbors();
        camera.far = camera.distance + layout.radius * 4 + 100;
        try { renderer.draw(camera,width,height,dpr,selected,related,light); }
        catch (e) { fail(e.message); return; }
        projected = graph.nodes.map(node => {
            const p = projectPoint(layout.positions.get(node.id),camera,width,height), size = Math.max(2.8,Math.min(24,(node.id === selected ? 12 : 7)*2*p.focal/Math.max(1,p.depth)));
            return { ...p, id: node.id, size };
        });
        drawLabels(related);
        Object.assign(canvas.dataset, { renderer: 'WebGL', layoutMethod: layout.method, yaw: camera.yaw, pitch: camera.pitch, distance: camera.distance,
            targetX: camera.target.x, targetY: camera.target.y, targetZ: camera.target.z, selectedId: selected || '',
            visibleNodes: projected.filter(p => p.visible && p.x >= 0 && p.x <= width && p.y >= 0 && p.y <= height).length,
            nodeVertices: renderer.stats.nodeVertices, edgeVertices: renderer.stats.edgeVertices, depthTest: 'true' });
    }
    function schedule() { if (!destroyed && queued === null) queued = requestAnimationFrame(draw); }
    function fit() { if (!layout) return; fitCamera(camera,layout.radius,width,height); fitted = true; schedule(); }
    function zoom(factor) { if (layout) { zoomCamera(camera,factor,layout.radius); schedule(); } }
    function select(id) { if (id && !byId.has(id)) return; selected = id; schedule(); }
    function locate(id) {
        if (!layout) { pendingLocate = id; return; }
        const p = layout.positions.get(id); if (!p) return;
        selected = id; camera.target = { ...p }; camera.distance = 280; fitted = true; schedule();
    }
    const point = event => { const r = canvas.getBoundingClientRect(); return { x: event.clientX-r.left, y: event.clientY-r.top }; };
    canvas.addEventListener('contextmenu',e => e.preventDefault(),{signal});
    canvas.addEventListener('pointerdown',event => {
        if (!layout || event.button !== 0 && event.button !== 2) return;
        event.preventDefault(); canvas.focus(); drag = { ...point(event), pointer: event.pointerId, total: 0, button: event.button, rotate: event.button === 2 || event.shiftKey }; canvas.setPointerCapture(event.pointerId);
    },{signal});
    canvas.addEventListener('pointermove',event => {
        const p = point(event);
        if (drag && drag.pointer === event.pointerId) {
            const dx = p.x-drag.x,dy = p.y-drag.y; drag.total += Math.abs(dx)+Math.abs(dy);
            if (drag.total > 3) { if (drag.rotate) rotateCamera(camera,dx,dy); else panCamera(camera,dx,dy,height); schedule(); }
            drag.x = p.x; drag.y = p.y;
        } else {
            const id = pickNode(projected,p.x,p.y); canvas.title = id ? `${byId.get(id).title} · ${byId.get(id).kind}` : '';
            if (hovered !== id) { hovered = id; schedule(); }
        }
    },{signal});
    canvas.addEventListener('pointerup',event => {
        if (!drag || drag.pointer !== event.pointerId) return;
        if (drag.total <= 3 && drag.button === 0 && !drag.rotate) { const p = point(event),id = pickNode(projected,p.x,p.y); if (id) { select(id); onNode(id); } }
        drag = null;
    },{signal});
    canvas.addEventListener('pointercancel',() => { drag = null; },{signal});
    canvas.addEventListener('pointerleave',() => { if (!drag) { hovered = null; schedule(); } },{signal});
    canvas.addEventListener('wheel',event => { event.preventDefault(); zoom(event.deltaY < 0 ? 1.18 : 1/1.18); },{passive:false,signal});
    canvas.addEventListener('keydown',event => {
        if (event.key === '+' || event.key === '=') { event.preventDefault(); zoom(1.25); }
        if (event.key === '-') { event.preventDefault(); zoom(.8); }
        if (event.key.startsWith('Arrow')) { event.preventDefault(); rotateCamera(camera,event.key==='ArrowLeft'?-12:event.key==='ArrowRight'?12:0,event.key==='ArrowUp'?-12:event.key==='ArrowDown'?12:0); schedule(); }
    },{signal});
    for (const [label,action] of [['＋',()=>zoom(1.25)],['−',()=>zoom(.8)],['适应全图',fit],['定位选中',()=>selected&&locate(selected)],['重置方向',()=>{camera.yaw=.35;camera.pitch=.2;schedule();}]]) {
        const b=document.createElement('button');b.type='button';b.textContent=label;b.onclick=action;controls.append(b);
    }
    const retry=document.createElement('button');retry.type='button';retry.textContent='重试显示';retry.hidden=true;controls.append(retry);
    const count=document.createElement('span');count.className='graph-overview-count';count.textContent=`${graph.nodes.length.toLocaleString()} 节点 · ${graph.edges.length.toLocaleString()} 关系 · 3D`;controls.append(count);
    function initRenderer() {
        if (!layout || destroyed) return;
        try { renderer?.dispose(); renderer=createGraphRenderer(canvas,graph,layout);canvas.dataset.layoutState='ready';retry.hidden=true;
            hint.textContent='左键平移 · 右键 / Shift＋左键旋转 · 滚轮缩放 · 点击节点看详情';schedule();
        } catch(e) { renderer=null;fail(e.message); }
    }
    retry.onclick=()=>{if(layout)initRenderer();else startLayout();};
    canvas.addEventListener('webglcontextlost',event=>{event.preventDefault();renderer=null;fail('图形上下文丢失，等待恢复');},{signal});
    canvas.addEventListener('webglcontextrestored',initRenderer,{signal});
    function startLayout() {
        worker?.terminate();canvas.dataset.layoutState='loading';retry.hidden=true;
        try {
            const activeWorker=new Worker(new URL('./project-graph-layout-worker.js',import.meta.url),{type:'module'});worker=activeWorker;
            worker.onmessage=event=>{if(destroyed||worker!==activeWorker)return;if(event.data.error){activeWorker.terminate();fail(event.data.error);return;}
                layout=event.data.layout;activeWorker.terminate();fit();if(pendingLocate){locate(pendingLocate);pendingLocate=null;}initRenderer();};
            worker.onerror=event=>{event.preventDefault();if(destroyed||worker!==activeWorker)return;activeWorker.terminate();fail(event.message||'布局线程不可用');};
            worker.postMessage({nodes:graph.nodes,edges:graph.edges});
        } catch(e) { fail(e.message); }
    }
    const resize=new ResizeObserver(()=>{
        const box=stage.getBoundingClientRect();if(!box.width||!box.height)return;
        width=box.width;height=box.height;dpr=Math.min(devicePixelRatio||1,2);canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);
        if(!fitted)fit();else schedule();
    });
    document.addEventListener('karolina:appearance',()=>{colors();schedule();},{signal});colors();resize.observe(stage);startLayout();
    return {select,locate,fit,destroy(){destroyed=true;worker?.terminate();renderer?.dispose();abort.abort();resize.disconnect();if(queued!==null)cancelAnimationFrame(queued);frame.remove();}};
}
