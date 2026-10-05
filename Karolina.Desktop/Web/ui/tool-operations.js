/** Function cards render declarations and delegate calls; no API or shared application state. */
export function renderToolOperations(target, operations, { esc, invoke, onError }) {
    target.replaceChildren();
    for (const operation of operations) {
        const card = document.createElement('details');
        card.open = true;
        card.className = 'tool-operation';
        card.dataset.operation = operation.id;
        card.innerHTML = `<summary>${esc(operation.title)}</summary><div class="tool-operation-body"><p>${esc(operation.description || '')}</p></div>`;
        const body = card.querySelector('.tool-operation-body');
        let input = null;
        if (!operation.menuPath && operation.launchMode !== 'gui' &&
            (Object.keys(operation.defaultArguments || {}).length || (operation.arguments || []).some(a => /\{[A-Za-z]/.test(a)))) {
            const label = document.createElement('label');
            label.textContent = '此函数的参数';
            input = document.createElement('textarea');
            input.rows = 3;
            input.value = JSON.stringify(operation.defaultArguments || {}, null, 2);
            input.setAttribute('aria-label', operation.title + '的参数');
            label.append(input);
            body.append(label);
        }
        const button = document.createElement('button');
        button.className = 'primary';
        button.dataset.invokeOperation = operation.id;
        button.textContent = operation.unityBuild ? '请求构建' : operation.menuPath ? '打开配置窗口' : operation.launchMode === 'gui' ? '打开GUI' : '调用此函数';
        button.onclick = async () => {
            try { await invoke(operation, input ? JSON.parse(input.value) : operation.defaultArguments || {}); }
            catch (e) { onError(e); }
        };
        body.append(button);
        target.append(card);
    }
}
