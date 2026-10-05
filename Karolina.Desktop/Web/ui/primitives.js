export const $ = id => document.getElementById(id);
export const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' } [c]));
/** DOM primitives know nothing about task state or provider implementations. */
export function createUI(afterAction) {
    let toastTimer;
    const toast = text => {
        const element = $ ('toast');
        element.textContent = text;
        element.hidden = false;
        clearTimeout(toastTimer);
        toastTimer = setTimeout(() => element.hidden = true, 6500);
    };
    const action = (id, handler) => {
        $ (id).addEventListener('click', async () => {
            const button = $ (id);
            if (button.disabled) return;
            button.disabled = true;
            button.setAttribute('aria-busy', 'true');
            try {
                await handler();
            } catch (error) {
                toast(error.message);
            } finally {
                button.disabled = false;
                button.removeAttribute('aria-busy');
                afterAction();
            }
        });
    };
    const drawer = (title, html) => {
        if ($ ('drawer').dataset.busy === 'true') return false;
        delete document.body.dataset.drawerKind;
        $ ('drawer').dispatchEvent(new CustomEvent('karolina:drawer-replacing'));
        $ ('drawerTitle').textContent = title;
        $ ('drawerBody').innerHTML = html;
        if (!$ ('drawer').open) $ ('drawer').showModal();
        return true;
    };
    $ ('drawer').addEventListener('close', () => {
        delete document.body.dataset.drawerKind;
    });
    const setText = (id, text) => {
        const element = $ (id);
        if (element.textContent !== text) element.textContent = text;
    };
    const connectionStatus = (id, text, online) => {
        const button = $ (id);
        let label = button.querySelector('.k-label');
        let dot = button.querySelector('.dot');
        if (!label) {
            [... button.childNodes].filter(node => node.nodeType === Node.TEXT_NODE).forEach(node => node.remove());
            label = document.createElement('span');
            label.className = 'k-label';
            button.append(label);
        }
        if (!dot) {
            dot = document.createElement('i');
            dot.className = 'dot';
            button.prepend(dot);
        }
        if (label.textContent !== text) label.textContent = text;
        if (dot.classList.contains('online') !== online) dot.classList.toggle('online', online);
    };
    return { $, esc, toast, action, drawer, setText, connectionStatus };
}
