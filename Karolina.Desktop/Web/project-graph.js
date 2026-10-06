import { mountGraphView } from './project-graph-view.js';

/** 全量画布与详情分离；搜索/选择节点只定位，不替换图谱。 */
export function registerProjectGraph({ state: ctx, actions, ui, api }) {
    const { $, toast } = ui;
    ctx.graph = { nodes: [], status: null, selectedId: null, request: 0, polling: null };
    ctx.graphContextNodes = new Map();
    const graphDetail = $('graphDetail');
    let view = null, inspector = null, loadedAt = null, detailRequest = 0, loading = null;
    const contextStrip = document.createElement('div'); contextStrip.id = 'graphContextState'; contextStrip.className = 'graph-context-state'; contextStrip.hidden = true;
    const contextCount = document.createElement('span'); contextCount.id = 'graphContextCount';
    const clear = document.createElement('button'); clear.className = 'quiet'; clear.textContent = '清除图谱资料';
    clear.onclick = () => { ctx.graphContextNodes.clear(); renderGraphContext(); };
    contextStrip.append(contextCount, clear); $('references').after(contextStrip);
    for (const kind of ['工程文档', '工程文件']) if (![...$('graphKind').options].some(o => o.value === kind)) $('graphKind').add(new Option(kind, kind));
    Object.assign(actions, { renderGraphDirectory, loadProjectGraph, refreshGraphStatus, openGraphNode, renderGraphContext, selectGraphNode: openGraphNode, observeGraphStatus });

    function paintStatus(status) {
        ctx.graph.status = status;
        $('graphStatus').textContent = status.indexing ? `${status.stage} · 已扫描 ${status.filesScanned} 个文件` : status.error ? `${status.stage} · ${status.error}` : status.indexed ? `${status.nodes.toLocaleString()} 节点 / ${status.edges.toLocaleString()} 关系 · ${new Date(status.lastSuccessfulIndex).toLocaleString()}` : status.stage;
        $('graphStatus').title = '全量三维关联图';
        const notice = $('graphNotice'); notice.classList.toggle('stale', !!status.isStale);
        const coverage = status.coverage;
        notice.textContent = `${status.isStale ? '索引过期，请刷新以更新工程事实。' : ''}${coverage ? `索引 ${coverage.fileNodes} 个工程文件，包含代码、资源、文档和工具；${coverage.isolatedNodes} 个孤立节点也在总图内。` : '旧索引缺少全工程覆盖信息，请刷新索引。'}跳过解析 ${status.skippedFiles} 项，未解析 GUID ${status.unresolvedGuids} 项；节点存在不代表内部关系都已解析。`;
        if ($('settingsGraphStatus')) $('settingsGraphStatus').textContent = $('graphStatus').textContent;
        for (const button of [$('buildProjectGraph'), $('settingsBuildGraph')]) if (button) button.disabled = status.indexing;
        document.dispatchEvent(new CustomEvent('karolina:graph-state', { detail: status }));
        if (status.indexing && !ctx.graph.polling) ctx.graph.polling = setInterval(() => refreshGraphStatus().catch(e => toast(e.message)), 900);
        if (!status.indexing && ctx.graph.polling) { clearInterval(ctx.graph.polling); ctx.graph.polling = null; }
    }
    function observeGraphStatus(status) {
        if (!status) return;
        paintStatus(status);
        if (ctx.page === 'graph' && status.indexed && !status.indexing && loadedAt !== status.lastSuccessfulIndex)
            loadOverview().catch(e => toast('读取总图失败：' + e.message));
    }
    async function refreshGraphStatus() { const status = await api('project-graph/status'); observeGraphStatus(status); return status; }
    async function loadOverview() {
        if (ctx.page !== 'graph') return;
        if (loading) return loading;
        const request = ctx.graph.request;
        const pending = (async () => {
            const graph = await api('project-graph/overview');
            if (request !== ctx.graph.request || ctx.page !== 'graph') return;
            const selectedId = ctx.graph.selectedId;
            ++detailRequest;
            view?.destroy(); graphDetail.replaceChildren(); graphDetail.classList.remove('empty');
            const workspace = document.createElement('div'); workspace.className = 'graph-workspace';
            const visual = document.createElement('div'); visual.className = 'graph-visual';
            const legend = document.createElement('div'); legend.className = 'graph-legend';
            for (const [category, label] of [['code','代码'],['type','类型'],['scene','场景'],['prefab','Prefab'],['art','模型 / 动画'],['resource','资源'],['document','文档'],['file','工程文件']]) {
                const entry = document.createElement('span'); entry.className = `graph-legend-${category}`; entry.textContent = label; legend.append(entry);
            }
            visual.append(legend); inspector = document.createElement('aside'); inspector.className = 'graph-inspector'; inspector.textContent = '点击总图中的节点查看详情。搜索可快速定位文件，图谱始终保留全量节点。';
            workspace.append(visual, inspector); graphDetail.append(workspace);
            ctx.graph.nodes = graph.nodes; loadedAt = graph.status.lastSuccessfulIndex;
            if (!graph.nodes.some(n => n.id === selectedId)) ctx.graph.selectedId = null;
            view = mountGraphView(visual, graph, { onNode: id => openGraphNode(id) });
            paintStatus(graph.status); renderGraphDirectory();
            if (selectedId && graph.nodes.some(n => n.id === selectedId)) await openGraphNode(selectedId);
        })();
        loading = pending;
        try { await pending; } finally { if (loading === pending) loading = null; }
    }
    function renderGraphDirectory() {
        if (ctx.page !== 'graph') return;
        $('search').placeholder = '搜索节点名称或类型…';
        $('sidebarNote').textContent = '3D空间按实际关联排布；点击只显示详情，用“定位选中”移动视野。';
        const directory = $('directory'); directory.replaceChildren();
        const query = $('search').value.trim().toLowerCase(), kind = $('graphKind').value;
        const terms = query.split(/\s+/).filter(Boolean);
        if (!query && !kind) {
            const overview = document.createElement('button'); overview.textContent = '适应全图'; overview.onclick = () => view?.fit(); directory.append(overview);
            for (const type of [...new Set(ctx.graph.nodes.map(n => n.kind))]) {
                const item = document.createElement('p'); item.className = 'graph-directory-summary'; item.textContent = `${type} · ${ctx.graph.nodes.filter(n => n.kind === type).length}`; directory.append(item);
            }
            const coverage = ctx.graph.status?.coverage;
            if (coverage) {
                const details = document.createElement('details'); details.className = 'graph-coverage-details';
                const title = document.createElement('summary'); title.textContent = '索引覆盖与排除范围';
                const body = document.createElement('p'); body.textContent = `代码 ${coverage.codeFiles}、资源 ${coverage.unityResources}、文档 ${coverage.documents}、其它文件 ${coverage.otherFiles}。缓存、构建输出、助手私有状态和.meta展示节点不参与图谱。`; details.append(title, body); directory.append(details);
            }
        } else {
            const matches = ctx.graph.nodes.filter(n => (!kind || n.kind === kind) && terms.every(term => `${n.title} ${n.path} ${n.symbol || ''}`.toLowerCase().includes(term)));
            const summary = document.createElement('p'); summary.className = 'graph-directory-summary'; summary.textContent = `${matches.length} 个匹配 · 总图仍含 ${ctx.graph.nodes.length} 节点`; directory.append(summary);
            for (const node of matches.slice(0, 100)) {
                const button = document.createElement('button'); button.className = 'graph-node-entry'; button.classList.toggle('active', node.id === ctx.graph.selectedId);
                const title = document.createElement('span'); title.className = 'title'; title.textContent = node.title;
                const kindLabel = document.createElement('small'); kindLabel.textContent = node.kind; button.append(title, kindLabel);
                button.onclick = () => openGraphNode(node.id); directory.append(button);
            }
            if (matches.length > 100) { const note = document.createElement('p'); note.className = 'graph-directory-summary'; note.textContent = '搜索列表先显示100项，请缩小关键词；画布节点没有截断。'; directory.append(note); }
        }
        if (!view && ctx.graph.status?.indexed) loadOverview().catch(e => toast(e.message));
    }
    async function openGraphNode(id) {
        if (ctx.page !== 'graph' || !inspector) return;
        ctx.graph.selectedId = id; view?.select(id); const request = ++detailRequest;
        inspector.textContent = '正在读取节点详情…'; renderGraphDirectory();
        try {
            const detail = await api('project-graph/node?id=' + encodeURIComponent(id));
            if (ctx.page !== 'graph' || request !== detailRequest) return;
            const node = detail.node; inspector.replaceChildren();
            const h = document.createElement('h2'); h.textContent = node.title;
            const kind = document.createElement('span'); kind.className = 'badge'; kind.textContent = node.kind;
            const add = document.createElement('button'); add.textContent = ctx.graphContextNodes.has(id) ? '已加入对话资料' : '加入对话资料'; add.disabled = ctx.graphContextNodes.has(id);
            add.onclick = () => { ctx.graphContextNodes.set(id, node); renderGraphContext(); add.textContent = '已加入对话资料'; add.disabled = true; };
            inspector.append(kind, h, add);
            if (detail.truncated) { const p = document.createElement('p'); p.textContent = '详情每个方向最多200条关系，列表已截断；总图包含全部关系。'; inspector.append(p); }
            for (const [label, edges, direction] of [['引用 / 声明',detail.outgoing,'out'],['被引用',detail.incoming,'in']]) {
                const section = document.createElement('section'); section.className = 'graph-relations';
                const heading = document.createElement('h3'); heading.textContent = `${label}（${edges.length}）`; section.append(heading);
                if (!edges.length) { const p = document.createElement('p'); p.textContent = '没有已解析关系；节点仍保留在总图中。'; section.append(p); }
                const related = new Map(detail.relatedNodes.map(n => [n.id, n]));
                for (const edge of edges) {
                    const targetId = direction === 'out' ? edge.to : edge.from, target = related.get(targetId);
                    const row = document.createElement('button'); row.className = 'graph-relation'; row.textContent = `${edge.relation} · ${target?.title || targetId}`;
                    row.title = target?.kind || ''; row.onclick = () => openGraphNode(targetId); section.append(row);
                    const evidence = document.createElement('details'); evidence.className = 'graph-reference-evidence';
                    const summary = document.createElement('summary'); summary.textContent = '查看引用依据'; evidence.append(summary);
                    for (const fact of edge.evidence || []) { const p = document.createElement('p'); p.textContent = `${fact.path.split('/').pop()}:${fact.line} · ${fact.confidence} · ${fact.basis}`; evidence.append(p); }
                    if (!edge.evidence?.length) { const p = document.createElement('p'); p.textContent = '旧索引缺少依据，请刷新索引。'; evidence.append(p); }
                    if (edge.evidenceTruncated) { const p = document.createElement('p'); p.textContent = '仅显示前8处依据，更多位置请核对源文件。'; evidence.append(p); }
                    section.append(evidence);
                }
                inspector.append(section);
            }
        } catch (error) { if (request === detailRequest && ctx.page === 'graph') { inspector.textContent = '读取详情失败：' + error.message; toast(error.message); } }
    }
    function renderGraphContext() { contextStrip.hidden = ctx.graphContextNodes.size === 0; contextCount.textContent = `${ctx.graphContextNodes.size} 个图谱节点已加入对话资料`; }
    async function loadProjectGraph() {
        try {
            const status = await refreshGraphStatus(); if (ctx.page !== 'graph') return;
            if (status.indexed && (!view || loadedAt !== status.lastSuccessfulIndex)) await loadOverview();
            else if (!status.indexed) { graphDetail.classList.add('empty'); graphDetail.textContent = '尚未建立索引。点击“建立 / 刷新索引”生成全量图。'; }
        } catch (error) { if (ctx.page === 'graph') toast('读取图谱失败：' + error.message); }
    }
    for (const button of [$('buildProjectGraph'), $('settingsBuildGraph')]) if (button) button.onclick = async () => { try { observeGraphStatus(await api('project-graph/index', {})); } catch (e) { toast(e.message); } };
    $('graphKind').onchange = renderGraphDirectory;
    document.addEventListener('karolina:navigate', event => { if (event.detail.page !== 'graph') { ctx.graph.request++; detailRequest++; loading = null; } });
    window.addEventListener('pagehide', () => { view?.destroy(); if (ctx.graph.polling) clearInterval(ctx.graph.polling); });
    renderGraphContext();
}
