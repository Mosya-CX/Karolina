import { portraitControlsMarkup } from './portrait-controls.js';
export function previewMarkup() {
    return `<div class="k-panel-title">同一套组件，不同的外观</div><p>框、节点、指示、图标与标签都可以独立替换。</p>
    <div class="appearance-preview-row"><img class="k-brand-image" data-brand-resource alt="Karolina 品牌"><span class="badge">当前外观</span></div>
    <div class="appearance-preview-row"><button data-k-icon="read">阅读</button><button data-k-icon="edit" class="selected">编辑</button><button data-k-icon="save" class="primary">保存</button></div>
    <div class="appearance-preview-row"><button data-k-icon="check">通过</button><button data-k-icon="return">退回</button><button data-k-icon="refresh">再执行</button><button disabled>不可用</button></div>
    <div class="appearance-preview-icons">${['search','attach','send','stop','codex','unity'].map(key=>`<button class="k-icon-only" data-k-icon="${key}" aria-label="${key}" title="${key}"></button>`).join('')}</div>
    <label class="k-field-label">输入框<input placeholder="清晰地描述一个想法"></label><label class="k-field-label">选择框<select><option>讨论需求</option><option>制定计划</option><option>执行任务</option></select></label>
    <label class="k-inline-check"><input type="checkbox" checked>节点与选中状态</label><div class="appearance-preview-row"><span class="dot online"></span><span>就绪</span><span class="badge">待处理</span></div>`;
}
export function settingsMarkup(themes, preferences, esc, preview) {
    return `<div class="appearance-layout"><div><div class="appearance-fields">
    <label class="wide">主题<select id="appearanceTheme">${themes.map(theme=>`<option value="${esc(theme.manifest.id)}">${esc(theme.manifest.name)}</option>`).join('')}</select></label>
    <label>配色<select id="appearanceMode"><option value="dark">深色</option><option value="light">浅色</option></select></label>
    <label>动效<select id="appearanceMotion"><option value="system">跟随系统</option><option value="full">完整动效</option><option value="reduced">轻量动效</option><option value="off">关闭动效</option></select></label>
    <label>场景效果<select id="appearanceEffects"><option value="auto">自动</option><option value="shader">Shader</option><option value="static">静态背景</option></select></label>
    <label class="k-inline-check"><input id="appearanceMascot" type="checkbox">显示看板娘</label>
    <label class="wide">背景强度<input id="appearanceOpacity" type="range" min="0" max="1" step="0.05"></label></div>${portraitControlsMarkup()}
    <div class="appearance-colors">${[['--k-pink','粉'],['--k-cyan','青'],['--k-purple','紫'],['--k-blue','过渡蓝']].map(([key,title])=>`<label>${title}<input type="color" data-appearance-color="${key}" aria-label="${title}"></label>`).join('')}</div>
    <details><summary>更多设计变量</summary><p class="appearance-help">用 --k-* 变量覆盖切角、节点、间距、尺寸、字体和颜色。删除某项恢复主题设置。</p><textarea id="appearanceTokens" class="appearance-token-editor" aria-label="自定义设计变量" rows="6">${esc(JSON.stringify(preferences.tokens,null,2))}</textarea><button id="appearanceApplyTokens">预览变量</button></details>
    <div class="appearance-actions"><button id="appearanceSave" class="primary" data-k-icon="save">保存外观</button><button id="appearanceReset" data-k-icon="reset">恢复默认</button></div>
    <p id="appearanceStatus" class="appearance-status" role="status"></p></div>
    <section id="appearancePreview" class="appearance-preview k-panel" aria-label="组件预览"><div class="k-panel-title">立绘预览</div><div class="appearance-portrait-preview"><img id="appearancePortraitPreview" class="chat-portrait-art" alt="当前聊天立绘预览"></div>${preview()}</section></div>
    <div class="appearance-package"><div class="k-panel-title">主题包</div><p class="appearance-help">导出后可逐个替换图片、图标、字体、组件 CSS 和 Shader。主题包包含 theme.json，导入后按工程保存。</p><input id="appearanceFile" type="file" accept=".zip,application/zip" aria-label="选择主题包"><label class="k-inline-check"><input id="appearanceReplace" type="checkbox">替换同名的自定义主题（原包留存恢复副本）</label><div class="appearance-actions"><button id="appearanceImport" data-k-icon="upload">导入主题包</button><button id="appearanceExport" data-k-icon="download">导出当前主题</button></div></div>`;
}
