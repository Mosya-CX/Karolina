/** navigation: business handlers with explicit dependencies. */
export function registerNavigation({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { renderDirectory, navigate, syncEditorDirty });
    function renderDirectory() {
        const query = $ ('search').value.toLowerCase();
        $ ('directory').replaceChildren();
        if (ctx.page === 'chat') {
            for (const c of ctx.snapshot.chats || []) if (c.title.toLowerCase().includes(query)) {
                const b = document.createElement('button');
                b.textContent = c.title;
                b.classList.toggle('active', c.id === ctx.threadId);
                b.onclick = () => actions.loadChat(c.id);
                $ ('directory').append(b);
            }
            return;
        }
        if (ctx.page === 'review') {
            actions.renderReviewDirectory(query);
            return;
        }
        if (ctx.page === 'rule') {
            actions.renderRuleDirectory(query);
            return;
        }
        if (ctx.page === 'tools') {
            actions.renderToolDirectory(query);
            return;
        }
        if (ctx.page === 'graph') {
            actions.renderGraphDirectory(query);
            return;
        }
        if (ctx.page === 'settings') {
            $ ('directory').replaceChildren();
            return;
        }
        let group = '';
        for (const d of ctx.docs.filter(d => (d.type === ctx.page || ctx.page === 'rule' && ['engineering', 'resource', 'guide'].includes(d.type)) && d.title.toLowerCase().includes(query))) {
            if (ctx.page !== 'plan' && d.domain !== group) {
                group = d.domain;
                const h = document.createElement('div');
                h.className = 'group';
                h.textContent = group;
                $ ('directory').append(h);
            }
            const b = document.createElement('button');
            b.innerHTML = `<span class="title">${esc(d.title)}</span>${d.status?`<span class="status">${esc(d.status)}</span>`:''}`;
            b.classList.toggle('active', ctx.currentDoc?.document.id === d.id);
            b.onclick = () => actions.loadDocument(d.id);
            $ ('directory').append(b);
        }
    }
    function navigate(next, { loadPage = true } = {}) {
        if (ctx.reviewSaving) {
            toast('正在保存审批，请稍候');
            return false;
        }
        if (next !== ctx.page && ctx.currentDoc && ctx.docDirty) {
            if (!confirm('正文尚未保存，放弃当前编辑？')) return false;
            $ ('documentSource').value = ctx.currentDoc.markdown;
            ctx.docDirty = false;
        }
        // 切换页面保留审批草稿及脏标志；换任务时才明确确认放弃并重新载入。
        ctx.page = next;
        document.body.dataset.page = next;
        document.dispatchEvent(new CustomEvent('karolina:navigate', { detail: { page: next } }));
        if (matchMedia('(max-width: 740px)').matches) {
            document.body.dataset.sidebar = 'closed';
            $ ('sidebarToggle').setAttribute('aria-expanded', 'false');
        }
        const names = { chat: 'AI 对话', requirement: '需求案', plan: '计划案', rule: '规则案', review: '任务审批', tools: '拓展工具', graph: '工程图谱', settings: '统一设置' };
        $ ('sectionTitle').textContent = names[next];
        $ ('search').placeholder = next === 'graph' ? '搜索代码、类型或资源路径…': '搜索标题…';
        $ ('sidebarNote').textContent = next === 'review' ? '本次任务开始前为基线；驳回意见返回执行，不自动回滚。': next === 'graph' ? '图谱显示静态代码声明与 Unity GUID 引用；不证明运行时行为。': next === 'settings' ? '默认值按当前工程保存；对话仍可临时覆盖。': '参考资料可不选，也可多选。';
        $ ('pageEyebrow').textContent = names[next];
        $ ('pageTitle').textContent = next === 'review' && ctx.currentReview ? ctx.currentReview.title: next === 'tools' && ctx.selectedTool ? ctx.selectedTool.definition.title: ['requirement', 'plan', 'rule'].includes(next) && ctx.currentDoc ? ctx.currentDoc.document.title: names[next];
        $ ('chatActions').hidden = next !== 'chat';
        $ ('newChat').hidden = next !== 'chat';
        $ ('newDocument').hidden = !['requirement', 'plan', 'rule'].includes(next);
        $ ('chatPage').hidden = next !== 'chat';
        $ ('documentPage').hidden = !['requirement', 'plan', 'rule'].includes(next);
        $ ('reviewPage').hidden = next !== 'review';
        $ ('toolsPage').hidden = next !== 'tools';
        $ ('projectGraphPage').hidden = next !== 'graph';
        $ ('settingsPage').hidden = next !== 'settings';
        actions.rememberWorkspace();
        document.querySelectorAll('.rail [data-page]').forEach(b => b.classList.toggle('active', b.dataset.page === next));
        $ ('search').value = '';
        actions.renderDirectory();
        if (loadPage && next === 'tools') actions.refreshTools().catch (e => toast(e.message));
        if (loadPage && next === 'review') actions.refreshReviews().then(() => {
            if (ctx.page === 'review' && !ctx.currentReview && ctx.reviewTasks.length) return actions.openReview(ctx.reviewTasks[0].id);
        }).catch (e => toast(e.message));
        if (loadPage && next === 'graph') actions.loadProjectGraph().catch(e => toast(e.message));
        if (loadPage && next === 'settings') actions.renderSettings();
        return true;
    }
    function syncEditorDirty() {
        if (ctx.editorSyncing || ctx.lastEditorDirty === (ctx.docDirty || ctx.reviewSummaryDirty || ctx.reviewNoteDirty || ctx.reviewFeedbackDirty)) return;
        ctx.editorSyncing = (async () => {
            while (ctx.lastEditorDirty !== (ctx.docDirty || ctx.reviewSummaryDirty || ctx.reviewNoteDirty || ctx.reviewFeedbackDirty)) {
                const dirty = ctx.docDirty || ctx.reviewSummaryDirty || ctx.reviewNoteDirty || ctx.reviewFeedbackDirty;
                await api('window/editor', { dirty });
                ctx.lastEditorDirty = dirty;
                ctx.editorSyncError = null;
            }
        })().catch (error => {
            ctx.lastEditorDirty = null;
            if (ctx.editorSyncError !== error.message) toast('编辑状态同步失败：' + error.message);
            ctx.editorSyncError = error.message;
        }).finally(() => {
            ctx.editorSyncing = null;
        });
    }
    document.querySelectorAll('.rail [data-page]').forEach(b => b.onclick = () => {
        if (actions.navigate(b.dataset.page)) {
            ++ ctx.documentRequest;
            ++ ctx.diffRequest;
            ++ ctx.reviewRequest;
            ++ ctx.toolRequest;
        }
    });
    $ ('search').oninput = (... args) => actions.renderDirectory(... args);
    $ ('referenceSearch').oninput = (... args) => actions.renderReferences(... args);
    $ ('clearReferences').onclick = () => {
        ctx.selectedDocs.clear();
        actions.renderReferences();
    };
    $ ('model').onchange = (... args) => actions.efforts(... args);
    $ ('closeDrawer').onclick = () => $ ('drawer').close();
    $ ('connectUnity').onclick = (... args) => actions.unityDrawer(... args);
    $ ('codexStatus').onclick = () => drawer('Codex 连接', `<p>状态：${ctx.snapshot.codex?.connected?'已连接':'未连接'}</p><p>程序：${esc(ctx.snapshot.codex?.executable)}</p><p>账号：${esc($('accountStatus').textContent)}</p><p>未登录时请在终端运行 <code>codex login</code>，然后点击顶部刷新模型与账号。</p><pre>${esc(ctx.snapshot.codex?.error||'')}</pre>`);
    $ ('sidebarToggle').onclick = () => {
        const open = document.body.dataset.sidebar !== 'open';
        document.body.dataset.sidebar = open ? 'open': 'closed';
        $ ('sidebarToggle').setAttribute('aria-expanded', String(open));
    };
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && !$ ('drawer').open) {
            document.body.dataset.sidebar = 'closed';
            $ ('sidebarToggle').setAttribute('aria-expanded', 'false');
        }
    });
}
