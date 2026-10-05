/** workspace: business handlers with explicit dependencies. */
export function registerWorkspace({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { renderRuleDirectory, setWorkflowMode, rememberWorkspace, restoreWorkspace, finishWorkspaceRestore });
    let restoreCancelled = false;
    // User interaction takes priority over saved navigation, including typed drafts.
    const interactionEvents = ['pointerdown', 'click', 'keydown', 'input'];
    function cancelWorkspaceRestore() {
        if (!ctx.restoringWorkspace) return;
        restoreCancelled = true;
        ++ctx.documentRequest;
        ++ctx.reviewRequest;
        // A selected tool only awaits its manual; keep that read when editing its arguments.
        // Leaving the page or selecting another tool invalidates it in navigation/openTool.
        if (ctx.page !== 'tools' || !ctx.selectedTool) ++ctx.toolRequest;
        ++ctx.diffRequest;
        finishWorkspaceRestore(false);
        // Capture runs before the control handler; persist the resulting user choice.
        queueMicrotask(rememberWorkspace);
    }
    function finishWorkspaceRestore(save = true) {
        ctx.restoringWorkspace = false;
        for (const event of interactionEvents) document.removeEventListener(event, cancelWorkspaceRestore, true);
        if (save) rememberWorkspace();
    }
    for (const event of interactionEvents) document.addEventListener(event, cancelWorkspaceRestore, true);
    window.addEventListener('pagehide', () => finishWorkspaceRestore(false), { once: true });
    function renderRuleDirectory(query) {
        for (const[section, title] of ctx.ruleSections) {
            const header = document.createElement('div');
            header.className = 'group';
            header.textContent = title;
            $ ('directory').append(header);
            for (const d of ctx.docs.filter(d => d.type === 'rule' && d.section === section && d.title.toLowerCase().includes(query))) {
                const b = document.createElement('button');
                b.textContent = d.title;
                b.classList.toggle('active', ctx.currentDoc?.document.id === d.id);
                b.onclick = () => actions.loadDocument(d.id);
                $ ('directory').append(b);
            }
        }
    }
    function setWorkflowMode(mode, restoring = false) {
        if (!['discuss-requirement', 'formulate-plan', 'execute-task'].includes(mode)) return;
        if (ctx.snapshot.busy && !restoring) return;
        ctx.workflowMode = mode;
        document.body.dataset.workflowMode = mode;
        document.dispatchEvent(new CustomEvent('karolina:workflow-mode', { detail: mode }));
        if (mode !== 'execute-task') ctx.taskForChat = null;
        document.querySelectorAll('[data-workflow-mode]').forEach(b => b.classList.toggle('selected', b.dataset.workflowMode === mode));
        $ ('executionPlanRow').hidden = mode !== 'execute-task';
        $ ('access').querySelector('[value=workspace-write]').textContent = mode === 'execute-task' ? '工程写入': '文档写入';
        const full = $ ('access').querySelector('[value=danger-full-access]');
        full.disabled = mode !== 'execute-task';
        if (full.disabled && $ ('access').value === 'danger-full-access') $ ('access').value = 'read-only';
        if (!restoring) actions.applyModelDefaults?.(mode, true);
        $ ('workflowHint').textContent = mode === 'discuss-requirement' ? '讨论目标与方案，输出或维护需求案；写入范围为 Docs。': mode === 'formulate-plan' ? '引用需求制定计划与验收流程；不会开始实施。': '选择具体计划，执行任务后审查本次 Unity 文件变化。';
        actions.updateReviewControls();
        actions.rememberWorkspace();
    }
    document.querySelectorAll('[data-workflow-mode]').forEach(b => b.onclick = () => actions.setWorkflowMode(b.dataset.workflowMode));
    function rememberWorkspace() {
        if (ctx.restoringWorkspace) return;
        ctx.nextWorkspacePreferences = {
            mode: ctx.workflowMode,
        };
        if (ctx.preferencesSaving) return;
        ctx.preferencesSaving = (async () => {
            while (ctx.nextWorkspacePreferences) {
                const value = ctx.nextWorkspacePreferences;
                ctx.nextWorkspacePreferences = null;
                await api('workspace/preferences', value);
            }
        })().catch (e => toast('工作模式未保存：' + e.message)).finally(() => {
            ctx.preferencesSaving = null;
        });
    }
    async function restoreWorkspace() {
        if (restoreCancelled) return;
        const saved = await api('workspace/preferences');
        if (restoreCancelled) return;
        try {
            setWorkflowMode(saved.mode || 'discuss-requirement', true);
        } finally {
            finishWorkspaceRestore();
        }
    }
}
