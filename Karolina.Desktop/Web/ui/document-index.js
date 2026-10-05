export function attachDocumentIndex({ api, ui, state: ctx, actions }, metadata, hash) {
    const { $, esc, toast } = ui;
    const panel = document.querySelector('[data-meta-panel="basic"]'), section = document.createElement('section');
    section.className = 'document-index-editor';
    section.innerHTML = `<h3>资料索引</h3><label>标签（最多10个）<input id="documentTags" value="${esc((metadata.tags || []).join('、'))}" placeholder="用逗号或顿号分隔标签"></label><small>标签用于检索，关闭计划与规则也可编辑。</small>${metadata.type === 'plan' ? '<h4>事前文件预估</h4><div id="plannedChangeRows"></div><button id="addPlannedChange">添加预估文件</button><h4>关联真实资源</h4><div id="actualResourceRows"></div><button id="addActualResource">添加关联文件</button><p class="index-hint">预估与真实关联分开保存；存在文件不代表实现/测试通过。</p>' : ''}<button id="saveDocumentIndex" class="primary">保存标签与资源索引</button>`;
    panel.append(section);
    let plans = structuredClone(metadata.plannedChanges || []), resources = structuredClone(metadata.resourceRefs || []), resourcesChanged = false, plansChanged = false;
    function renderRows() {
        if (metadata.type !== 'plan') return;
        const closed = metadata.status === '关闭';
        $('addPlannedChange').hidden = closed;
        $('plannedChangeRows').innerHTML = plans.map((p, i) => `<div class="resource-edit-row"><select data-plan-op="${i}" ${closed?'disabled':''}>${[['add','新增'],['modify','修改'],['delete','删除']].map(([v,t])=>`<option value="${v}" ${p.operation===v?'selected':''}>${t}</option>`).join('')}</select><input data-plan-path="${i}" value="${esc(p.path)}" placeholder="Assets/... 或 Tools/..." ${closed?'readonly':''}><input data-plan-reason="${i}" value="${esc(p.reason)}" placeholder="预计做什么" ${closed?'readonly':''}>${closed?'':`<button data-remove-plan="${i}" aria-label="删除预估">移除</button>`}</div>`).join('') || `<p>${esc(metadata.estimateStatus || '尚未预估；执行前需登记文件与原因。')}</p>`;
        $('actualResourceRows').innerHTML = resources.map((r, i) => `<div class="resource-edit-row actual"><input data-resource-path="${i}" value="${esc(r.path)}" placeholder="关联的真实文件路径"><input data-resource-evidence="${i}" value="${esc(r.evidence)}" placeholder="与计划关联的依据"><button data-reveal-resource="${i}">定位</button><button data-remove-resource="${i}" aria-label="移除关联">移除</button>${r.operation === 'delete' ? '<small>执行中删除的文件（保留历史关联）</small>' : ''}</div>`).join('') || '<p>尚无已核对的资源关联。</p>';
        section.querySelectorAll('[data-plan-op],[data-plan-path],[data-plan-reason]').forEach(input => input.oninput = () => { const key = input.dataset.planOp != null ? 'operation' : input.dataset.planPath != null ? 'path' : 'reason'; plans[Number(input.dataset.planOp ?? input.dataset.planPath ?? input.dataset.planReason)][key] = input.value; plansChanged = true; });
        section.querySelectorAll('[data-resource-path],[data-resource-evidence]').forEach(input => input.oninput = () => { resources[Number(input.dataset.resourcePath ?? input.dataset.resourceEvidence)][input.dataset.resourcePath != null ? 'path' : 'evidence'] = input.value; resourcesChanged = true; });
        section.querySelectorAll('[data-remove-plan]').forEach(b => b.onclick = () => { plans.splice(Number(b.dataset.removePlan),1); plansChanged = true; renderRows(); });
        section.querySelectorAll('[data-remove-resource]').forEach(b => b.onclick = () => { resources.splice(Number(b.dataset.removeResource),1); resourcesChanged = true; renderRows(); });
        section.querySelectorAll('[data-reveal-resource]').forEach(b => b.onclick = async () => { try { await api('resource/reveal', { path: resources[Number(b.dataset.revealResource)].path }); } catch(e) { toast(e.message); } });
    }
    if (metadata.type === 'plan') {
        renderRows();
        $('addPlannedChange').onclick = () => { plans.push({ path:'', operation:'modify', reason:'' }); plansChanged = true; renderRows(); };
        $('addActualResource').onclick = () => { resources.push({path:'', evidence:''}); resourcesChanged = true; renderRows(); };
    }
    $('saveDocumentIndex').onclick = async () => {
        const button = $('saveDocumentIndex'); button.disabled = true;
        try {
            await api('document/index', { id:metadata.id, expectedMetadataHash:hash, tags:$('documentTags').value.split(/[、,，\n]/), plannedChanges:plansChanged?plans:null, resourceRefs:resourcesChanged?resources:null });
            $('drawer').close(); ctx.docs = await api('documents'); actions.renderReferences(); ctx.documentCache.delete(metadata.id);
            if (ctx.docDirty) { if (ctx.currentDoc?.document.id === metadata.id) ctx.currentDoc.document = ctx.docs.find(d => d.id === metadata.id) || ctx.currentDoc.document; }
            else if (ctx.page === 'tools' && ctx.selectedTool?.definition.guideId === metadata.id) {
                const guide = await api('document/' + encodeURIComponent(metadata.id));
                $('toolManual').innerHTML = ui.markdown(guide.markdown);
            }
            else if (ctx.currentDoc?.document.id === metadata.id) await actions.loadDocument(metadata.id);
            toast('资料索引已保存');
        } catch(e) { toast(e.message); button.disabled = false; }
    };
}
