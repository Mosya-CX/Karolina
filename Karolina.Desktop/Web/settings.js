import { effortLabel, selectDefaultEffort, selectDefaultModel } from './core/model-policy.js';

/** Settings: project-scoped defaults and a single navigation entry for settings panels. */
export function registerSettings({ state: ctx, actions, ui, api }) {
    const { $, esc, toast } = ui;
    ctx.settings = { schemaVersion: 1, modelProfiles: {}, autoVerification: true, maxVerificationRepairRounds: 1 };
    ctx.settingsDirty = false;
    ctx.settingsLoadError = false;
    let modelSignature = '';

    Object.assign(actions, { loadSettings, renderSettings, applyModelDefaults, selectSettingsTab });

    async function loadSettings() {
        try {
            ctx.settings = await api('settings');
            ctx.settingsLoadError = false;
            ctx.settingsDirty = false;
            $('resetWorkbenchSettings').hidden = true;
            renderSettings();
            applyModelDefaults(ctx.workflowMode, true);
        } catch (error) {
            ctx.settingsLoadError = true;
            $('settingsStatus').textContent = '设置读取失败，原文件保留：' + error.message;
            $('resetWorkbenchSettings').hidden = false;
            setSettingsInputsDisabled(true);
            $('resetWorkbenchSettings').onclick = resetSettings;
            toast('设置读取失败：' + error.message);
        }
    }
    function profileFor(mode) { return ctx.settings.modelProfiles?.[mode] || { model: '', effort: '' }; }
    function renderSettings(force = false) {
        if (ctx.settingsLoadError) { setSettingsInputsDisabled(true); return; }
        setSettingsInputsDisabled(false);
        $('resetWorkbenchSettings').hidden = true;
        if (ctx.settingsDirty && !force) return;
        const signature = JSON.stringify(ctx.snapshot.models || []);
        if (signature !== modelSignature || force) {
            modelSignature = signature;
            const root = $('settingsModelProfiles');
            root.replaceChildren();
            const names = { 'discuss-requirement': '讨论需求', 'formulate-plan': '制定计划', 'execute-task': '执行任务' };
            for (const mode of ['discuss-requirement', 'formulate-plan', 'execute-task']) {
                const profile = profileFor(mode);
                const row = document.createElement('div');
                row.className = 'settings-model-row';
                const modelLabel = document.createElement('label');
                modelLabel.textContent = names[mode] + ' · 默认模型';
                const modelSelect = document.createElement('select');
                modelSelect.id = 'settingsModel-' + mode;
                modelSelect.dataset.mode = mode;
                modelSelect.add(new Option(ctx.snapshot.models?.length ? '未识别低成本模型 · 请手动选择' : '连接 Codex 后选择', ''));
                for (const model of ctx.snapshot.models || []) modelSelect.add(new Option(model.displayName || model.model, model.model));
                if (!(ctx.snapshot.models || []).some(model => model.model === profile.model) && profile.model) modelSelect.add(new Option(profile.model + ' · 当前不可用', profile.model));
                const effortLabelElement = document.createElement('label');
                effortLabelElement.textContent = '思考挡位';
                const effortSelect = document.createElement('select');
                effortSelect.id = 'settingsEffort-' + mode;
                effortSelect.dataset.mode = mode;
                const applyEfforts = (preferred = '') => {
                    const model = (ctx.snapshot.models || []).find(item => item.model === modelSelect.value);
                    effortSelect.replaceChildren();
                    const efforts = model?.supportedReasoningEfforts || [];
                    for (const effort of efforts) {
                        const option = new Option(effortLabel(effort.reasoningEffort), effort.reasoningEffort);
                        option.title = effort.description || '';
                        effortSelect.add(option);
                    }
                    if (!effortSelect.options.length) effortSelect.add(new Option('选择模型后显示可用挡位', ''));
                    const fallback = selectDefaultEffort(model, preferred);
                    effortSelect.value = efforts.some(e => e.reasoningEffort === preferred) ? preferred : fallback;
                    effortSelect.disabled = efforts.length === 0;
                };
                modelSelect.value = selectDefaultModel(ctx.snapshot.models || [], profile.model);
                if (!modelSelect.value && profile.model) modelSelect.value = profile.model;
                modelLabel.append(modelSelect);
                effortLabelElement.append(effortSelect);
                row.append(modelLabel, effortLabelElement);
                root.append(row);
                modelSelect.disabled = !(ctx.snapshot.models || []).length;
                modelSelect.addEventListener('change', () => { applyEfforts(profile.effort); markDirty(); });
                effortSelect.addEventListener('change', markDirty);
                applyEfforts(profile.effort);
            }
        }
        $('settingsAutoVerification').checked = !!ctx.settings.autoVerification;
        $('settingsRepairRounds').value = String(Math.max(0, Math.min(3, ctx.settings.maxVerificationRepairRounds ?? 1)));
        $('settingsAutoVerification').onchange = markDirty;
        $('settingsRepairRounds').onchange = markDirty;
        $('saveWorkbenchSettings').onclick = saveSettings;
        const modes = ['discuss-requirement', 'formulate-plan', 'execute-task'];
        const modelNeedsChoice = (ctx.snapshot.models || []).length && modes.some(mode => !profileFor(mode).model && !selectDefaultModel(ctx.snapshot.models || []));
        const unavailableModes = modes.filter(mode => {
            const model = profileFor(mode).model;
            return model && !(ctx.snapshot.models || []).some(item => item.model === model);
        });
        $('settingsStatus').textContent = !(ctx.snapshot.models || []).length
            ? '尚未连接 Codex；可先配置验证策略，模型预设待连接后选择。'
            : unavailableModes.length ? '以下模式保存的模型当前不可用：' + unavailableModes.map(mode => ({ 'discuss-requirement': '讨论需求', 'formulate-plan': '制定计划', 'execute-task': '执行任务' }[mode])).join('、') + '。请重新选择模型并保存。'
            : modelNeedsChoice ? '模型目录没有可确认的低成本候选；尚未配置的模式保持未选，需手动指定默认模型。' : '';
    }
    function markDirty() {
        ctx.settingsDirty = true;
        $('settingsStatus').textContent = '有未保存的设置';
    }
    function formSettings() {
        const profiles = {};
        for (const mode of ['discuss-requirement', 'formulate-plan', 'execute-task']) {
            profiles[mode] = {
                model: $('settingsModel-' + mode).value,
                effort: $('settingsEffort-' + mode).value
            };
        }
        return {
            schemaVersion: 1,
            modelProfiles: profiles,
            autoVerification: $('settingsAutoVerification').checked,
            maxVerificationRepairRounds: Number($('settingsRepairRounds').value)
        };
    }
    async function saveSettings() {
        if (ctx.settingsLoadError) return;
        const button = $('saveWorkbenchSettings');
        button.disabled = true;
        try {
            ctx.settings = await api('settings', formSettings());
            ctx.settingsDirty = false;
            $('settingsStatus').textContent = '已保存到当前工程；对话页将使用对应模式的默认值。';
            applyModelDefaults(ctx.workflowMode, true);
            toast('Karolina 设置已保存');
        } catch (error) { $('settingsStatus').textContent = '保存失败：' + error.message; toast(error.message); }
        finally { button.disabled = false; }
    }
    async function resetSettings() {
        const button = $('resetWorkbenchSettings');
        button.disabled = true;
        $('settingsStatus').textContent = '正在备份无法读取的设置并恢复默认值…';
        try {
            const recovery = await api('settings/reset', {});
            ctx.settings = recovery.settings;
            ctx.settingsLoadError = false;
            ctx.settingsDirty = false;
            $('resetWorkbenchSettings').hidden = true;
            modelSignature = '';
            renderSettings(true);
            applyModelDefaults(ctx.workflowMode, true);
            $('settingsStatus').textContent = recovery.backupFile
                ? `已恢复默认设置；损坏文件备份为 ${recovery.backupFile}`
                : '已恢复默认设置。';
            toast('设置已恢复，原文件已保留备份');
        } catch (error) {
            $('settingsStatus').textContent = '恢复失败，原文件仍保留：' + error.message;
            toast(error.message);
        } finally { button.disabled = false; }
    }
    function setSettingsInputsDisabled(disabled) {
        document.querySelectorAll('#settingsModelProfiles select, #settingsAutoVerification, #settingsRepairRounds, #saveWorkbenchSettings').forEach(control => control.disabled = disabled);
    }
    function applyModelDefaults(mode = ctx.workflowMode, force = false) {
        if (mode !== ctx.workflowMode && !force) return;
        const models = ctx.snapshot.models || [];
        const profile = profileFor(mode);
        const selected = selectDefaultModel(models, profile.model);
        const status = $('modelProfileStatus');
        const profileUnavailable = !!profile.model && !models.some(item => item.model === profile.model);
        if (!selected) {
            if (status) status.textContent = profileUnavailable
                ? `保存的默认模型“${profile.model}”当前不可用，也没有可确认的低成本替代项；请手动选择。`
                : models.length ? '没有可确认的低成本默认模型，请手动选择。' : '连接 Codex 后选择模型。';
            return;
        }
        const model = $('model');
        if (!model || model.disabled && !models.length) return;
        if (force || !models.some(item => item.model === model.value)) model.value = selected;
        actions.efforts();
        const currentModel = models.find(item => item.model === model.value);
        const effort = selectDefaultEffort(currentModel, profile.effort);
        if ([...$('effort').options].some(option => option.value === effort)) $('effort').value = effort;
        if (status) status.textContent = profileUnavailable
            ? `保存的默认模型“${profile.model}”已不可用；当前临时使用“${selected}”，请到统一设置页更新预设。`
            : '';
    }
    function selectSettingsTab(tab) {
        if (!['ai', 'graph', 'appearance'].includes(tab)) tab = 'ai';
        document.querySelectorAll('[data-settings-tab]').forEach(button => {
            const selected = button.dataset.settingsTab === tab;
            button.classList.toggle('selected', selected);
            button.setAttribute('aria-selected', String(selected));
        });
        document.querySelectorAll('[data-settings-panel]').forEach(panel => panel.hidden = panel.dataset.settingsPanel !== tab);
        document.dispatchEvent(new CustomEvent('karolina:settings-tab', { detail: tab }));
        if (tab === 'appearance') actions.mountAppearanceSettings?.();
        if (tab === 'graph') actions.refreshGraphStatus?.().catch(error => toast(error.message));
        if (ctx.settingsDirty && tab !== 'ai') $('settingsStatus').textContent = 'AI 设置有未保存的更改';
    }
    document.querySelectorAll('[data-settings-tab]').forEach(button => button.onclick = () => selectSettingsTab(button.dataset.settingsTab));
    document.addEventListener('karolina:graph-state', event => {
        const status = event.detail;
        const target = $('settingsGraphStatus');
        if (target) target.textContent = status.indexing ? `${status.stage} · ${status.filesScanned} 个文件`: status.error ? `${status.stage} · ${status.error}`: status.indexed ? `${status.stage} · ${status.nodes} 个节点 / ${status.edges} 条关系`: status.stage;
    });
}
