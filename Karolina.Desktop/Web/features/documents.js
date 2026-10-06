import { buildToc, evolutionEntries, compareReferenceCode } from '../ui/document-structure.js';
import { attachDocumentIndex } from '../ui/document-index.js';
import { matchesReference, resourceKind } from '../ui/reference-picker.js';
/** documents: business handlers with explicit dependencies. */
export function registerDocuments({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    Object.assign(actions, { paintDocument, loadDocument, documentMetadata, renderReferences, metaValue, showDocumentMetadata, showDocumentResources });
    function paintDocument(data) {
        ctx.currentDoc = data;
        ctx.docDirty = false;
        const d = data.document;
        actions.navigate(['requirement', 'plan', 'rule'].includes(d.type) ? d.type: 'rule');
        $ ('pageTitle').textContent = d.title;
        $ ('documentStatus').textContent = d.status || '';
        $ ('documentStatus').hidden = !d.status;
        $ ('metadataButton').disabled = false;
        $ ('historyButton').hidden = d.type !== 'requirement';
        $('resourcesButton').hidden=d.type!=='plan';
        $('resourcesButton').textContent=`关联代码与资源 · ${(d.resourceRefs || []).length}`;
        $ ('sourceMode').disabled = d.type === 'plan' && ['关闭'].includes(d.status);
        $ ('sourceMode').title = $ ('sourceMode').disabled ? '计划已结束，后续工作请新建计划': '';
        $ ('markdown').innerHTML = data.rendered || (data.rendered = markdown(data.markdown));
        $ ('markdown').scrollTop = 0;
        $ ('documentSource').value = data.markdown;
        $ ('markdown').hidden = false;
        $ ('documentSource').hidden = true;
        $ ('saveDocument').hidden = true;
        $ ('changeSummaryRow').hidden = true;
        $ ('changeSummary').value = '';
        $ ('readMode').classList.add('selected');
        $ ('sourceMode').classList.remove('selected');
        actions.rememberWorkspace();
        buildToc($('markdown'), $('documentToc'));
        actions.renderDirectory();
    }
    async function loadDocument(id) {
        const request = ++ ctx.documentRequest;
        try {
            const guideTool = ctx.extensionTools.find(t => t.definition.guideId === id);
            if (guideTool) {
                actions.navigate('tools');
                await actions.openTool(guideTool);
                return;
            }
            if (ctx.currentDoc && id !== ctx.currentDoc.document.id && ctx.docDirty) {
                if (!confirm('正文尚未保存，放弃当前编辑？')) return;
                ctx.docDirty = false;
                $ ('documentSource').value = ctx.currentDoc.markdown;
            }
            const cached = ctx.documentCache.get(id);
            if (cached) actions.paintDocument(cached);
            const fresh = await api('document/' + encodeURIComponent(id));
            ctx.documentCache.set(id, fresh);
            if (ctx.documentCache.size > 32) ctx.documentCache.delete(ctx.documentCache.keys().next().value);
            if (request !== ctx.documentRequest || ctx.docDirty) return;
            if (!cached || fresh.hash !== cached.hash || JSON.stringify(fresh.document) !== JSON.stringify(cached.document)) actions.paintDocument(fresh);
            else ctx.currentDoc = { ... fresh, rendered: cached.rendered };
        } catch (e) {
            toast(e.message);
        }
    }
    async function documentMetadata() {
        if (!ctx.currentDoc) return null;
        const id = ctx.currentDoc.document.id, data = await api('document/' + encodeURIComponent(id) + '/metadata');
        return ctx.currentDoc?.document.id === id ? data: null;
    }
    function renderReferences() {
        const q = $('referenceSearch').value.trim().toLowerCase(), showInactive = $('includeInactiveReferences').checked;
        const type=ctx.referenceType || '', tag=ctx.referenceTag || '';
        if(!showInactive) for(const d of ctx.docs) if(['废弃','关闭'].includes(d.status))ctx.selectedDocs.delete(d.id);
        $('referenceTypes').querySelectorAll('button').forEach(b=>b.classList.toggle('selected',b.dataset.referenceType===type));
        const tags=[...new Set(ctx.docs.filter(d=>['requirement','plan'].includes(d.type) && (showInactive || !['废弃','关闭'].includes(d.status))).flatMap(d=>d.tags || []))].sort((a,b)=>a.localeCompare(b,'zh-CN'));
        $('referenceTags').replaceChildren();
        for(const value of tags) {const chip=document.createElement('button');chip.className='reference-tag';chip.textContent=value;chip.classList.toggle('selected',tag===value);chip.onclick=()=>{ctx.referenceTag=tag===value?'':value;renderReferences();};$('referenceTags').append(chip);}
        $('referenceTagCount').textContent=`浏览标签 · ${tags.length}${tag?' · 已选：'+tag:''}`;
        $('referenceList').replaceChildren();
        const documents = ctx.docs.filter(d => ['requirement', 'plan'].includes(d.type) &&
            (!type || d.type===type) && (showInactive || !['废弃','关闭'].includes(d.status)) && matchesReference(d,q,tag)).sort(compareReferenceCode);
        for (const d of documents) {
            const card = document.createElement('div'), label = document.createElement('label'), checkbox = document.createElement('input'), text = document.createElement('span');
            card.className = 'reference-document'; checkbox.type = 'checkbox'; checkbox.checked = ctx.selectedDocs.has(d.id);
            checkbox.onchange = () => { checkbox.checked ? ctx.selectedDocs.add(d.id) : ctx.selectedDocs.delete(d.id); renderReferences(); };
            text.innerHTML = `<span class="reference-type ${d.type}">${d.type==='plan'?'计划':'需求'}</span><strong>${esc(d.title)}</strong><small class="reference-status">${esc(d.status)}</small>`;
            label.append(checkbox,text); card.append(label);
            const documentTags=document.createElement('div');documentTags.className='reference-document-tags';
            for(const value of d.tags || []) {const chip=document.createElement('button');chip.className='reference-tag';chip.textContent=value;chip.onclick=()=>{ctx.referenceTag=ctx.referenceTag===value?'':value;renderReferences();};documentTags.append(chip);}card.append(documentTags);
            if (d.type === 'plan' && checkbox.checked) {
                const refs = d.resourceRefs || [], paths = refs.map(r=>r.selectionKey || r.path);
                if (!ctx.selectedResources.has(d.id)) ctx.selectedResources.set(d.id,new Set(paths));
                const selected = ctx.selectedResources.get(d.id), details = document.createElement('details');
                details.className = 'reference-resources';
                details.open=true;
                const summary = document.createElement('summary'); summary.textContent = `关联真实资源 · ${paths.filter(p=>selected.has(p)).length}/${paths.length}`; details.append(summary);
                if (refs.length) {
                    const allLabel = document.createElement('label'), all = document.createElement('input'); all.type='checkbox'; all.checked=paths.every(p=>selected.has(p)); all.indeterminate=!all.checked && paths.some(p=>selected.has(p));
                    all.onchange=()=>{ ctx.selectedResources.set(d.id,new Set(all.checked?paths:[])); renderReferences(); }; allLabel.append(all,document.createTextNode('全部关联资源')); details.append(allLabel);
                    for (const r of refs) {
                        const key=r.selectionKey || r.path,row=document.createElement('label'), check=document.createElement('input'), caption=document.createElement('span'); check.type='checkbox'; check.checked=selected.has(key);
                        caption.textContent=`${r.repository?'外部仓库 · ':''}${resourceKind(r.path)} · ${r.path}`; caption.title=[r.repository,r.evidence].filter(Boolean).join('\n'); check.onchange=()=>{check.checked?selected.add(key):selected.delete(key);summary.textContent=`关联真实资源 · ${paths.filter(p=>selected.has(p)).length}/${paths.length}`;all.checked=paths.every(p=>selected.has(p));all.indeterminate=!all.checked && paths.some(p=>selected.has(p));}; row.append(check,caption);details.append(row);
                    }
                } else { const hint=document.createElement('small');hint.textContent='此计划尚未关联可核对的真实资源。';details.append(hint); }
                card.append(details);
            }
            $('referenceList').append(card);
        }
        if (!documents.length) $('referenceList').textContent='没有符合筛选的参考资料。';
        $('referenceCount').textContent=ctx.selectedDocs.size?`${ctx.selectedDocs.size} 份`:'未选择'; actions.renderReviewPlans();
    }
    $('includeInactiveReferences').onchange = renderReferences;
    $('referenceTypes').querySelectorAll('button').forEach(b=>b.onclick=()=>{ctx.referenceType=b.dataset.referenceType;renderReferences();});
    async function showDocumentResources() {
        const doc=ctx.currentDoc?.document;if(doc?.type!=='plan')return;
        const refs=await api('document/'+encodeURIComponent(doc.id)+'/resources');
        drawer('关联代码与资源',`<p>${esc(doc.title)} · ${refs.length}个关联文件。关联是定位与取证，不表示人工验收已通过。</p>${refs.map(r=>`<section class="document-resource"><strong>${resourceKind(r.path)} · ${esc(r.path.split('/').at(-1))}</strong><code>${esc(r.path)}</code>${r.repository?`<p>外部仓库：${esc(r.repository)}</p>`:''}<p>${esc(r.evidence)}</p><small>${r.repository?'外部引用，未检查本地文件':r.exists?'当前文件存在':'历史文件已删除或当前缺失'}</small>${r.exists?`<button data-resource-reveal="${esc(r.path)}">在文件管理器定位</button>`:''}</section>`).join('') || '<p>尚未关联真实文件。可在文档信息与关联中登记，不能按目录猜测。</p>'}`);
        $('drawerBody').querySelectorAll('[data-resource-reveal]').forEach(b=>b.onclick=()=>api('resource/reveal',{path:b.dataset.resourceReveal}).catch(e=>toast(e.message)));
    }
    $('resourcesButton').onclick=()=>showDocumentResources().catch(e=>toast(e.message));
    $('tocButton').onclick=()=>{if(!ctx.currentDoc)return;drawer('文档标题目录','<div id="drawerToc" class="drawer-toc"></div>');buildToc($('markdown'),$('drawerToc'));};
    $ ('documentSource').oninput = () => {
        ctx.docDirty = true;
        ++ ctx.editGeneration;
        actions.syncEditorDirty();
    };
    $ ('readMode').onclick = () => {
        if (!ctx.currentDoc) return;
        $ ('changeSummaryRow').hidden = true;
        $ ('markdown').innerHTML = markdown($ ('documentSource').value);
        buildToc($('markdown'), $('documentToc'));
        $ ('markdown').hidden = false;
        $ ('documentSource').hidden = true;
        $ ('saveDocument').hidden = true;
        $ ('readMode').classList.add('selected');
        $ ('sourceMode').classList.remove('selected');
    };
    $ ('sourceMode').onclick = () => {
        if (!ctx.currentDoc || $ ('sourceMode').disabled) return;
        $ ('changeSummaryRow').hidden = ctx.currentDoc.document.type !== 'requirement';
        $ ('markdown').hidden = true;
        $ ('documentSource').hidden = false;
        $ ('saveDocument').hidden = false;
        $ ('readMode').classList.remove('selected');
        $ ('sourceMode').classList.add('selected');
    };
    action('saveDocument', async () => {
        if (!ctx.currentDoc) return;
        const submitted = ctx.currentDoc, id = submitted.document.id, text = $ ('documentSource').value, hash = submitted.hash, summary = $ ('changeSummary').value, generation = ctx.editGeneration, editorPage = ctx.page, editorRequest = ctx.documentRequest;
        await api('document/save', { id, markdown: text, expectedHash: hash, changeSummary: summary });
        const fresh = await api('document/' + encodeURIComponent(id));
        ctx.documentCache.set(id, fresh);
        if (ctx.currentDoc?.document.id === id && ctx.page === editorPage && ctx.documentRequest === editorRequest) {
            if (ctx.editGeneration === generation && $ ('documentSource').value === text) {
                ++ ctx.documentRequest;
                actions.paintDocument(fresh);
            } else {
                ctx.currentDoc = { ... fresh };
                ctx.docDirty = true;
            }
        }
        toast('正文已保存' + (submitted.document.type === 'requirement' && summary ? '，需求变更已记入演进': ''));
    });
    const metaLabels = {
        id: '编号',
        code: '编码',
        title: '标题',
        type: '类别',
        status: '状态',
        domain: '模块',
        version: '版本',
        path: '正文位置',
        created: '创建日期',
        createdAt: '创建日期',
        updatedAt: '更新日期',
        completed: '结束日期',
        risk: '风险',
        implementationState: '实现核查'
    };
    function metaValue(value) {
        return esc(typeof value === 'object' ? JSON.stringify(value): value);
    }
    async function showDocumentMetadata(id = null) {
        try {
            const result = id ? await api('document/' + encodeURIComponent(id) + '/metadata') : await actions.documentMetadata();
            if (!result) return;
            const m = result.metadata;
            const basic = Object.entries(metaLabels).filter(([k]) => m[k] != null).map(([k, label]) => `<dt>${label}</dt><dd>${actions.metaValue(m[k])}</dd>`).join('');
            const links = [... (m.requirementRefs || m.requirements || []), ... (m.related || [])].map(r => {
                const id = typeof r === 'string' ? r: r.id, d = ctx.docs.find(d => d.id === id);
                return `<div class="relation-card">${d?`<a href="#document:${encodeURIComponent(id)}" data-document="${esc(id)}">${esc(d.title)}</a>`:esc(typeof r==='string'?r:r.title)}${typeof r==='object'?`<small>引用版本 ${esc(r.version)} · ${esc((r.sections||[]).join('、'))}${d?.status?' · 当前 '+esc(d.status):''}</small>`:''}</div>`;
            }).join('');
            const editable = !ctx.docDirty && ['requirement', 'plan'].includes(m.type) && !['关闭'].includes(m.status);
            const transitions = m.type === 'requirement' ? ['激活', '废弃']: ({ 准备: ['准备', '执行'], 执行: ['执行', '测试'], 测试: ['测试', '校正', '验收'], 校正: ['校正', '执行', '测试'], 验收: ['验收', '校正', '关闭'] })[m.status] || [];
            const technical = Object.fromEntries(Object.entries(m).filter(([k]) => !['history', 'evolution'].includes(k)));
            drawer('文档信息与关联', `<div class="meta-tabs"><button class="selected" data-meta-tab="basic">元数据</button><button data-meta-tab="relations">关联资料</button><button data-meta-tab="evidence">来源与证据</button></div><section data-meta-panel="basic"><dl class="metadata-grid">${basic}</dl>${editable?`<label>生命周期<select id="documentLife">${transitions.map(x=>`<option ${x===m.status?'selected':''}>${esc(x)}</option>`).join('')}</select></label>${m.type==='plan'?`<label>验收/取消结论<input id="closeConclusion" placeholder="结束计划时填写真实验收或取消结论"></label>`:''}<button id="saveDocumentDetails">保存状态与引用</button>`:''}<details><summary>查看机器元数据</summary><pre>${esc(JSON.stringify(technical,null,2))}</pre></details></section><section data-meta-panel="relations" hidden>${links||'<p>此文档没有关联资料。</p>'}${editable&&m.type==='plan'?`<details><summary>修改参考需求</summary><div class="metadata-refs">${ctx.docs.filter(d=>d.type==='requirement').map(d=>`<label><input type="checkbox" data-req-id="${esc(d.id)}" ${(m.requirements||[]).includes(d.id)?'checked':''}> ${esc(d.title)}</label>`).join('')}</div></details>`:''}</section><section data-meta-panel="evidence" hidden><h3>来源</h3><pre>${esc(JSON.stringify(m.sources||[],null,2))}</pre><h3>工程证据</h3><pre>${esc(JSON.stringify(m.evidence||[],null,2))}</pre>${m.assessment?`<h3>核查结论</h3><pre>${esc(JSON.stringify(m.assessment,null,2))}</pre>`:''}</section>`);
            attachDocumentIndex({ api, ui, state: ctx, actions }, m, result.hash);
            if (editable) $ ('saveDocumentDetails').onclick = async () => {
                const b = $ ('saveDocumentDetails');
                b.disabled = true;
                try {
                    await api('document/details', {
                        id: m.id,
                        expectedMetadataHash: result.hash,
                        status: $ ('documentLife').value,
                        requirements: m.type === 'plan' && JSON.stringify([... document.querySelectorAll('[data-req-id]:checked')].map(x => x.dataset.reqId).sort()) !== JSON.stringify([... (m.requirements || [])].sort()) ? [... document.querySelectorAll('[data-req-id]:checked')].map(x => x.dataset.reqId): null,
                        conclusion: $ ('closeConclusion')?.value || null
                    });
                    $ ('drawer').close();
                    ctx.docs = await api('documents');
                    actions.renderReferences();
                    ctx.documentCache.delete(m.id);
                    await actions.loadDocument(m.id);
                    toast('状态与引用已保存');
                } catch (e) {
                    toast(e.message);
                    b.disabled = false;
                }
            };
            document.querySelectorAll('[data-meta-tab]').forEach(b => b.onclick = () => {
                document.querySelectorAll('[data-meta-tab]').forEach(x => x.classList.toggle('selected', x === b));
                document.querySelectorAll('[data-meta-panel]').forEach(x => x.hidden = x.dataset.metaPanel !== b.dataset.metaTab);
            });
        } catch (e) {
            toast(e.message);
        }
    }
    $('metadataButton').onclick = () => showDocumentMetadata();
    $('historyButton').onclick = () => {
        if (!ctx.currentDoc || ctx.currentDoc.document.type !== 'requirement') return;
        if (ctx.docDirty) { toast('先保存正文，再查看演进时间线。'); return; }
        const entries = evolutionEntries(ctx.currentDoc.markdown);
        drawer('需求演进 · ' + ctx.currentDoc.document.title, `<p>从正文「需求演进」生成，最近记录在前。</p><div class="evolution-timeline">${entries.map((entry,i)=>`<details class="evolution-entry" ${i===0?'open':''}><summary>${esc(entry.title)}</summary><div class="markdown">${markdown(entry.body)}</div></details>`).join('') || '<p>此需求正文尚无演进记录。</p>'}</div>`);
    };
    $ ('newDocument').onclick = () => {
        const type = ctx.page;
        drawer('新建' + ({ requirement: '需求案', plan: '计划案', rule: '规则案' } [type]), `<label>标题<input id="newTitle" placeholder="用功能名称命名"></label><label>英文文件标题<input id="newEnglishTitle" placeholder="例如 document-lifecycle（编号自动生成）"></label><label>英文模块目录<input id="newEnglishDomain" placeholder="例如 combat；留空使用 custom"></label><label>模块显示名<input id="newDomain" placeholder="例如：战斗、Karolina"></label>${type==='rule'?`<label>规则板块<select id="newRuleSection">${ctx.ruleSections.map(([id,title])=>`<option value="${id}">${title}</option>`).join('')}</select></label>`:''}${type==='plan'?`<details open><summary>参考需求（准备可稍后关联，执行前必须填写）</summary><div class="metadata-refs">${ctx.docs.filter(d=>d.type==='requirement').map(d=>`<label><input type="checkbox" data-new-req="${esc(d.id)}"> ${esc(d.title)}</label>`).join('')}</div></details>`:''}<button id="createDocument" class="primary">创建${type==='plan'?'准备':''}</button>`);
        $ ('createDocument').onclick = async () => {
            const b = $ ('createDocument');
            b.disabled = true;
            try {
                const r = await api('document/create', {
                    type,
                    title: $ ('newTitle').value,
                    domain: $ ('newDomain').value,
                    englishTitle: $ ('newEnglishTitle').value,
                    englishDomain: $ ('newEnglishDomain').value,
                    ruleSection: type === 'rule' ? $ ('newRuleSection').value: null,
                    requirements: type === 'plan' ? [... document.querySelectorAll('[data-new-req]:checked')].map(x => x.dataset.newReq): []
                });
                $ ('drawer').close();
                ctx.docs = await api('documents');
                actions.renderReferences();
                await actions.loadDocument(r.id);
                $ ('sourceMode').click();
            } catch (e) {
                toast(e.message);
                b.disabled = false;
            }
        };
    };
    document.addEventListener('click', e => {
        const link = e.target.closest('a[data-document]');
        if (link) {
            e.preventDefault();
            if ($ ('drawer').open) $ ('drawer').close();
            actions.loadDocument(link.dataset.document);
        }
    });
}
