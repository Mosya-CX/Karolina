import { effortLabel } from '../core/model-policy.js';
/** Pure progress text; elapsed time comes from the caller's clock. */
export function explanationStatus(job, now) {
    const end = job.active ? now : new Date(job.updated).getTime();
    const elapsed = Math.max(0,Math.floor((end-new Date(job.started).getTime())/1000));
    return `${job.phase} · ${job.model} / ${effortLabel(job.effort)} · ${job.phase === '已中断' ? '最后记录在第' : '耗时'}${elapsed}秒\n${job.activity} · 目标${job.targetFiles}个文件 · 已保存${job.savedFiles}个`
        + (job.receivedCharacters ? ` · 已接收${job.receivedCharacters}字符` : '') + (job.error ? '\n'+job.error : '');
}
