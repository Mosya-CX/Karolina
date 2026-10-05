import { renderFileExplanation } from './ui/review-explanation.js';
import { registerReviewGeneration } from './ui/review-generation.js';
/** review: business handlers with explicit dependencies. */
export function registerReview({ state: ctx, actions, ui, api }) {
    const { $, esc, toast, action, markdown, drawer } = ui;
    registerReviewGeneration({state:ctx,actions,ui,api});
    Object.assign(actions, {
        reviewMutation,
        renderReviewPlans,
        renderReviewDirectory,
        refreshReviews,
        openReview,
        renderReviewFiles,
        openReviewFile,
        paintReviewDiff,
        paintReviewNotes,
        updateReviewControls,
        draftNotes,
        saveFileDecision
    });
    async function reviewMutation(fn) {
        if (ctx.reviewSaving) throw new Error('正在保存审批，请稍候');
        ctx.reviewSaving = true;
        actions.updateReviewControls();
        try {
            return await fn();
        } finally {
            ctx.reviewSaving = false;
            actions.updateReviewControls();
        }
    }
    function renderReviewPlans() {
        const select = $ ('executionPlan'), previous = select.value;
        select.replaceChildren(new Option('不选 · 普通只读对话', ''));
        for (const plan of ctx.docs.filter(d => d.type === 'plan' && d.status !== '关闭')) select.add(new Option(plan.title + ' · ' + plan.status, plan.id));
        if ([... select.options].some(o => o.value === previous)) select.value = previous;
    }
    function renderReviewDirectory(query) {
        for (const task of ctx.reviewTasks.filter(t => t.title.toLowerCase().includes(query))) {
            const button = document.createElement('button');
            button.className = 'review-task';
            button.classList.toggle('active', ctx.currentReview?.id === task.id);
            const visible = task.files.length;
            button.innerHTML = `<span class="title">${esc(task.title)}</span><small>${esc(task.state)} · 第 ${task.round} 轮 · ${visible} 个Agent文件 · ${(task.candidates || []).filter(f=>f.origin==='unknown').length} 个来源待确认</small>`;
            button.onclick = () => {
                if (!ctx.reviewSaving) actions.openReview(task.id).catch (e => toast(e.message));
            };
            $ ('directory').append(button);
        }
        if (!ctx.reviewTasks.length) $ ('directory').innerHTML = '<div class="empty">还没有任务审批<br>在 AI 对话中选择执行计划，或在外部执行前开始记录。</div>';
    }
    async function refreshReviews() {
        if (ctx.reviewReading) return ctx.reviewReading;
        ctx.lastReviewSync = Date.now();
        ctx.reviewReading = (async () => {
            ctx.reviewTasks = await api('reviews');
            if (ctx.page === 'review') {
                actions.renderDirectory();
                const fresh = ctx.reviewTasks.find(t => t.id === ctx.currentReview?.id);
                if (fresh) {
                    ctx.currentReview.executionProgress = fresh.executionProgress;
                    ctx.currentReview.progressError = fresh.progressError;
                    renderExecutionProgress(fresh);
                }
                if (fresh && fresh.revision > ctx.currentReview.revision && !ctx.reviewSaving) {
                    const selectedPath=ctx.reviewFile?.path, selectedFingerprint=ctx.reviewFile?.fingerprint;
                    const originDraft = $('originReason')?.value.trim() || $('reviewOrigins').querySelector('[data-origin-path]:checked');
                    if (!ctx.reviewSummaryDirty && !ctx.reviewNoteDirty && !ctx.reviewFeedbackDirty && !originDraft) {
                        await actions.openReview(fresh.id); const file=[...ctx.currentReview.files,...(ctx.currentReview.candidates || [])].find(f=>f.path===selectedPath);if(file)await actions.openReviewFile(file);
                    } else {
                        ctx.currentReview=fresh;
                        $('reviewState').textContent=fresh.state+' · 第 '+fresh.round+' 轮 · 草稿已保留';
                        const file=[...fresh.files,...(fresh.candidates || [])].find(f=>f.path===selectedPath);
                        if(file?.fingerprint===selectedFingerprint && ctx.reviewFile) {ctx.reviewFile.origin=file.origin;ctx.reviewFile.originEvidence=file.originEvidence;ctx.reviewFile.explanation=file.explanation;if(ctx.reviewDiffData?.file.fingerprint===file.fingerprint)actions.paintReviewDiff({...ctx.reviewDiffData,file:{...ctx.reviewDiffData.file,...file,notes:ctx.reviewFile.notes},explanation:file.explanation});}
                        else if(ctx.reviewNoteDirty) { $('reviewState').textContent+=' · 文件已变化，请核对新版本后迁移意见';showDraftRebase(file); }
                        else if(file)await actions.openReviewFile(file);
                        actions.renderReviewFiles();renderOrigins();actions.updateReviewControls();
                    }
                }
            }
        })();
        try {
            await ctx.reviewReading;
        } finally {
            ctx.reviewReading = null;
        }
    }
    function showDraftRebase(file) {
        let button=$('rebaseReviewDraft');
        if(!button){button=document.createElement('button');button.id='rebaseReviewDraft';$('reviewDiffTitle').after(button);}
        button.hidden=false;button.textContent=file?'核对新差异并保留为文件意见':'复制旧版本意见';
        button.onclick=async()=>{
            if(!file){const text=actions.draftNotes().map(n=>n.text).join('\n');await navigator.clipboard.writeText(text);toast('旧版本意见已复制；此文件已无当前变化。');return;}
            if(!confirm('文件内容已变化。将旧版本草稿与行意见改为文件整体意见，保留原位置说明；请核对新差异后再保存。'))return;
            const notes=actions.draftNotes().map(n=>({text:(n.line?`原版本${n.side==='before'?'变更前':'变更后'}第${n.line}行：`:'原版本意见：')+n.text}));
            const oldDirty=ctx.reviewNoteDirty;ctx.reviewNoteDirty=false;
            try{await actions.openReviewFile(file);ctx.reviewFile.notes=notes;ctx.reviewNoteDirty=true;ctx.reviewDraftGeneration++;actions.paintReviewNotes();actions.syncEditorDirty();button.hidden=true;}catch(e){ctx.reviewNoteDirty=oldDirty;toast(e.message);}
        };
    }
    async function openReview(id, discard = false) {
        if (!discard && (ctx.reviewSummaryDirty || ctx.reviewNoteDirty || ctx.reviewFeedbackDirty) && !confirm('有未保存的摘要或审批意见，放弃后切换？')) return;
        const request = ++ ctx.reviewRequest, generation = ctx.reviewDraftGeneration, data = await api('review/' + id);
        if (request !== ctx.reviewRequest || generation !== ctx.reviewDraftGeneration && !discard) return;
        ctx.currentReview = data;
        ctx.reviewFile = null;
        ctx.reviewSummaryDirty = false;
        ctx.reviewNoteDirty = false;
        ctx.reviewFeedbackDirty = false;
        ctx.noteAnchor = null;
        $ ('reviewEmpty').hidden = true;
        $ ('reviewContent').hidden = false;
        $ ('pageTitle').textContent = data.title;
        $ ('reviewState').textContent = data.state + ' · 第 ' + data.round + ' 轮';
        $ ('reviewRun').textContent = '执行结果：' + data.runState;
        renderExecutionProgress(data);
        $ ('reviewProvenance').textContent = '对照方式：' + data.baselineKind + '\n' + data.baselineDescription;
        $ ('reviewSummary').value = data.summary;
        $ ('reviewFeedback').value = data.feedback;
        $ ('reviewPlan').textContent = ctx.docs.find(d => d.id === data.planId)?.title || data.title;
        $ ('reviewSummary').readOnly = data.state === '已通过' || data.state === '待再执行';
        $ ('reviewFeedback').readOnly = data.state !== '待审批';
        $ ('reviewNotes').replaceChildren();
        $ ('reviewNote').value = '';
        $ ('noteLocation').textContent = '文件整体意见（也可点击差异行定位）';
        $ ('reviewDiffTitle').textContent = '选择文件阅读改动说明';
        $ ('reviewDiff').innerHTML = '<div class="empty">选择文件先看具体改动、原因与验证限制。批准针对本次任务的具体文件版本。</div>';
        $ ('reviewAttempts').innerHTML = data.attempts.length ? `<summary>之前的退回意见（${data.attempts.length} 轮）</summary>` + data.attempts.map(a => `<section class="review-attempt"><strong>第 ${a.round} 轮</strong><p>${esc(a.feedback)}</p>${a.files.filter(f=>f.decision==='不通过').map(f=>`<p><code>${esc(f.path)}</code><br>${f.notes.map(n=>esc(n.text)).join('<br>')}</p>`).join('')}</section>`).join(''): '';
        actions.renderReviewFiles();
        renderOrigins();
        actions.renderDirectory();
        actions.updateReviewControls();
        actions.syncEditorDirty();
        actions.rememberWorkspace();
    }
    function renderExecutionProgress(task) {
        const panel = $('reviewExecutionProgress');
        if (!panel) return;
        const progress = task?.executionProgress;
        const events = progress?.events || [];
        panel.hidden = !progress && !task?.progressError && task?.runState !== '运行中';
        $('reviewProgressSummary').textContent = progress ? `Agent 执行进度 · ${progress.state}` : 'Agent 执行进度';
        $('reviewProgressCurrent').textContent = task?.progressError || (progress ? `${progress.progressStage}：${progress.progressMessage}${progress.progressUpdated ? ' · ' + new Date(progress.progressUpdated).toLocaleString() : ''}` : task?.runState === '运行中' ? '任务已启动，等待 Codex 活动记录…' : '暂无执行过程记录。');
        const list = $('reviewProgressEvents');
        list.replaceChildren();
        for (const event of events.slice(-12).reverse()) {
            const item = document.createElement('li');
            const at = event.at ? new Date(event.at).toLocaleTimeString() + ' · ' : '';
            item.textContent = `${at}${event.stage} · ${event.outcome}：${event.detail}`;
            list.append(item);
        }
    }
    function renderReviewFiles() {
        $('reviewFiles').replaceChildren();
        const q=$('reviewSearch').value.toLowerCase(), files=[...(ctx.currentReview?.files || []),...(ctx.currentReview?.candidates || []).filter(f=>f.origin==='unknown')];
        for(const file of files) {
            if(!file.path.toLowerCase().includes(q)) continue;
            const button=document.createElement('button');button.className='review-file';button.classList.toggle('active',ctx.reviewFile?.path===file.path);
            button.innerHTML=`<span>${esc(file.path)}</span><small>${esc(file.kind)} · ${file.origin==='unknown'?'来源待确认':esc(file.decision)}</small><small class="review-brief">${esc(file.explanation?.fingerprint===file.fingerprint?file.explanation.summary:file.origin==='agent'?'待补齐Agent操作说明':'待补齐冻结变化说明')}</small>`;
            button.onclick=()=>{if(!ctx.reviewSaving) actions.openReviewFile(file).catch(e=>toast(e.message));};$('reviewFiles').append(button);
        }
        if(!files.length) $('reviewFiles').innerHTML='<div class="empty">没有需要审批的Agent变化。人工文件已排除；文件变化后台同步。</div>';
        const unknown=(ctx.currentReview?.candidates || []).filter(f=>f.origin==='unknown').length;
        $('reviewCounts').textContent=`${ctx.currentReview?.files.length || 0} 个Agent文件 · ${unknown} 个来源待确认 · ${(ctx.currentReview?.candidates || []).filter(f=>f.origin==='human').length}个人工文件已排除`;
        $('approveTask').textContent='批准本次任务的Agent变更';
    }
    async function saveOrigins(versions,origin,reason) {
            const id=ctx.currentReview.id;
            if(ctx.reviewNoteDirty) throw new Error('请先保存当前文件意见');
            for(const version of versions) {
                const file=[...ctx.currentReview.files,...ctx.currentReview.candidates].find(f=>f.path===version.path);
                if(!file || file.fingerprint!==version.fingerprint) throw new Error('文件已变化，请先查看最新差异再确认来源');
            }
            const summary=$('reviewSummary').value, feedback=$('reviewFeedback').value, sd=ctx.reviewSummaryDirty, fd=ctx.reviewFeedbackDirty;
            try { for(const version of versions) { ctx.currentReview=await api('review/origin',{id:ctx.currentReview.id,revision:ctx.currentReview.revision,path:version.path,fingerprint:version.fingerprint,origin,reason}); } await actions.openReview(id,true);await actions.refreshReviews(); }
            finally { $('reviewSummary').value=summary;$('reviewFeedback').value=feedback;ctx.reviewSummaryDirty=sd;ctx.reviewFeedbackDirty=fd;actions.syncEditorDirty(); }
        }

    function renderOrigins() {
        const target=$('reviewOrigins'), task=ctx.currentReview, candidates=task?.candidates || [];
        const sameTask=target.dataset.reviewId===task?.id;
        const open=sameTask?[...target.querySelectorAll(':scope > details')].map(d=>d.open):[];
        const reason=sameTask?($('originReason')?.value || ''):'';
        const selected=new Map(sameTask?[...target.querySelectorAll('[data-origin-path]:checked')].map(e=>[e.dataset.originPath,e.dataset.originFingerprint]):[]);
        const unknown=candidates.filter(f=>f.origin==='unknown'), human=candidates.filter(f=>f.origin==='human');
        const rows=(files,check)=>files.map(f=>`<div class="origin-row">${check?`<input type="checkbox" data-origin-path="${esc(f.path)}" data-origin-fingerprint="${esc(f.fingerprint)}" aria-label="选择来源待确认文件">`:''}<button data-origin-preview="${esc(f.path)}">${esc(f.path)}</button><small>${esc(f.explanation?.summary || f.kind)} · ${esc(f.originEvidence)}</small>${check?'':`<button data-restore-origin="${esc(f.path)}" data-origin-fingerprint="${esc(f.fingerprint)}">重新确认</button>`}</div>`).join('');
        target.innerHTML=`<details class="origin-pending" ><summary>来源待确认 · ${unknown.length}</summary><p>同期人工素材与命令/MCP写入无法仅凭时间判断。确认属于Agent的版本才进入审批；未知来源会阻止整体通过。</p>${rows(unknown,true)}${unknown.length?'<label>确认依据<input id="originReason" placeholder="例如：这些素材由我提供 / 这些文件由Agent工具写入"></label><button data-confirm-origin="agent">所选属于Agent</button><button data-confirm-origin="human">所选属于人工</button>':''}</details><details><summary>人工变化（不参与审批）· ${human.length}</summary>${rows(human,false)}</details>`;
        target.dataset.reviewId=task?.id || '';
        target.querySelectorAll(':scope > details').forEach((d,i)=>d.open=!!open[i]);
        if($('originReason')) $('originReason').value=reason;
        target.querySelectorAll('[data-origin-path]').forEach(e=>e.checked=selected.get(e.dataset.originPath)===e.dataset.originFingerprint);
        target.querySelectorAll('[data-origin-preview]').forEach(b=>b.onclick=()=>actions.openReviewFile(candidates.find(f=>f.path===b.dataset.originPreview)).catch(e=>toast(e.message)));
        target.querySelectorAll('[data-confirm-origin]').forEach(b=>b.onclick=()=>actions.reviewMutation(async()=>{
            const versions=[...target.querySelectorAll('[data-origin-path]:checked')].map(x=>({path:x.dataset.originPath,fingerprint:x.dataset.originFingerprint})),reason=$('originReason').value.trim();
            if(!versions.length || !reason) throw new Error('请选择文件并填写来源依据');await saveOrigins(versions,b.dataset.confirmOrigin,reason);
        }).catch(e=>toast(e.message)));
        target.querySelectorAll('[data-restore-origin]').forEach(b=>b.onclick=()=>actions.reviewMutation(()=>saveOrigins([{path:b.dataset.restoreOrigin,fingerprint:b.dataset.originFingerprint}],'unknown','人工请求重新确认来源')).catch(e=>toast(e.message)));
        target.querySelectorAll('button,input').forEach(e=>e.disabled=ctx.reviewSaving || ctx.snapshot.busy || !['执行','待审批'].includes(task?.state));
    }
    async function openReviewFile(file) {
        if (ctx.reviewNoteDirty && !confirm('文件意见还未保存，放弃后切换？')) return;
        const request = ++ ctx.reviewRequest, id = ctx.currentReview.id, generation = ctx.reviewDraftGeneration, data = await api('review/' + id + '/diff?path=' + encodeURIComponent(file.path));
        if (request !== ctx.reviewRequest || id !== ctx.currentReview?.id || generation !== ctx.reviewDraftGeneration) return;
        ctx.reviewFile = data.file;
        if($('rebaseReviewDraft'))$('rebaseReviewDraft').hidden=true;
        ctx.noteAnchor = null;
        ctx.reviewNoteDirty = false;
        $ ('reviewNote').value = '';
        $ ('reviewDiffTitle').textContent = file.path;
        $ ('noteLocation').textContent = '文件整体意见（也可点击差异行定位）';
        actions.renderReviewFiles();
        actions.paintReviewDiff(data);
        actions.paintReviewNotes();
        actions.updateReviewControls();
    }
    function paintReviewDiff(data) {
        ctx.reviewDiffData=data;
        const target = $ ('reviewDiff');
        target.replaceChildren();
        renderFileExplanation(target,data,ui,actions.requestReviewExplanations);
        const facts=document.createElement('details'),factTitle=document.createElement('summary');factTitle.textContent='来源依据';facts.append(factTitle);const provenance=document.createElement('p');provenance.textContent=data.file.originEvidence;facts.append(provenance);target.append(facts);
        if(['执行','待审批'].includes(ctx.currentReview.state)) {
            const correct=document.createElement('button');correct.textContent='更正来源归属';correct.onclick=()=>{
                if(ctx.reviewNoteDirty){toast('请先保存当前文件意见');return;}
                const latest=[...ctx.currentReview.files,...(ctx.currentReview.candidates || [])].find(f=>f.path===data.file.path);
                if(latest?.fingerprint!==data.file.fingerprint){toast('文件已变化，请先查看最新差异再确认来源');return;}
                drawer('确认文件来源',`<p>${esc(data.file.path)}</p><label>归属<select id="correctOrigin"><option value="agent">Agent</option><option value="human">人工（不参与审批）</option><option value="unknown">待确认</option></select></label><label>确认依据<input id="correctOriginReason" placeholder="说明实际来源"></label><button id="correctOriginSave">保存归属</button>`);
                $('correctOrigin').value=data.file.origin;
                $('correctOriginSave').onclick=()=>actions.reviewMutation(async()=>{await saveOrigins([{path:data.file.path,fingerprint:data.file.fingerprint}],$('correctOrigin').value,$('correctOriginReason').value);$('drawer').close();}).catch(e=>toast(e.message));
            };target.append(correct);
        }
        if (ctx.currentReview.selectionEvidence?.[data.file.path]) {
            const evidence = document.createElement('p');
            evidence.textContent = '收录依据：' + ctx.currentReview.selectionEvidence[data.file.path];
            target.append(evidence);
        }
        if (!data.text) {
            return;
        }
        const fragment = document.createDocumentFragment();
        for (const line of data.lines) {
            const row = document.createElement('div');
            row.className = 'diff-line ' + line.kind;
            for (const[side, number] of[['before', line.before], ['after', line.after]]) {
                const cell = document.createElement('button');
                cell.className = 'number';
                cell.textContent = number ?? '';
                cell.disabled = number == null;
                cell.title = number == null ? '': (side === 'before' ? '变更前': '变更后') + '第 ' + number + ' 行，添加意见';
                cell.onclick = () => {
                    ctx.noteAnchor = { side, line: number };
                    $ ('noteLocation').textContent = (side === 'before' ? '变更前': '变更后') + '第 ' + number + ' 行';
                    $ ('reviewNote').focus();
                };
                row.append(cell);
            }
            const text = document.createElement('span');
            text.className = 'text';
            text.textContent = line.text;
            row.append(text);
            fragment.append(row);
        }
        const details=document.createElement('details'),label=document.createElement('summary');label.textContent='代码差异与行意见';details.open=true;details.append(label,fragment);target.append(details);
    }
    function paintReviewNotes() {
        $ ('reviewNotes').innerHTML = (ctx.reviewFile?.notes || []).map((n, i) => `<div class="review-note"><small>${n.line?(n.side==='before'?'变更前':'变更后')+'第 '+n.line+' 行':'文件整体'}</small><p>${esc(n.text)}</p>${ctx.currentReview.state==='待审批'&&ctx.reviewFile?.origin==='agent'?`<button data-remove-note="${i}">移除意见</button>`:''}</div>`).join('');
        document.querySelectorAll('[data-remove-note]').forEach(b => b.onclick = () => {
            if (ctx.reviewSaving) return;
            ctx.reviewFile.notes.splice(Number(b.dataset.removeNote), 1);
            ctx.reviewNoteDirty = true;
            ctx.reviewDraftGeneration ++;
            actions.paintReviewNotes();
            actions.syncEditorDirty();
        });
    }
    function updateReviewControls() {
        const task = ctx.currentReview, idle = !ctx.snapshot.busy && !ctx.reviewSaving;
        $ ('reviewSummary').readOnly = ctx.reviewSaving || !task || ['已通过', '待再执行'].includes(task.state);
        $ ('reviewFeedback').readOnly = ctx.reviewSaving || task?.state !== '待审批';
        $ ('reviewNote').readOnly = ctx.reviewSaving || task?.state !== '待审批' || ctx.reviewFile?.origin !== 'agent';
        $ ('refreshReviews').disabled = ctx.reviewSaving;
        actions.updateReviewGeneration();
        $ ('startExternalReview').disabled = ctx.reviewSaving || !!ctx.snapshot.busy;
        $ ('saveReviewSummary').disabled = !idle || !task || !['执行', '待审批'].includes(task.state) || !$ ('reviewSummary').value.trim() || !ctx.reviewSummaryDirty;
        for (const id of['approveTask', 'returnTask']) $ (id).disabled = !idle || !task || task.state !== '待审批';
        const missing=task?.files?.filter(f=>!f.explanation || f.explanation.fingerprint!==f.fingerprint) || [];
        if(task?.candidates?.some(f=>f.origin==='unknown') || missing.length) $('approveTask').disabled=true;
        $('approveTask').title=missing.length?`${missing.length}个Agent文件缺少当前版本操作说明`:'';
        $('reviewOrigins').querySelectorAll('button,input').forEach(e=>e.disabled=!idle || !['执行','待审批'].includes(task?.state));
        for (const id of['approveFile', 'rejectFile', 'saveFileNotes']) $ (id).disabled = !idle || !ctx.reviewFile || ctx.reviewFile.origin !== 'agent' || task?.state !== '待审批' || !task.files.some(f=>f.path===ctx.reviewFile.path && f.fingerprint===ctx.reviewFile.fingerprint);
        if(ctx.reviewFile && (!ctx.reviewFile.explanation || ctx.reviewFile.explanation.fingerprint!==ctx.reviewFile.fingerprint)) $('approveFile').disabled=true;
        $ ('resumeReview').disabled = !idle || task?.state !== '待再执行';
        $ ('executionPlan').disabled = !!ctx.snapshot.busy || !!ctx.taskForChat;
        $ ('executionHint').textContent = ctx.taskForChat ? '继续审批退回的任务（原开始基线保留）': '项目写入前请选择计划；参考资料仍可不选或多选。';
    }
    function draftNotes() {
        const notes = [... (ctx.reviewFile?.notes || [])], text = $ ('reviewNote').value.trim();
        if (text) notes.push({ text, ... (ctx.noteAnchor || { }) });
        return notes;
    }
    async function saveFileDecision(accept) {
        if (!ctx.currentReview || !ctx.reviewFile) return;
        const id = ctx.currentReview.id, path = ctx.reviewFile.path;
        const summary = $ ('reviewSummary').value, feedback = $ ('reviewFeedback').value, summaryDirty = ctx.reviewSummaryDirty, feedbackDirty = ctx.reviewFeedbackDirty;
        let saved = false;
        try {
            await api('review/file', { id, revision: ctx.currentReview.revision, path, fingerprint: ctx.reviewFile.fingerprint, accept, notes: actions.draftNotes() });
            saved = true;
            await actions.openReview(id, true);
            await actions.openReviewFile(ctx.currentReview.files.find(f => f.path === path));
            await actions.refreshReviews();
        } catch (e) {
            if (saved) throw new Error('文件意见已保存，但重新读取失败，请刷新任务：' + e.message);
            throw e;
        } finally {
            $ ('reviewSummary').value = summary;
            $ ('reviewFeedback').value = feedback;
            ctx.reviewSummaryDirty = summaryDirty;
            ctx.reviewFeedbackDirty = feedbackDirty;
            actions.syncEditorDirty();
        }
    }
    action('startExternalReview', async () => {
        drawer('开始记录任务', `<p>必须在任务执行前记录基线。只统计此后 Assets、Packages、ProjectSettings 的变化；已有改动保留。外部 IDE/Unity 同期改动先列来源待确认，人工提供资源不进入审批。</p><label>执行计划<select id="externalPlan">${ctx.docs.filter(d=>d.type==='plan'&&d.status!=='关闭').map(d=>`<option value="${esc(d.id)}">${esc(d.title)} · ${esc(d.status)}</option>`).join('')}</select></label><button id="beginExternalReview" class="primary">记录开始基线</button>`);
        $ ('beginExternalReview').onclick = async () => {
            const b = $ ('beginExternalReview');
            b.disabled = true;
            try {
                const task = await api('review/start', { planId: $ ('externalPlan').value });
                $ ('drawer').close();
                await actions.refreshReviews();
                await actions.openReview(task.id, true);
            } catch (e) {
                toast(e.message);
                b.disabled = false;
            }
        };
    });
    action('refreshReviews', async () => {
        await actions.refreshReviews();
        if (ctx.currentReview) await actions.openReview(ctx.currentReview.id);
        else if (ctx.reviewTasks.length) await actions.openReview(ctx.reviewTasks[0].id);
    });
    action('saveReviewSummary', () => actions.reviewMutation(async () => {
        if (ctx.reviewNoteDirty) throw new Error('请先保存当前文件意见');
        const feedback = $ ('reviewFeedback').value, feedbackDirty = ctx.reviewFeedbackDirty;
        const task = await api('review/summary', { id: ctx.currentReview.id, revision: ctx.currentReview.revision, summary: $ ('reviewSummary').value });
        await actions.openReview(task.id, true);
        $ ('reviewFeedback').value = feedback;
        ctx.reviewFeedbackDirty = feedbackDirty;
        await actions.refreshReviews();
        actions.syncEditorDirty();
        toast('任务摘要已保存；文件差异由后台自动同步。');
    }));
    action('approveFile', () => actions.reviewMutation(() => actions.saveFileDecision(true)));
    action('rejectFile', () => actions.reviewMutation(() => actions.saveFileDecision(false)));
    action('saveFileNotes', () => actions.reviewMutation(() => actions.saveFileDecision(null)));
    action('approveTask', () => actions.reviewMutation(async () => {
        if (ctx.reviewNoteDirty || ctx.reviewSummaryDirty) throw new Error('请先保存意见或提交最新摘要');
        if (!confirm('批准此次任务中确认属于Agent的文件变化？已有不通过的文件必须先处理；人工文件不参与审批。本操作不自动关闭计划。')) return;
        const task = await api('review/decision', { id: ctx.currentReview.id, revision: ctx.currentReview.revision, accept: true, feedback: $ ('reviewFeedback').value });
        await actions.openReview(task.id, true);
        await actions.refreshReviews();
        toast('此次任务整体批准通过。');
    }));
    action('returnTask', () => actions.reviewMutation(async () => {
        if (ctx.reviewNoteDirty || ctx.reviewSummaryDirty) throw new Error('请先保存文件意见或提交最新摘要');
        const task = await api('review/decision', { id: ctx.currentReview.id, revision: ctx.currentReview.revision, accept: false, feedback: $ ('reviewFeedback').value });
        await actions.openReview(task.id, true);
        await actions.refreshReviews();
        toast('任务已退回；点击「返回再执行」将意见带回 AI 对话。');
    }));
    action('resumeReview', async () => {
        const task = ctx.currentReview;
        if (task.threadId) {
            if (!await actions.loadChat(task.threadId)) return;
        } else {
            if (!actions.navigate('chat')) return;
            ctx.threadId = null;
            ctx.lastMessage.clear();
            ctx.toolMessages.clear();
            $ ('messages').innerHTML = '<div class="empty">外部任务返回执行 · 将建立新的 Codex 对话</div>';
        }
        ctx.taskForChat = task.id;
        actions.setWorkflowMode('execute-task');
        $ ('executionPlan').value = task.planId;
        $ ('access').value = 'workspace-write';
        $ ('prompt').value = task.feedbackPrompt + '\n\n请根据以上意见修正，保持已通过且内容未变的文件，完成后总结变化及真实验证结果。';
        $ ('prompt').focus();
        actions.updateReviewControls();
    });
    $ ('reviewSummary').oninput = () => {
        ctx.reviewSummaryDirty = true;
        ctx.reviewDraftGeneration ++;
        actions.updateReviewControls();
        actions.syncEditorDirty();
    };
    $ ('reviewNote').oninput = () => {
        ctx.reviewNoteDirty = true;
        ctx.reviewDraftGeneration ++;
        actions.syncEditorDirty();
    };
    $ ('reviewFeedback').oninput = () => {
        ctx.reviewFeedbackDirty = true;
        ctx.reviewDraftGeneration ++;
        actions.syncEditorDirty();
    };
    $ ('reviewSearch').oninput = (... args) => actions.renderReviewFiles(... args);
    $ ('executionPlan').onchange = () => {
        ctx.taskForChat = null;
        actions.updateReviewControls();
    };
}
