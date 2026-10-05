/** ProjectGraph: a read-only browser for the backend's project-scoped CodeGraph/ResourceGraph snapshot. */
export function registerProjectGraph({ state: ctx, actions, ui, api }) {
    const { $, esc, toast } = ui;
    ctx.graph = { nodes: [], status: null, selectedId: null, request: 0, polling: null, kinds: new Set() };
    ctx.graphContextNodes = new Map();
    const graphDetail = $ ('graphDetail');
    const contextStrip = document.createElement('div');
    contextStrip.id = 'graphContextState';
    contextStrip.className = 'graph-context-state';
    contextStrip.hidden = true;
    contextStrip.innerHTML = '<span id="graphContextCount"></span><button id="clearGraphContext" class="quiet">清除图谱资料</button>';
    $ ('references').after(contextStrip);

    Object.assign(actions, { renderGraphDirectory, loadProjectGraph, refreshGraphStatus, openGraphNode, renderGraphContext, selectGraphNode });

    async function refreshGraphStatus() {
        const status = await api('project-graph/status');
        ctx.graph.status = status;
        paintStatus(status);
        document.dispatchEvent(new CustomEvent('karolina:graph-state', { detail: status }));
        return status;
    }
    function paintStatus(status) {
        const label = status.indexing ? `${status.stage} · 已扫描 ${status.filesScanned} 个文件`: status.error ? `${status.stage} · ${status.error}`: status.indexed ? `${status.stage} · ${status.nodes} 个节点 / ${status.edges} 条关系 · 跳过 ${status.skippedFiles} · 未解析 GUID ${status.unresolvedGuids} · ${new Date(status.lastSuccessfulIndex).toLocaleString()}`: status.stage;
        $ ('graphStatus').textContent = label;
        $ ('graphStatus').title = status.root + (status.error ? '\n' + status.error: '');
        const settingsStatus = $ ('settingsGraphStatus');
        if (settingsStatus) settingsStatus.textContent = label;
        for (const button of [$ ('buildProjectGraph'), $ ('settingsBuildGraph')]) if (button) button.disabled = status.indexing;
        if (status.indexing) startPolling(); else stopPolling();
    }
    function startPolling() {
        if (ctx.graph.polling) return;
        ctx.graph.polling = setInterval(() => {
            refreshGraphStatus().then(status => {
                if (status.indexed && !status.indexing && ctx.page === 'graph') return loadNodes();
            }).catch(error => toast('读取图谱状态失败：' + error.message));
        }, 900);
    }
    function stopPolling() {
        if (!ctx.graph.polling) return;
        clearInterval(ctx.graph.polling);
        ctx.graph.polling = null;
    }
    async function loadNodes() {
        const requestId = ++ctx.graph.request;
        const query = $ ('search').value.trim();
        const kind = $ ('graphKind').value;
        const data = await api(`project-graph/nodes?query=${encodeURIComponent(query)}&kind=${encodeURIComponent(kind)}&take=250`);
        if (requestId !== ctx.graph.request) return;
        ctx.graph.status = data.status;
        ctx.graph.nodes = data.nodes || [];
        paintStatus(data.status);
        renderGraphDirectoryFromCache(data.truncated);
        if (ctx.graph.selectedId && ctx.graph.nodes.some(node => node.id === ctx.graph.selectedId)) return;
        ctx.graph.selectedId = null;
        graphDetail.classList.add('empty');
        graphDetail.textContent = data.nodes.length ? '从左侧选择一个节点查看关联关系。': data.status.indexed ? '没有符合搜索条件的节点。': '先建立工程图谱索引。';
    }
    function renderGraphDirectory() {
        if (ctx.page !== 'graph') return;
        loadNodes().catch(error => {
            if (error.message.includes('尚未建立')) {
                graphDetail.classList.add('empty');
                graphDetail.textContent = '尚未建立索引。点击“建立 / 刷新索引”。';
                refreshGraphStatus().catch(failure => toast(failure.message));
            } else toast('读取图谱失败：' + error.message);
        });
    }
    function renderGraphDirectoryFromCache(truncated = false) {
        const directory = $ ('directory');
        directory.replaceChildren();
        let kind = '';
        for (const node of ctx.graph.nodes) {
            if (node.kind !== kind) {
                kind = node.kind;
                const heading = document.createElement('div');
                heading.className = 'group';
                heading.textContent = kind;
                directory.append(heading);
            }
            const button = document.createElement('button');
            button.className = 'graph-node-entry';
            button.classList.toggle('active', node.id === ctx.graph.selectedId);
            button.innerHTML = `<span class="title">${esc(node.title)}</span><small>${esc(node.path)}</small>`;
            button.onclick = () => openGraphNode(node.id);
            directory.append(button);
        }
        if (!ctx.graph.nodes.length) directory.innerHTML = '<div class="empty">没有符合条件的节点</div>';
        if (truncated) {
            const note = document.createElement('small');
            note.className = 'graph-truncated';
            note.textContent = '只显示前 250 项，请缩小搜索范围。';
            directory.append(note);
        }
    }
    async function openGraphNode(id) {
        try {
            const detail = await api('project-graph/node?id=' + encodeURIComponent(id));
            ctx.graph.selectedId = detail.node.id;
            ctx.graph.status = detail.status;
            renderGraphDirectoryFromCache();
            paintDetail(detail);
        } catch (error) { toast('打开图谱节点失败：' + error.message); }
    }
    function paintDetail(detail) {
        const node = detail.node;
        graphDetail.classList.remove('empty');
        const heading = document.createElement('div');
        heading.className = 'graph-detail-heading';
        const title = document.createElement('div');
        title.innerHTML = `<span class="badge">${esc(node.kind)}</span><h2>${esc(node.title)}</h2><code>${esc(node.path)}</code>${node.symbol ? `<p>${esc(node.symbol)}</p>`: ''}`;
        const add = document.createElement('button');
        add.className = 'primary';
        add.textContent = ctx.graphContextNodes.has(node.id) ? '已加入对话资料' : '加入对话资料';
        add.disabled = ctx.graphContextNodes.has(node.id);
        add.onclick = () => {
            ctx.graphContextNodes.set(node.id, node);
            renderGraphContext();
            toast('已加入图谱资料；Agent 会收到此节点及其直接关系');
        };
        heading.append(title, add);
        const relationList = (edges, direction) => {
            const section = document.createElement('section');
            section.className = 'graph-relations';
            const h = document.createElement('h3');
            h.textContent = direction === 'out' ? `此节点引用 / 声明（${edges.length}）` : `被这些节点引用（${edges.length}）`;
            section.append(h);
            if (!edges.length) {
                const none = document.createElement('p'); none.textContent = '没有已确认关系'; section.append(none); return section;
            }
            for (const edge of edges) {
                const id = direction === 'out' ? edge.to : edge.from;
                const related = detail.relatedNodes.find(candidate => candidate.id === id);
                if (!related) continue;
                const row = document.createElement('button');
                row.className = 'graph-relation';
                row.innerHTML = `<span class="badge">${esc(edge.relation)}</span><span><strong>${esc(related.title)}</strong><small>${esc(related.path)}</small></span>`;
                row.onclick = () => openGraphNode(related.id);
                section.append(row);
            }
            return section;
        };
        const relations = document.createElement('div');
        relations.className = 'graph-relations-grid';
        relations.append(relationList(detail.outgoing, 'out'), relationList(detail.incoming, 'in'));
        graphDetail.replaceChildren(heading, relations);
    }
    function selectGraphNode(id) { return openGraphNode(id); }
    function renderGraphContext() {
        const strip = $ ('graphContextState');
        const count = $ ('graphContextCount');
        if (!strip || !count) return;
        strip.hidden = ctx.graphContextNodes.size === 0;
        count.textContent = `${ctx.graphContextNodes.size} 个工程图谱节点已加入对话资料`;
        $ ('clearGraphContext').onclick = () => { ctx.graphContextNodes.clear(); renderGraphContext(); };
    }
    async function loadProjectGraph() {
        try {
            const status = await refreshGraphStatus();
            if (status.indexed) await loadNodes();
            else {
                graphDetail.classList.add('empty');
                graphDetail.textContent = '尚未建立索引。点击“建立 / 刷新索引”。';
            }
        } catch (error) { toast('读取工程图谱失败：' + error.message); }
    }
    async function startBuild() {
        try { paintStatus(await api('project-graph/index', {})); }
        catch (error) { toast('启动图谱索引失败：' + error.message); }
    }
    for (const id of ['buildProjectGraph', 'settingsBuildGraph']) $(id).onclick = startBuild;
    $ ('graphKind').onchange = () => loadNodes().catch(error => toast(error.message));
    window.addEventListener('pagehide', stopPolling, { once: true });
    renderGraphContext();
}
