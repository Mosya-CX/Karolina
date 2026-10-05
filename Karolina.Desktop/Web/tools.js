import { renderToolOperations } from './ui/tool-operations.js';
/** tools: business handlers with explicit dependencies. */
export function registerTools({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { refreshTools, renderToolDirectory, openTool, renderToolHistory, updateToolControls });
    async function refreshTools() {
        ctx.extensionTools = await api('tools');
        if (ctx.page === 'tools') actions.renderDirectory();
    }
    function renderToolDirectory(query) {
        for (const item of ctx.extensionTools.filter(t => t.definition.title.toLowerCase().includes(query))) {
            const tool = item.definition, b = document.createElement('button');
            b.innerHTML = `<span class="title">${esc(tool.title)}</span><small>${tool.kind==='group'?`${tool.operations.length}个调用函数`:tool.kind==='manual'?'操作指南':'可调用工具'}</small>`;
            b.classList.toggle('active', ctx.selectedTool?.definition.id === tool.id);
            b.onclick = () => actions.openTool(item).catch (e => toast(e.message));
            $ ('directory').append(b);
        }
        if (!ctx.extensionTools.length) $ ('directory').innerHTML = '<div class="empty">尚未注册拓展工具。</div>';
    }
    async function openTool(item) {
        if (ctx.toolRunning) throw new Error('工具正在执行，请等待或停止');
        const request = ++ ctx.toolRequest;
        ctx.selectedTool = item;
        const tool = item.definition;
        $('toolGuideMetadata').hidden = !tool.guideId;
        $ ('toolEmpty').hidden = true;
        $ ('toolContent').hidden = false;
        $ ('pageTitle').textContent = tool.title;
        $ ('toolDescription').textContent = tool.description;
        const operations=tool.kind==='group'?tool.operations:tool.kind==='manual'?[]:[tool];
        $('toolKind').textContent=operations.length?`${operations.length}个调用函数 · 人工直接调用`:'操作指南';
        $('toolOperations').replaceChildren();
        $('toolFunctions').hidden=!operations.length;
        $('toolFunctions').open=true;
        $('toolManual').textContent='正在载入使用说明…';
        renderToolOperations($('toolOperations'),operations,{esc,invoke:(operation,args)=>invokeOperation(item,operation,args),onError:e=>toast('调用失败：'+e.message)});
        $ ('toolOutput').textContent = '尚未调用。';
        updateToolControls();
        if (tool.guideId) {
            const guide = await api('document/' + encodeURIComponent(tool.guideId));
            if (request !== ctx.toolRequest) return;
            $ ('toolManual').innerHTML = markdown(guide.markdown);
        } else $ ('toolManual').textContent = '缺少配套说明，请完善注册信息。';
        actions.renderDirectory();
        actions.rememberWorkspace();
        await actions.renderToolHistory();
    }
    async function renderToolHistory() {
        const runs = await api('tools/runs');
        $ ('toolHistoryBody').replaceChildren();
        for (const run of runs) {
            const p = document.createElement('p');
            const tool=ctx.extensionTools.find(t=>t.definition.id===run.toolId)?.definition,operation=tool?.operations?.find(o=>o.id===run.operationId);
            p.textContent = (tool?.title || run.toolId) + (operation?' / '+operation.title:'') + ' · ' + run.state + ' · ' + new Date(run.started).toLocaleString();
            $ ('toolHistoryBody').append(p);
        }
    }
    action('toolGuideMetadata', () => actions.showDocumentMetadata(ctx.selectedTool?.definition.guideId));
    action('refreshTools', actions.refreshTools);
    function updateToolControls() {
        const blocked=!!ctx.snapshot.busy || ctx.toolRunning;
        $('toolOperations').querySelectorAll('button,textarea').forEach(e=>e.disabled=blocked);
        $('toolAvailability').textContent=ctx.snapshot.toolBusy || ctx.toolRunning?'工具正在执行，等待结束后可继续调用。':ctx.snapshot.busy?'Codex任务正在执行，工程锁保留；任务结束后恢复调用。':'可立即调用；使用说明和历史加载不阻塞按钮。';
        $('toolStop').disabled=!ctx.toolRunning;
    }
    async function invokeOperation(item,operation,argumentsValue) {
        if(ctx.toolRunning || ctx.snapshot.busy)throw new Error('请等待当前操作结束');
        ctx.toolRunning = true;
        updateToolControls();
        $ ('toolOutput').textContent = '正在调用：'+operation.title+'…';
        try {
            const result = await api('tools/run', { id: item.definition.id, operationId:item.definition.kind==='group'?operation.id:null, hash: item.hash, arguments: argumentsValue });
            $ ('toolOutput').textContent = operation.title+' · '+result.state + '\n' + result.output;
            await actions.refreshReviews();
        } catch (e) {
            $ ('toolOutput').textContent = e.message;
            throw e;
        } finally {
            ctx.toolRunning = false;
            updateToolControls();
            await actions.renderToolHistory();
        }
    }
    action('toolStop', () => api('tools/stop', { }));
    action('buildFinished', async () => {
        if (confirm('确认 Unity 构建已经结束？')) {
            await api('tools/build-finished', { });
            toast('已登记构建结束，Unity操作恢复。');
        }
    });
    action('registerTool', async () => {
        drawer('注册拓展工具', `<p>一个工具可包含多个相关函数，共用使用说明。先替换示例的程序路径和参数；注册不会执行工具，调用以当前Windows用户权限运行。</p><label>注册声明（JSON）<textarea id="toolRegistration" rows="13"></textarea></label><label>配套使用说明（Markdown）<textarea id="toolRegistrationManual" rows="6" placeholder="用途、参数、步骤、预期结果、失败处理与写入范围"></textarea></label><button id="saveToolRegistration" class="primary">注册工具</button>`);
        $ ('toolRegistration').value = JSON.stringify({
            id: 'my-tool',
            title: '我的工具',
            description: '说明工具用途',
            kind: 'group',
            operations: [{id:'export',title:'导出资源',kind:'external',executable:'Tools/MyTool/MyTool.exe',arguments:['export'],readOnly:false},{id:'validate',title:'检查资源',kind:'external',executable:'Tools/MyTool/MyTool.exe',arguments:['check'],readOnly:true}],
            defaultArguments: { },
            readOnly: true,
            timeoutSeconds: 60
        }, null, 2);
        $ ('saveToolRegistration').onclick = async () => {
            const b = $ ('saveToolRegistration');
            b.disabled = true;
            try {
                const item = await api('tools/register', { definition: JSON.parse($ ('toolRegistration').value), manual: $ ('toolRegistrationManual').value });
                $ ('drawer').close();
                ctx.docs = await api('documents');
                await actions.refreshTools();
                await actions.openTool(item);
                toast('工具已注册，尚未执行。');
            } catch (e) {
                toast(e.message);
            } finally {
                b.disabled = false;
            }
        };
    });
}
