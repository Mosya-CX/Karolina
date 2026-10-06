import { effortLabel, selectDefaultModel } from '../core/model-policy.js';
/** providers: business handlers with explicit dependencies. */
export function registerProviders({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { updateControls, connectProvider, autoConnect, efforts, refreshState, poll, unityDrawer });
    function updateControls() {
        $ ('send').disabled = !ctx.snapshot.codex?.connected || !ctx.snapshot.models?.length || !($ ('model').value) || ctx.snapshot.busy || !!ctx.submittingChat;
        $ ('send').title = ctx.snapshot.models?.length && !$ ('model').value ? '无法可靠识别低成本模型，请手动选择后发送。' : '';
        $ ('stop').hidden = !ctx.snapshot.busy || !!ctx.snapshot.toolBusy;
        ['model', 'effort', 'access', 'newChat'].forEach(id => $ (id).disabled = !!ctx.snapshot.busy || !!ctx.submittingChat || (['model', 'effort'].includes(id) && !ctx.snapshot.models?.length));
        actions.updateReviewControls();
        document.querySelectorAll('[data-workflow-mode]').forEach(b => b.disabled = !!ctx.snapshot.busy || !!ctx.submittingChat);
        actions.updateToolControls();
        $ ('toolStop').disabled = !ctx.toolRunning;
        $ ('registerTool').disabled = !!ctx.snapshot.busy || ctx.toolRunning;
        $ ('connectCodex').disabled = !!ctx.connectionRequests.codex;
        actions.refreshChoices?.();
    }
    function connectProvider(name, manual = false) {
        if (ctx.connectionRequests[name]) return ctx.connectionRequests[name];
        ctx.connectionAttempts[name] = Date.now();
        ctx.connectionErrors[name] = '';
        ctx.connectionRequests[name] = (async () => {
            try {
                await api(name === 'codex' ? 'codex/connect': 'unity', name === 'codex' ? { }: { action: 'connect' });
                if (manual) toast('已连接 Codex，模型和账号已刷新');
            } catch (e) {
                ctx.connectionErrors[name] = e.message;
                if (manual) toast(e.message);
            } finally {
                ctx.connectionRequests[name] = null;
                await actions.refreshState().catch (error => {
                    $ ('taskStatus').textContent = '连接状态读取失败';
                    $ ('taskStatus').title = error.message;
                });
            }
        })();
        actions.refreshState().catch (error => {
            $ ('taskStatus').textContent = '连接状态读取失败';
            $ ('taskStatus').title = error.message;
        });
        return ctx.connectionRequests[name];
    }
    function autoConnect() {
        if (ctx.snapshot.busy) return;
        for (const name of['codex', 'unity']) {
            const ready = name === 'codex' ? ctx.snapshot.codex?.connected && ctx.snapshot.models?.length: ctx.snapshot.unity?.state === '已连接';
            if (!ready && !ctx.connectionRequests[name] && Date.now() - ctx.connectionAttempts[name] > 15000) actions.connectProvider(name);
        }
    }
    function efforts() {
        const m = ctx.snapshot.models?.find(m => m.model === $ ('model').value);
        $ ('effort').replaceChildren();
        for (const e of m?.supportedReasoningEfforts || []) {
            const o = new Option(effortLabel(e.reasoningEffort), e.reasoningEffort);
            o.title = e.description;
            $ ('effort').add(o);
        }
        if (m) $ ('effort').value = m.defaultReasoningEffort;
        updateControls();
    }
    async function refreshState() {
        ctx.snapshot = await api('state');
        actions.observeGraphStatus?.(ctx.snapshot.graph);
        ui.setText('project', ctx.snapshot.project);
        ui.setText('projectPath', ctx.snapshot.root);
        $ ('projectPath').title = ctx.snapshot.root;
        ui.connectionStatus('codexStatus', `Codex ${ctx.connectionRequests.codex?'连接中…':ctx.snapshot.codex.connected?'已连接':ctx.connectionErrors.codex?'连接失败':'未连接'}`, ctx.snapshot.codex.connected);
        ui.connectionStatus('unityStatus', `Unity ${ctx.connectionRequests.unity?'连接中…':ctx.snapshot.unity.state}${ctx.snapshot.unity.busy?' · 操作中':ctx.snapshot.unity.lastResult?' · '+ctx.snapshot.unity.lastResult:''}`, ctx.snapshot.unity.state === '已连接');
        const acc = ctx.snapshot.codex.account?.account;
        $ ('accountStatus').textContent = ctx.snapshot.codex.connected ? (acc ? `${acc.type==='chatgpt'?'ChatGPT':'API'} · ${acc.planType||'已登录'}`: '未登录'): (ctx.snapshot.codex.error ? '连接已断开': '');
        $ ('taskStatus').textContent = ctx.snapshot.toolBusy ? '拓展工具正在执行…': ctx.snapshot.busy ? (ctx.snapshot.currentRun?.progressMessage || (ctx.snapshot.activeTurn ? 'Codex 正在执行…': '等待轮次回执…')): '就绪';
        $ ('taskStatus').title = ctx.snapshot.currentRun ? `${ctx.snapshot.currentRun.progressStage || ''} · ${ctx.snapshot.currentRun.progressUpdated || ''}`: '当前没有活动任务';
        actions.syncChatProgress?.();
        $ ('codexStatus').title = ctx.connectionErrors.codex || ctx.snapshot.codex.error || '';
        $ ('unityStatus').title = ctx.connectionErrors.unity || ctx.snapshot.unity.error || '';
        ui.setText('connectCodex', ctx.snapshot.codex.connected ? 'Codex · 刷新模型与账号': 'Codex · 连接');
        ui.setText('connectUnity', 'Unity · ' + ctx.snapshot.unity.state);
        const signature = JSON.stringify(ctx.snapshot.models);
        if (signature !== ctx.modelSignature) {
            const prior = $ ('model').value;
            ctx.modelSignature = signature;
            $ ('model').replaceChildren(new Option(ctx.snapshot.models.length ? '请手动选择低成本模型' : '连接后获取模型', ''));
            for (const m of ctx.snapshot.models) $ ('model').add(new Option(m.displayName, m.model));
            if (ctx.snapshot.models.some(m => m.model === prior)) $ ('model').value = prior;
            else $ ('model').value = selectDefaultModel(ctx.snapshot.models, ctx.settings?.modelProfiles?.[ctx.workflowMode]?.model);
            actions.efforts();
            actions.renderSettings?.();
            if (!ctx.snapshot.models.some(m => m.model === prior)) actions.applyModelDefaults?.(ctx.workflowMode, true);
        }
        actions.updateControls();
        const chatSignature = JSON.stringify(ctx.snapshot.chats);
        if (ctx.page === 'chat' && chatSignature !== ctx.chatSignature) actions.renderDirectory();
        ctx.chatSignature = chatSignature;
    }
    async function poll() {
        if (ctx.updating) return;
        ctx.updating = true;
        try {
            const data = await api('events?after=' + ctx.eventCursor);
            if (data.truncated) toast('实时记录已超出缓存；可从对话历史重新载入完整结果。');
            data.events.forEach(e => actions.receive(e.value));
            ctx.eventCursor = data.cursor;
            await actions.refreshState();
            actions.syncEditorDirty();
            actions.autoConnect();
            if (ctx.page === 'review' && Date.now() - ctx.lastReviewSync > 5000) actions.refreshReviews().catch (e => toast(e.message));
        } catch (e) {
            $ ('taskStatus').textContent = '服务连接失败';
        } finally {
            ctx.updating = false;
        }
    }
    function unityDrawer() {
        drawer('Unity 连接与验证', `<p>状态：${esc(ctx.snapshot.unity?.state)} · 当前工程 ${esc(ctx.snapshot.project)}</p><p>操作调用当前工程的 Unity MCP，结果保留真实回执。不会自动清空 Console。</p><button data-unity="connect">连接并核对工程</button><button data-unity="state">Editor 状态</button><button data-unity="console">Console 错误</button><button data-unity="compile">刷新脚本</button><label>测试程序集<input id="unityAssembly" value="FrameSyncMoba.FrameSync.Tests"></label><label>测试类<input id="unityClass" value="CommandTargetTickResolverAdaptiveTests"></label><label>测试模式<select id="unityMode"><option>EditMode</option><option>PlayMode</option></select></label><button data-unity="tests">运行聚焦测试</button><pre id="unityResult">${esc(ctx.snapshot.unity?.error||'尚未执行操作')}</pre>`);
        document.querySelectorAll('[data-unity]').forEach(b => b.onclick = async () => {
            b.disabled = true;
            try {
                const r = await api('unity', { action: b.dataset.unity, assembly: $ ('unityAssembly').value, class: $ ('unityClass').value, mode: $ ('unityMode').value });
                $ ('unityResult').textContent = JSON.stringify(r, null, 2);
                await actions.refreshState();
            } catch (e) {
                $ ('unityResult').textContent = e.message;
            } finally {
                b.disabled = false;
            }
        });
    }
    $ ('unityStatus').onclick = (... args) => actions.unityDrawer(... args);
    action('connectCodex', () => actions.connectProvider('codex', true));
    action('diagnosticsButton', async () => {
        const d = await api('diagnostics');
        drawer('运行详情', `<p>它用来查明 Codex 何时开始、是否完成、为什么失败、用了哪些权限，以及 Unity 的真实回执。普通讨论不需要手动管理它。</p><p>本地记录：<code>${esc(d.location)}</code></p><pre>${esc(JSON.stringify(d.run,null,2))}</pre>`);
    });
}
