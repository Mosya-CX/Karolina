/** Numeric controls are ordinary theme tokens, so saved settings and exported packs share one source. */
const controls = [{ key: '--k-chat-portrait-pivot-x', name: 'Pivot X（左 → 右）', min: 0, max: 100, step: 1, fallback: 100, percent: true }, { key: '--k-chat-portrait-pivot-y', name: 'Pivot Y（上 → 下）', min: 0, max: 100, step: 1, fallback: 100, percent: true }, { key: '--k-chat-portrait-scale', name: '缩放', min: 25, max: 200, step: 1, fallback: 100, percent: false }];
export function portraitControlsMarkup() {
    return `<fieldset class="appearance-portrait-controls"><legend>聊天立绘</legend><p class="appearance-help">Pivot 控制右侧区域内的对齐位置和缩放中心，0% 为左／上，100% 为右／下。放大超出区域的部分会裁切。</p>${controls.map(control => `<label>${control.name}<span class="appearance-portrait-value"><input type="range" data-portrait-range="${control.key}" min="${control.min}" max="${control.max}" step="${control.step}" aria-label="${control.name}滑块"><input type="number" data-portrait-number="${control.key}" min="${control.min}" max="${control.max}" step="${control.step}" required aria-label="${control.name}"><span>%</span></span></label>`).join('')}</fieldset>`;
}
export function bindPortraitControls(root, change) {
    const fields = controls.map(control => ({ ... control, range: root.querySelector(`[data-portrait-range="${control.key}"]`), number: root.querySelector(`[data-portrait-number="${control.key}"]`) }));
    for (const field of fields) {
        for (const input of[field.range, field.number]) input.addEventListener('input', () => {
            if (!input.checkValidity()) return;
            const value = input.valueAsNumber;
            field.range.value = field.number.value = String(value);
            change(field.key, field.percent ? value + '%': String(value / 100));
        });
    }
    return {
        sync(tokens) {
            const style = getComputedStyle(document.documentElement);
            for (const field of fields) {
                const token = tokens[field.key] ?? style.getPropertyValue(field.key).trim();
                let value = field.percent ? (/^\d+(\.\d+)?%$/.test(token) ? parseFloat(token): NaN): Number(token) * 100;
                if (!token || !Number.isFinite(value)) value = field.fallback;
                value = Math.max(field.min, Math.min(field.max, value));
                field.range.value = field.number.value = String(value);
            }
        },
        validate() {
            for (const field of fields) if (!field.number.reportValidity()) throw new Error('请填写有效的立绘参数');
        }
    };
}
