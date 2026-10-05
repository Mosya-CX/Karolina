import test from 'node:test';
import assert from 'node:assert/strict';
import { createContext } from '../Karolina.Desktop/Web/core/context.js';
import { registerSettings } from '../Karolina.Desktop/Web/settings.js';

test('设置注册完成后，工作台可以继续初始化并通过 actions 切换设置', t => {
    const originalDocument = globalThis.document;
    const events = new Map();
    const tabs = ['ai', 'graph', 'appearance'].map(tab => ({
        dataset: { settingsTab: tab }, selected: false,
        classList: { toggle(_name, selected) { this.owner.selected = selected; } },
        setAttribute(name, value) { this[name] = value; }
    }));
    tabs.forEach(tab => tab.classList.owner = tab);
    const panels = tabs.map(tab => ({ dataset: { settingsPanel: tab.dataset.settingsTab }, hidden: false }));
    globalThis.document = {
        querySelectorAll(selector) {
            if (selector === '[data-settings-tab]') return tabs;
            if (selector === '[data-settings-panel]') return panels;
            return [];
        },
        addEventListener(name, handler) { events.set(name, handler); },
        dispatchEvent(event) { events.get(event.type)?.(event); return true; }
    };
    t.after(() => { globalThis.document = originalDocument; });
    const elements = new Map();
    const context = createContext();
    context.ui = {
        $(id) { if (!elements.has(id)) elements.set(id, { textContent: '' }); return elements.get(id); },
        esc: String, toast(message) { assert.fail('不应出现错误提示：' + message); }
    };
    context.api = async () => { assert.fail('注册或切换外观标签不应发起服务请求'); };
    let mounted = 0;
    context.actions.mountAppearanceSettings = () => mounted++;

    // 使用真实工作台上下文：state 和 actions 各自独立。
    registerSettings(context);
    for (const name of ['loadSettings', 'renderSettings', 'applyModelDefaults', 'selectSettingsTab'])
        assert.equal(typeof context.actions[name], 'function', name);
    assert.equal(Object.hasOwn(context.state, 'actions'), false);
    tabs[2].onclick();
    assert.deepEqual(panels.map(panel => panel.hidden), [true, true, false]);
    assert.deepEqual(tabs.map(tab => tab.selected), [false, false, true]);
    assert.equal(mounted, 1);
    context.actions.applyModelDefaults();
    assert.equal(elements.get('modelProfileStatus').textContent, '连接 Codex 后选择模型。');
});
