/** Pure policies; only inputs from the provider are eligible selections. */
const names = Object.freeze({ none:'无',minimal:'极低',low:'低',medium:'中',high:'高',xhigh:'极高',max:'最高',ultra:'超高' });
export function effortLabel(effort) { return names[effort] || effort; }
export function selectReviewModel(models, preferred) {
    return models.find(m => m.model === preferred)?.model
        || models.find(m => /(?:^|[-_])(luna|nano|mini)(?:$|[-_])/i.test(m.model))?.model || '';
}
export function selectReviewEffort(model, preferred) {
    const supported = model?.supportedReasoningEfforts || [];
    return supported.find(e => e.reasoningEffort === preferred)?.reasoningEffort
        || ['low','minimal','none'].find(e => supported.some(s => s.reasoningEffort === e)) || '';
}
export function selectDefaultModel(models, preferred = '') {
    return models.find(m => m.model === preferred)?.model
        || models.find(m => /(?:^|[-_])(luna|nano|mini|small)(?:$|[-_])/i.test(m.model))?.model
        || '';
}
export function selectDefaultEffort(model, preferred = '') {
    const supported = model?.supportedReasoningEfforts || [];
    return supported.find(e => e.reasoningEffort === preferred)?.reasoningEffort
        || ['low','minimal','none'].find(e => supported.some(s => s.reasoningEffort === e))
        || model?.defaultReasoningEffort
        || supported[0]?.reasoningEffort
        || '';
}
