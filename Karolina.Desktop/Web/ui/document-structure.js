/** Reading projections; Markdown remains the only source of requirement evolution. */
export function buildToc(content, target) {
    target.replaceChildren();
    const toolbar=document.createElement('div');toolbar.className='toc-tools';
    const title=document.createElement('strong');title.textContent='标题目录';
    const expand=document.createElement('button'),collapse=document.createElement('button');expand.textContent='展开全部';collapse.textContent='折叠子级';expand.type=collapse.type='button';
    toolbar.append(title,expand,collapse);target.append(toolbar);
    const headings = [...content.querySelectorAll('h1,h2,h3,h4,h5,h6')];
    if (!headings.length) return;
    const top = Math.min(...headings.map(h => Number(h.tagName[1]))), stack = [];
    for (const heading of headings) {
        const level = Number(heading.tagName[1]);
        while (stack.length && stack.at(-1).level >= level) stack.pop();
        const branch = document.createElement('details'), summary = document.createElement('summary'), link = document.createElement('a'), children = document.createElement('div');
        branch.className = 'toc-branch'; branch.open = level === top; branch.dataset.level = level;
        link.href = '#' + heading.id; link.textContent = heading.textContent;
        link.onclick = event => { event.preventDefault();if(target.closest('#drawer'))document.getElementById('drawer').close(); heading.scrollIntoView({ behavior: 'smooth', block: 'start' }); target.querySelectorAll('a').forEach(a => a.classList.toggle('active', a === link)); };
        summary.append(link); children.className = 'toc-children'; branch.append(summary, children);
        (stack.at(-1)?.children || target).append(branch); stack.push({ level, children });
    }
    target.querySelectorAll('.toc-branch').forEach(branch => { if (!branch.querySelector('.toc-children').children.length) branch.classList.add('toc-leaf'); });
    expand.onclick=()=>target.querySelectorAll('.toc-branch').forEach(b=>b.open=true);
    collapse.onclick=()=>target.querySelectorAll('.toc-branch').forEach(b=>b.open=Number(b.dataset.level)===top);
}
export function evolutionEntries(source) {
    const section = source.match(/^##\s+需求演进[^\r\n]*\r?\n([\s\S]*?)(?=^##\s|(?![\s\S]))/m);
    if (!section) return [];
    const body = section[1], headings = [...body.matchAll(/^###\s+(.+)$/gm)];
    if (!headings.length) return body.trim() ? [{ title: '已有演进记录', body: body.trim() }] : [];
    const entries = headings.map((h, i) => ({ title: h[1], body: body.slice(h.index + h[0].length, headings[i + 1]?.index ?? body.length).trim() }));
    return entries.reverse();
}
export function compareReferenceCode(a, b) {
    const parts = doc => String(doc.code || doc.id).match(/\d+/g)?.map(Number) || [];
    const aa = parts(a), bb = parts(b);
    for (let i = 0; i < Math.max(aa.length, bb.length); i++) if ((aa[i] || 0) !== (bb[i] || 0)) return (bb[i] || 0) - (aa[i] || 0);
    return String(b.code || b.id).localeCompare(String(a.code || a.id), 'zh-CN', { numeric: true });
}
