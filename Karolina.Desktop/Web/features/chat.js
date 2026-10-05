/** chat: business handlers with explicit dependencies. */
export function registerChat({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { addMessage, loadChat, renderApprovals, receive });
    function addMessage(role, text, id) {
        $ ('welcome')?.remove();
        const m = document.createElement('div');
        m.className = 'message ' + role;
        const b = document.createElement('div');
        b.className = 'bubble markdown';
        b.style.padding = '0';
        b.style.overflow = 'visible';
        if (role === 'user') {
            b.classList.remove('markdown');
            b.textContent = text;
        } else b.innerHTML = markdown(text);
        m.append(b);
        $ ('messages').append(m);
        if (id) ctx.lastMessage.set(id, { text, node: b });
        $ ('messages').scrollTop = $ ('messages').scrollHeight;
        return b;
    }
    async function loadChat(id) {
        try {
            if (ctx.snapshot.busy && id !== ctx.snapshot.activeThread) throw new Error('请等当前轮次结束后切换对话');
            if (!actions.navigate('chat')) return false;
            const data = await api('chat/' + encodeURIComponent(id));
            if (ctx.taskForChat && ctx.currentReview?.threadId !== id) ctx.taskForChat = null;
            ctx.threadId = id;
            $ ('messages').replaceChildren();
            ctx.lastMessage.clear();
            ctx.toolMessages.clear();
            for (const turn of data.thread.turns || []) for (const item of turn.items || []) {
                if (item.type === 'userMessage') actions.addMessage('user', (item.content || []).filter(c => c.type === 'text').map(c => c.text).join('\n'));
                if (item.type === 'agentMessage') actions.addMessage('agent', item.text || '', item.id);
            }
            actions.renderDirectory();
            actions.updateReviewControls();
            return true;
        } catch (e) {
            toast(e.message);
            return false;
        }
    }
    function renderApprovals() {
        $ ('approval').replaceChildren();
        $ ('approval').hidden = ctx.approvalQueue.size === 0;
        for (const[id, request] of ctx.approvalQueue) {
            const card = document.createElement('div'), label = document.createElement('strong'), pre = document.createElement('pre');
            label.textContent = 'Codex 请求你的批准';
            pre.textContent = JSON.stringify(request.params, null, 2);
            card.append(label, pre);
            for (const[text, accept] of[['允许一次', true], ['拒绝', false]]) {
                const b = document.createElement('button');
                b.textContent = text;
                b.onclick = async () => {
                    b.disabled = true;
                    try {
                        await api('approval', { id, accept });
                        ctx.approvalQueue.delete(id);
                        actions.renderApprovals();
                    } catch (e) {
                        toast(e.message);
                        b.disabled = false;
                    }
                };
                card.append(b);
            }
            $ ('approval').append(card);
        }
    }
    function receive(e) {
        const method = e.method, p = e.params || { };
        if (p.threadId && ctx.threadId && p.threadId !== ctx.threadId) return;
        if (method === 'item/agentMessage/delta') {
            let m = ctx.lastMessage.get(p.itemId);
            if (!m) {
                actions.addMessage('agent', '', p.itemId);
                m = ctx.lastMessage.get(p.itemId);
            }
            m.text += p.delta;
            m.node.innerHTML = markdown(m.text);
        }
        if (method === 'item/completed' && p.item?.type === 'agentMessage') {
            const m = ctx.lastMessage.get(p.item.id);
            if (m) {
                m.text = p.item.text;
                m.node.innerHTML = markdown(m.text);
            } else actions.addMessage('agent', p.item.text, p.item.id);
        }
        if (method === 'item/started' && ['commandExecution', 'fileChange', 'mcpToolCall'].includes(p.item?.type)) {
            const d = document.createElement('details');
            d.className = 'tool-event';
            const summary = document.createElement('summary');
            summary.textContent = p.item.type === 'commandExecution' ? `运行命令：${p.item.command}`: p.item.type === 'mcpToolCall' ? `调用工具：${p.item.tool}`: '修改文件';
            const pre = document.createElement('pre');
            pre.textContent = JSON.stringify(p.item, null, 2);
            d.append(summary, pre);
            $ ('messages').append(d);
            ctx.toolMessages.set(p.item.id, { node: d, summary, pre });
        }
        if (method === 'item/completed' && ctx.toolMessages.has(p.item?.id)) {
            const t = ctx.toolMessages.get(p.item.id), item = p.item;
            t.summary.textContent = (item.type === 'commandExecution' ? '命令': item.type === 'fileChange' ? '文件修改': '工具') + ' · ' + ({ completed: '已完成', failed: '失败', declined: '已拒绝', interrupted: '已中断' })[item.status] + ' ' + (item.command || item.tool || '');
            t.pre.textContent = item.aggregatedOutput || item.output || JSON.stringify(item.changes || item.result || item, null, 2);
        }
        if (method === 'karolina/approval') {
            ctx.approvalQueue.set(JSON.stringify(p.id), p);
            actions.renderApprovals();
        }
        if (method === 'turn/completed') {
            ctx.approvalQueue.clear();
            actions.renderApprovals();
            const status = p.turn?.status;
            document.dispatchEvent(new CustomEvent('karolina:turn-state', { detail: status }));
            toast(status === 'completed' ? '本轮已完成，请审查结果': status === 'interrupted' ? '本轮已停止': `本轮状态：${status} ${p.turn?.error?.message||''}`);
        }
        if (method === 'karolina/review-ready') {
            actions.refreshReviews().catch (e => toast(e.message));
            toast('任务差异已生成，可前往任务审批。');
        }
        if (method === 'karolina/disconnected') {
            ctx.approvalQueue.clear();
            actions.renderApprovals();
        }
        if (method === 'error' || method === 'karolina/error' || method === 'karolina/disconnected') toast(p.error?.message || p.message || 'Codex 返回错误');
    }
    document.addEventListener('click', event => {
        const button = event.target.closest('[data-prompt]');
        if (!button) return;
        if (button.dataset.startMode) actions.setWorkflowMode(button.dataset.startMode);
        $ ('prompt').value = button.dataset.prompt;
        $ ('prompt').focus();
    });
    $ ('newChat').onclick = () => {
        ctx.taskForChat = null;
        ctx.threadId = null;
        actions.updateReviewControls();
        ctx.lastMessage.clear();
        ctx.toolMessages.clear();
        $ ('messages').replaceChildren($ ('welcomeTemplate').content.cloneNode(true));
        actions.renderDirectory();
    };
    action('send', async () => {
        const text = $ ('prompt').value.trim();
        if (!text) throw new Error('请输入指令');
        const data = await api('chat/send', {
            text,
            model: $ ('model').value,
            effort: $ ('effort').value,
            access: $ ('access').value,
            documents: [... ctx.selectedDocs],
            resourceSelections: Object.fromEntries(ctx.docs.filter(d => d.type==='plan' && ctx.selectedDocs.has(d.id)).map(d => [d.id, Array.from(ctx.selectedResources.get(d.id) ?? d.resourceRefs?.map(r => r.path) ?? [])])),
            threadId: ctx.threadId,
            taskId: ctx.taskForChat,
            planId: ctx.workflowMode === 'execute-task' ? $ ('executionPlan').value || null: null,
            mode: ctx.workflowMode,
            graphNodeIds: [...(ctx.graphContextNodes?.keys() || [])]
        });
        ctx.threadId = data.threadId;
        actions.addMessage('user', text);
        $ ('prompt').value = '';
        await actions.refreshState();
    });
    $ ('prompt').onkeydown = e => {
        if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
            e.preventDefault();
            if (!$ ('send').disabled) $ ('send').click();
        }
    };
    action('stop', async () => {
        await api('chat/stop', { });
        toast('已发送停止请求，等待 Codex 确认');
    });
}
