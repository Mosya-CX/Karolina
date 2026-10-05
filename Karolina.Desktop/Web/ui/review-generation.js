import { explanationStatus } from './review-progress.js';
import { effortLabel, selectReviewModel, selectReviewEffort } from '../core/model-policy.js';
/** Approval explanation settings are independent of the chat composer. */
export function registerReviewGeneration({ state: ctx, actions, ui, api }) {
    const { $, toast } = ui;
    let signature = '', starting = false;
    const preferenceKey = () => 'karolina-review-model:' + ctx.snapshot.root;
    function preference() { try { return JSON.parse(localStorage.getItem(preferenceKey()) || '{}'); } catch { return {}; } }
    function remember() { localStorage.setItem(preferenceKey(), JSON.stringify({model:$('reviewModel').value,effort:$('reviewEffort').value})); }
    function efforts(preferred = null) {
        const model = ctx.snapshot.models?.find(m => m.model === $('reviewModel').value);
        const supported = model?.supportedReasoningEfforts || [];
        $('reviewEffort').replaceChildren(new Option('选择档位', ''));
        for (const item of supported) $('reviewEffort').add(new Option(effortLabel(item.reasoningEffort),item.reasoningEffort));
        $('reviewEffort').value = selectReviewEffort(model, preferred);
    }
    function updateReviewGeneration() {
        const models = ctx.snapshot.models || [], next = JSON.stringify([ctx.snapshot.root,models]);
        if (signature !== next) {
            signature = next;
            const previous = $('reviewModel').value || preference().model;
            const previousEffort = $('reviewEffort').value || preference().effort;
            $('reviewModel').replaceChildren(new Option('请选择模型', ''));
            for (const model of models) $('reviewModel').add(new Option(model.displayName || model.model,model.model));
            $('reviewModel').value = selectReviewModel(models, previous);
            efforts(models.some(m => m.model === previous) ? previousEffort : null);
        }
        const job = ctx.snapshot.reviewExplanations?.find(j => j.reviewId === ctx.currentReview?.id);
        const disabled = starting || !!ctx.snapshot.busy || !!ctx.reviewSaving;
        $('reviewModel').disabled = disabled || !models.length;
        $('reviewEffort').disabled = disabled || !$('reviewModel').value;
        $('explainReview').disabled = disabled || !ctx.currentReview || !['执行','待审批'].includes(ctx.currentReview.state) || !ctx.snapshot.codex?.connected || !$('reviewModel').value || !$('reviewEffort').value;
        $('stopReviewExplanation').hidden = !job?.active;
        $('stopReviewExplanation').disabled = !job?.active || !ctx.snapshot.activeTurn;
        const status = $('reviewExplanationStatus');
        if (starting && !job?.active) status.textContent = '正在提交说明请求…';
        else if (job) status.textContent = explanationStatus(job, Date.now());
        else status.textContent = !models.length ? '连接Codex后可选择说明模型。' : !$('reviewModel').value ? '没有识别到低档模型，请手动选择；不会自动使用高级模型。' : '默认优先mini/nano/luna系列与低档位；可独立调整，不影响AI对话设置。';
    }
    async function requestReviewExplanations() {
        if (starting) return;
        try {
            if (!ctx.currentReview) throw new Error('请选择任务');
            if (!ctx.snapshot.codex?.connected) throw new Error('请先连接Codex');
            if (ctx.reviewSaving || ctx.snapshot.busy) throw new Error('请等待当前操作结束');
            if (!$('reviewModel').value || !$('reviewEffort').value) throw new Error('请在审批页选择模型和档位');
            starting = true; updateReviewGeneration();
            await api('review/explain',{id:ctx.currentReview.id,revision:ctx.currentReview.revision,model:$('reviewModel').value,effort:$('reviewEffort').value});
            await actions.refreshState();
            toast('说明生成已启动，进度显示在审批页。');
        } catch (e) { await actions.refreshState().catch(()=>{});toast(e.message); }
        finally { starting = false; updateReviewGeneration(); }
    }
    $('reviewModel').onchange = () => { efforts();remember();updateReviewGeneration(); };
    $('reviewEffort').onchange = () => { remember();updateReviewGeneration(); };
    $('explainReview').onclick = requestReviewExplanations;
    $('stopReviewExplanation').onclick = async () => {
        const job = ctx.snapshot.reviewExplanations?.find(j => j.reviewId === ctx.currentReview?.id && j.active);
        if (!job) return;
        try { await api('review/explain/stop',{id:job.reviewId,runId:job.runId});toast('已请求停止，等待Codex真实终态。'); }
        catch(e) {toast(e.message);}
    };
    Object.assign(actions,{ updateReviewGeneration, requestReviewExplanations });
}
