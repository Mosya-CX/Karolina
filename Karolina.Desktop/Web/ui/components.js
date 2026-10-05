import { iconKey, textButtonGroups } from './icon-registry.js';
const frameMarkup = '<svg viewBox="0 0 100 40" preserveAspectRatio="none" aria-hidden="true"><path class="k-frame-line" vector-effect="non-scaling-stroke" d="M11 1H86L99 13V30L91 39H10L1 30V11Z"/><path class="k-frame-leading" vector-effect="non-scaling-stroke" d="M1 19V11L11 1H33"/><path class="k-frame-trailing" vector-effect="non-scaling-stroke" d="M75 39H91L99 30V24"/></svg><i class="k-corner-node"></i><i class="k-state-indicator"></i>';
/** Decomposes a button into independent frame, label/content, icon, node, and state indicator atoms. */
export function createComponents(ui) {
    let iconUrls = { };
    let assetUrls = { };
    const drawFrame = button => {
        const svg = button.querySelector(':scope > .k-button-frame svg');
        const width = button.offsetWidth, height = button.offsetHeight;
        if (!svg || width < 2 || height < 2) return;
        const cut = Math.min(parseFloat(getComputedStyle(button).getPropertyValue('--k-button-cut')) || 10, width / 3, height / 3);
        const inset = 1, end = width - inset, bottom = height - inset, small = Math.min(7, cut);
        svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        svg.querySelector('.k-frame-line').setAttribute('d', `M${cut} ${inset}H${end-cut}L${end} ${cut}V${bottom-small}L${end-small} ${bottom}H${cut}L${inset} ${bottom-cut}V${cut}Z`);
        svg.querySelector('.k-frame-leading').setAttribute('d', `M${inset} ${height/2}V${cut}L${cut} ${inset}H${Math.min(width/3,cut+22)}`);
        svg.querySelector('.k-frame-trailing').setAttribute('d', `M${width*.75} ${bottom}H${end-small}L${end} ${bottom-small}V${height*.6}`);
    };
    const observed = new Set();
    const sizes = new ResizeObserver(entries => entries.forEach(entry => drawFrame(entry.target)));
    const decorate = button => {
        if (button.dataset.kNative === 'true' || button.classList.contains('number') || button.closest('.markdown')) return;
        if (!button.classList.contains('k-button')) button.classList.add('k-button');
        if (textButtonGroups[button.id]) button.dataset.kGroup = textButtonGroups[button.id];
        if (!button.querySelector(':scope > .k-button-frame')) {
            const frame = document.createElement('span');
            frame.className = 'k-button-frame';
            frame.setAttribute('aria-hidden', 'true');
            frame.innerHTML = frameMarkup;
            button.append(frame);
            sizes.observe(button);
            observed.add(button);
        }
        const key = iconKey(button);
        if (key && iconUrls[key]) {
            let icon = button.querySelector(':scope > .k-icon');
            if (!icon) {
                icon = document.createElement('img');
                icon.className = 'k-icon';
                icon.alt = '';
                icon.setAttribute('aria-hidden', 'true');
                button.prepend(icon);
            }
            if (icon.getAttribute('src') !== iconUrls[key]) icon.src = iconUrls[key];
            icon.dataset.kKey = key;
        }
        button.querySelector('.k-button-frame').style.backgroundImage = assetUrls.buttonFrame ? `url("${assetUrls.buttonFrame}")`: '';
        if (button.classList.contains('k-custom-frame') !== !!assetUrls.buttonFrame) button.classList.toggle('k-custom-frame', !!assetUrls.buttonFrame);
        const selected = button.classList.contains('active') || button.classList.contains('selected');
        if (button.dataset.page || button.dataset.workflowMode || ['readMode', 'sourceMode'].includes(button.id)) button.setAttribute('aria-pressed', String(selected));
    };
    const refresh = root => {
        if (root instanceof HTMLButtonElement) decorate(root);
        root.querySelectorAll?.('button').forEach(decorate);
        for (const button of observed) if (!button.isConnected) {
            sizes.unobserve(button);
            observed.delete(button);
        }
        const resources = [... (root.querySelectorAll?.('[data-icon-resource], [data-brand-resource]') || [])];
        if (root.matches?.('[data-icon-resource], [data-brand-resource]')) resources.push(root);
        for (const image of resources) {
            const url = image.hasAttribute('data-brand-resource') ? assetUrls.brand: iconUrls[image.dataset.iconResource];
            if (url && image.getAttribute('src') !== url) image.src = url;
        }
    };
    let scheduled = false;
    const pending = new Set();
    const observer = new MutationObserver(records => {
        for (const record of records) {
            const button = record.target.nodeType === 1 ? record.target.closest('button'): record.target.parentElement?.closest('button');
            if (button) pending.add(button);
            if (record.type === 'childList') record.addedNodes.forEach(node => {
                if (node.nodeType === 1) pending.add(node);
            });
        }
        if (scheduled || !pending.size) return;
        scheduled = true;
        queueMicrotask(() => {
            scheduled = false;
            for (const node of pending) if (node.isConnected) refresh(node);
            pending.clear();
            document.body.dataset.conversation = document.getElementById('welcome') ? 'welcome': 'active';
        });
    });
    observer.observe(document.body, { childList: true, subtree: true, attributes: true, attributeFilter: ['class'] });
    refresh(document.body);
    return {
        applyTheme(icons, assets) {
            iconUrls = icons;
            assetUrls = assets;
            refresh(document.body);
            observed.forEach(drawFrame);
        },
        refresh,
        dispose() {
            observer.disconnect();
            sizes.disconnect();
            observed.clear();
            pending.clear();
        }
    };
}
