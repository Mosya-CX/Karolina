export function matchesReference(doc, query, tag = '') {
    const tags=doc.tags || [], text=[doc.title,doc.code,doc.id,...tags].join(' ').toLocaleLowerCase();
    if(tag && !tags.includes(tag)) return false;
    const partial=(haystack,needle)=>{if(haystack.includes(needle))return true;let index=0;for(const c of haystack){if(c===needle[index])index++;if(index===needle.length)return true;}return false;};
    return query.trim().toLocaleLowerCase().split(/\s+/).filter(Boolean).every(token=>token.startsWith('#')?tags.some(t=>partial(t.toLocaleLowerCase(),token.slice(1))):partial(text,token));
}
export function resourceKind(path) {
    if(/\.(cs|js|ts|shader|hlsl|compute)$/i.test(path)) return '代码';
    if(path.startsWith('Docs/')) return '文档';
    if(path.startsWith('ProjectSettings/') || path.startsWith('Packages/')) return '配置';
    return '资源';
}
