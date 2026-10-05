import { request } from '../core/api.js';
import { createThemeRenderer } from './renderer.js';
import { validateTokens } from './manifest.js';
import { settingsMarkup, previewMarkup } from './settings-view.js';
import { bindPortraitControls } from './portrait-controls.js';
const defaults = () => ({ themeId: 'karolina', mode: 'dark', motion: 'system', effects: 'auto', mascotVisible: true, backgroundOpacity: .8, tokens: { } });
export function createAppearance({ api, ui, components, effects, mascot }) {
    const renderer = createThemeRenderer({ components, effects, mascot, reportError: ui.toast });
    let preferences = defaults();
    let themes = [];
    let draft;
    let saving = false;
    let editorGeneration = 0;
    let tokenDirty = false;
    let previewSerial = 0;
    let portraitControls;
    const base = () => themes.find(t => t.manifest.id === 'karolina');
    const apply = value => renderer.apply(base(), themes.find(t => t.manifest.id === value.themeId) || base(), value);
    const initialize = async () => {
        try {
            const data = await api('appearance');
            themes = data.themes;
            preferences = data.preferences;
            if (!themes.some(t => t.manifest.id === preferences.themeId)) {
                ui.toast('原主题已移除，已使用默认主题');
                preferences.themeId = 'karolina';
            }
            await apply(preferences);
        } catch (error) {
            ui.toast('外观加载失败：' + error.message + '；已恢复默认视觉');
            const response = await fetch('/theme-assets/karolina/theme.json');
            if (!response.ok) throw new Error('默认主题资源缺失');
            themes = [{ manifest: await response.json(), baseUrl: '/theme-assets/karolina/', builtIn: true }];
            preferences = defaults();
            await apply(preferences);
        }
        ui.$ ('theme').onclick = async () => {
            const next = { ... preferences, mode: preferences.mode === 'dark' ? 'light': 'dark' };
            try {
                await apply(next);
                await api('appearance', next);
                preferences = next;
            } catch (error) {
                await apply(preferences);
                ui.toast(error.message);
            }
        };
    };
    const mountSettings = () => {
        if (saving) return;
        const generation = ++ editorGeneration;
        draft = structuredClone(preferences);
        tokenDirty = false;
        saving = false;
        const mount = ui.$ ('settingsAppearanceMount');
        if (!mount) return;
        mount.innerHTML = settingsMarkup(themes, draft, ui.esc, previewMarkup);
        components.refresh(mount);
        const changed = () => {
            preview().catch (error => ui.toast(error.message));
        };
        portraitControls = bindPortraitControls(mount, (key, value) => {
            try {
                if (tokenDirty) {
                    const tokens = JSON.parse(ui.$ ('appearanceTokens').value);
                    validateTokens(tokens);
                    draft.tokens = tokens;
                    tokenDirty = false;
                }
                draft.tokens[key] = value;
                ui.$ ('appearanceTokens').value = JSON.stringify(draft.tokens, null, 2);
                preview(false).catch (error => ui.toast(error.message));
            } catch (error) {
                portraitControls.sync(draft.tokens);
                ui.toast(error.message);
            }
        });
        for (const id of['appearanceTheme', 'appearanceMode', 'appearanceMotion', 'appearanceEffects', 'appearanceMascot', 'appearanceOpacity']) ui.$ (id).addEventListener('change', changed);
        document.querySelectorAll('[data-appearance-color]').forEach(input => input.addEventListener('input', changed));
        ui.$ ('appearanceTokens').oninput = () => {
            tokenDirty = true;
        };
        ui.$ ('appearanceApplyTokens').onclick = () => {
            try {
                draft.tokens = JSON.parse(ui.$ ('appearanceTokens').value);
                validateTokens(draft.tokens);
                tokenDirty = false;
                portraitControls.sync(draft.tokens);
                preview(false).catch (error => ui.toast(error.message));
            } catch (error) {
                ui.toast(error.message);
            }
        };
        ui.$ ('appearanceReset').onclick = async () => {
            const currentEditor = editorGeneration;
            draft = defaults();
            tokenDirty = false;
            syncFields();
            await apply(draft);
            if (currentEditor === editorGeneration) {
                portraitControls.sync(draft.tokens);
                ui.$ ('appearanceTokens').value = '{}';
                setStatus('已预览默认外观；点击保存后生效。');
            }
        };
        ui.$ ('appearanceSave').onclick = save;
        ui.$ ('appearanceExport').onclick = exportPack;
        ui.$ ('appearanceImport').onclick = importPack;
        syncFields();
        apply(draft).then(() => {
            if (generation === editorGeneration && draft) portraitControls.sync(draft.tokens);
        }).catch (error => ui.toast(error.message));
    };
    document.addEventListener('karolina:settings-tab', event => {
        if (event.detail === 'appearance' || !draft || saving) return;
        ++ previewSerial;
        ++ editorGeneration;
        draft = null;
        apply(preferences).catch(error => ui.toast(error.message));
    });
    const setStatus = text => {
        const element = ui.$ ('appearanceStatus');
        if (element) element.textContent = text;
    };
    const syncFields = () => {
        for (const[id, key] of[['appearanceTheme', 'themeId'], ['appearanceMode', 'mode'], ['appearanceMotion', 'motion'], ['appearanceEffects', 'effects']]) ui.$ (id).value = draft[key];
        ui.$ ('appearanceMascot').checked = draft.mascotVisible;
        ui.$ ('appearanceOpacity').value = draft.backgroundOpacity;
        ui.$ ('appearanceTokens').value = JSON.stringify(draft.tokens, null, 2);
        portraitControls.sync(draft.tokens);
        document.querySelectorAll('[data-appearance-color]').forEach(input => {
            const value = draft.tokens[input.dataset.appearanceColor] || getComputedStyle(document.documentElement).getPropertyValue(input.dataset.appearanceColor).trim();
            if (/^#[0-9a-f]{6}$/i.test(value)) input.value = value;
        });
    };
    const readFields = () => {
        portraitControls.validate();
        draft = {
            ... draft,
            themeId: ui.$ ('appearanceTheme').value,
            mode: ui.$ ('appearanceMode').value,
            motion: ui.$ ('appearanceMotion').value,
            effects: ui.$ ('appearanceEffects').value,
            mascotVisible: ui.$ ('appearanceMascot').checked,
            backgroundOpacity: Number(ui.$ ('appearanceOpacity').value)
        };
    };
    const preview = async (readColors = true) => {
        if (!draft) return;
        readFields();
        if (readColors) document.querySelectorAll('[data-appearance-color]').forEach(input => {
            if (document.activeElement === input) draft.tokens[input.dataset.appearanceColor] = input.value;
        });
        validateTokens(draft.tokens);
        const serial = ++ previewSerial;
        const value = structuredClone(draft);
        if (await apply(value) && serial === previewSerial && draft) {
            portraitControls.sync(draft.tokens);
            if (!tokenDirty) ui.$ ('appearanceTokens').value = JSON.stringify(draft.tokens, null, 2);
            setStatus('正在预览；保存后重启仍保留，切换设置分类会放弃未保存预览。');
        }
    };
    const save = async () => {
        if (saving) return;
        const generation = editorGeneration;
        const mount = ui.$ ('settingsAppearanceMount');
        const controls = [... mount.querySelectorAll('button,input,select,textarea')].map(element => ({ element, disabled: element.disabled }));
        try {
            readFields();
            draft.tokens = JSON.parse(ui.$ ('appearanceTokens').value);
            validateTokens(draft.tokens);
            const value = structuredClone(draft);
            saving = true;
            mount.dataset.busy = 'true';
            mount.setAttribute('aria-busy', 'true');
            controls.forEach(({ element }) => element.disabled = true);
            await apply(value);
            await api('appearance', value);
            preferences = value;
            if (generation === editorGeneration) {
                draft = structuredClone(preferences);
                tokenDirty = false;
                syncFields();
                await apply(preferences);
                setStatus('外观已保存。');
            }
            ui.toast('外观已保存');
        } catch (error) {
            ui.toast(error.message);
        } finally {
            saving = false;
            delete mount.dataset.busy;
            mount.removeAttribute('aria-busy');
            controls.forEach(({ element, disabled }) => {
                if (element.isConnected) element.disabled = disabled;
            });
        }
    };
    const exportPack = async () => {
        const currentEditor = editorGeneration;
        const button = ui.$ ('appearanceExport');
        button.disabled = true;
        try {
            readFields();
            draft.tokens = JSON.parse(ui.$ ('appearanceTokens').value);
            validateTokens(draft.tokens);
            const value = structuredClone(draft);
            const blob = await request('appearance/export', { method: 'POST', body: value, binary: true });
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url;
            link.download = value.themeId + '-theme.zip';
            link.click();
            setTimeout(() => URL.revokeObjectURL(url), 30000);
            if (currentEditor === editorGeneration) setStatus('已导出主题包，可修改 theme.json、组件 CSS 和资源后重新导入。');
        } catch (error) {
            ui.toast(error.message);
        } finally {
            button.disabled = false;
        }
    };
    const importPack = async () => {
        const currentEditor = editorGeneration;
        const file = ui.$ ('appearanceFile').files[0];
        if (!file) {
            ui.toast('请选择 ZIP 主题包');
            return;
        }
        const button = ui.$ ('appearanceImport');
        button.disabled = true;
        try {
            const item = await request('appearance/import?replace=' + ui.$ ('appearanceReplace').checked, { method: 'POST', body: file, contentType: 'application/zip' });
            const data = await api('appearance');
            themes = data.themes;
            if (currentEditor !== editorGeneration || !draft) {
                ui.toast('主题包已导入，可在下次打开外观时选择');
                return;
            }
            const select = ui.$ ('appearanceTheme');
            select.replaceChildren(... themes.map(t => new Option(t.manifest.name, t.manifest.id)));
            select.value = item.manifest.id;
            if (!tokenDirty) draft.tokens = { };
            await preview(false);
            if (currentEditor === editorGeneration) setStatus('主题包已导入并预览；点击保存来采用。');
        } catch (error) {
            ui.toast(error.message);
        } finally {
            button.disabled = false;
        }
    };
    return { initialize, mountSettings, get preferences() {
        return structuredClone(preferences);
    }
    };
}
