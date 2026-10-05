/** Resizing owns only geometry. It does not reload documents, change conversations, or replace theme preferences. */
export function createPanelLayout({ api, ui }) {
    const configurations = [{ key: 'sidebar', target: '#sidebar', axis: 'x', min: 180, max: () => Math.min(640, innerWidth - 500), label: '目录与工作区分隔线' }, { key: 'toc', target: '#documentToc', axis: 'x', min: 100, max: el => Math.min(480, el.parentElement.clientWidth - 320), label: '文档目录与正文分隔线' }, { key: 'reviewFiles', target: '.review-files', axis: 'x', min: 160, max: el => Math.min(560, el.parentElement.clientWidth - 320), label: '审批文件与差异分隔线' }, { key: 'reviewHeight', target: '.review-layout', axis: 'y', min: 240, max: () => 1400, label: '审批差异区高度' }, {
        key: 'composer',
        target: '.composer-wrap',
        axis: 'y',
        direction: - 1,
        min: 180,
        max: el => {
            const siblings = [... el.parentElement.children].filter(child => child !== el && !child.matches('.messages') && child.getClientRects().length);
            return Math.min(1100, el.parentElement.clientHeight - 100 - siblings.reduce((sum, child) => {
                const style = getComputedStyle(child);
                return sum + child.getBoundingClientRect().height + parseFloat(style.marginTop) + parseFloat(style.marginBottom);
            }, 0));
        },
        label: '对话与输入区分隔线',
        before: true
    }];
    let sizes = { }, drag = null, timer = null, nextSave = null, saving = null, disposed = false;
    const writerId = crypto.randomUUID();
    let sequence = 0;
    const controls = [];
    const clamp = (config, value) => Math.round(Math.max(config.min, Math.min(Math.max(config.min, config.max(config.element)), value)));
    const measure = config => config.element.getBoundingClientRect()[config.axis === 'x' ? 'width': 'height'];
    const apply = config => {
        if (!config.element.getClientRects().length) return;
        const value = sizes[config.key];
        if (value == null) document.documentElement.style.removeProperty('--k-layout-' + config.key);
        else document.documentElement.style.setProperty('--k-layout-' + config.key, clamp(config, value) + 'px');
        config.handle.setAttribute('aria-valuenow', String(Math.round(value == null ? measure(config): clamp(config, value))));
        config.handle.setAttribute('aria-valuemax', String(Math.max(config.min, config.max(config.element))));
    };
    const flush = () => {
        clearTimeout(timer);
        timer = null;
        if (!nextSave || saving || disposed) return;
        saving = (async () => {
            while (nextSave && !disposed) {
                const value = nextSave;
                nextSave = null;
                await api('layout', value, { keepalive: true });
            }
        })().catch (error => {
            if (!disposed) ui.toast('面板尺寸保存失败：' + error.message);
        }).finally(() => {
            saving = null;
            if (nextSave && !disposed) flush();
        });
    };
    const remember = () => {
        nextSave = { sizes: { ... sizes }, writerId, sequence: ++ sequence };
        clearTimeout(timer);
        timer = setTimeout(flush, 250);
    };
    const end = (cancel = false) => {
        if (!drag) return;
        const current = drag;
        drag = null;
        delete document.body.dataset.resizing;
        if (cancel) {
            if (current.original == null) delete sizes[current.config.key];
            else sizes[current.config.key] = current.original;
            apply(current.config);
        } else {
            remember();
            flush();
        }
        if (current.config.handle.hasPointerCapture(current.pointer)) current.config.handle.releasePointerCapture(current.pointer);
    };
    for (const config of configurations) {
        const element = document.querySelector(config.target);
        if (!element) continue;
        const handle = document.createElement('div');
        handle.className = 'k-splitter';
        handle.dataset.axis = config.axis;
        handle.dataset.resize = config.key;
        handle.tabIndex = 0;
        handle.setAttribute('role', 'separator');
        handle.setAttribute('aria-label', config.label);
        handle.setAttribute('aria-orientation', config.axis === 'x' ? 'vertical': 'horizontal');
        handle.setAttribute('aria-valuemin', String(config.min));
        if (!element.id) element.id = 'panel-' + config.key;
        handle.setAttribute('aria-controls', element.id);
        handle.title = '拖动调整；方向键微调；双击或 Home 恢复默认';
        if (config.before) element.before(handle);
        else element.after(handle);
        Object.assign(config, { element, handle });
        controls.push(config);
        handle.onpointerdown = event => {
            if (event.button !== 0 || drag) return;
            event.preventDefault();
            handle.focus();
            drag = { config, pointer: event.pointerId, start: event[config.axis === 'x' ? 'clientX': 'clientY'], size: measure(config), original: sizes[config.key] };
            document.body.dataset.resizing = config.axis;
            handle.setPointerCapture(event.pointerId);
        };
        handle.onpointermove = event => {
            if (!drag || drag.pointer !== event.pointerId || drag.config !== config) return;
            sizes[config.key] = clamp(config, drag.size + (event[config.axis === 'x' ? 'clientX': 'clientY'] - drag.start) * (config.direction || 1));
            apply(config);
        };
        handle.onpointerup = event => {
            if (drag?.pointer === event.pointerId) end();
        };
        handle.onpointercancel = () => end(true);
        handle.onlostpointercapture = () => end(true);
        const reset = () => {
            end(true);
            delete sizes[config.key];
            apply(config);
            remember();
        };
        handle.ondblclick = reset;
        handle.onkeydown = event => {
            if (event.key === 'Home') {
                event.preventDefault();
                reset();
                return;
            }
            if (drag) return;
            const negative = config.axis === 'x' ? 'ArrowLeft': 'ArrowUp', positive = config.axis === 'x' ? 'ArrowRight': 'ArrowDown';
            if (![negative, positive].includes(event.key)) return;
            event.preventDefault();
            sizes[config.key] = clamp(config, measure(config) + (event.key === negative ? - 1: 1) * (config.direction || 1) * (event.shiftKey ? 40: 12));
            apply(config);
            remember();
        };
        handle.onkeyup = flush;
        apply(config);
    }
    const resized = () => {
        controls.forEach(apply);
        constrainDialog();
    };
    const navigated = () => {
        end(true);
        requestAnimationFrame(() => {
            if (!disposed) controls.forEach(apply);
        });
    };
    const escape = event => {
        if (event.key === 'Escape' && drag) {
            event.preventDefault();
            event.stopPropagation();
            end(true);
        }
    };
    window.addEventListener('resize', resized);
    document.addEventListener('keydown', escape, true);
    document.addEventListener('karolina:navigate', navigated);
    document.addEventListener('karolina:appearance', navigated);
    // Dialogs remain a single native web dialog, with draggable headers and the browser's resize grip.
    const dialog = ui.$ ('drawer'), header = dialog.querySelector('.drawer-header');
    let moving = null;
    const constrainDialog = () => {
        if (!dialog.open || !dialog.style.left) return;
        dialog.style.left = Math.max(8, Math.min(innerWidth - dialog.offsetWidth - 8, parseFloat(dialog.style.left))) + 'px';
        dialog.style.top = Math.max(8, Math.min(innerHeight - dialog.offsetHeight - 8, parseFloat(dialog.style.top))) + 'px';
    };
    const dialogSize = new ResizeObserver(constrainDialog);
    dialogSize.observe(dialog);
    const chatSize = new ResizeObserver(() => {
        if (!disposed) controls.forEach(apply);
    });
    controls.forEach(config => chatSize.observe(config.element));
    chatSize.observe(document.querySelector('.composer-wrap').parentElement);
    chatSize.observe(document.getElementById('approval'));
    const moveStart = event => {
        if (event.button !== 0 || event.target.closest('button,input,select,textarea,a')) return;
        const rect = dialog.getBoundingClientRect();
        moving = { pointer: event.pointerId, x: event.clientX, y: event.clientY, left: rect.left, top: rect.top };
        header.setPointerCapture(event.pointerId);
        event.preventDefault();
    };
    const move = event => {
        if (!moving || moving.pointer !== event.pointerId) return;
        const left = Math.max(8, Math.min(innerWidth - dialog.offsetWidth - 8, moving.left + event.clientX - moving.x));
        const top = Math.max(8, Math.min(innerHeight - dialog.offsetHeight - 8, moving.top + event.clientY - moving.y));
        Object.assign(dialog.style, { position: 'fixed', inset: 'auto', margin: '0', left: left + 'px', top: top + 'px' });
    };
    const moveEnd = () => {
        if (moving && header.hasPointerCapture(moving.pointer)) header.releasePointerCapture(moving.pointer);
        moving = null;
    };
    const moveLost = () => moving = null;
    const recenter = () => {
        moveEnd();
        for (const key of['position', 'inset', 'margin', 'left', 'top']) dialog.style[key] = '';
    };
    header.addEventListener('pointerdown', moveStart);
    header.addEventListener('pointermove', move);
    header.addEventListener('pointerup', moveEnd);
    header.addEventListener('pointercancel', moveEnd);
    header.addEventListener('lostpointercapture', moveLost);
    dialog.addEventListener('close', recenter);
    return {
        async initialize() {
            try {
                sizes = (await api('layout')).sizes || { };
                controls.forEach(apply);
            } catch (error) {
                ui.toast('面板布局加载失败：' + error.message);
            }
        },
        dispose() {
            end(true);
            clearTimeout(timer);
            if (nextSave) {
                const value = nextSave;
                nextSave = null;
                api('layout', value, { keepalive: true }).catch (error => console.warn('退出时面板尺寸保存失败', error));
            }
            disposed = true;
            dialogSize.disconnect();
            chatSize.disconnect();
            window.removeEventListener('resize', resized);
            document.removeEventListener('keydown', escape, true);
            document.removeEventListener('karolina:navigate', navigated);
            document.removeEventListener('karolina:appearance', navigated);
            header.removeEventListener('pointerdown', moveStart);
            header.removeEventListener('pointermove', move);
            header.removeEventListener('pointerup', moveEnd);
            header.removeEventListener('pointercancel', moveEnd);
            header.removeEventListener('lostpointercapture', moveLost);
            dialog.removeEventListener('close', recenter);
            controls.forEach(({ handle }) => handle.remove());
        }
    };
}
