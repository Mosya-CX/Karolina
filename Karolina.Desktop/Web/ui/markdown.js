import { esc } from './primitives.js';
/** Markdown never accepts raw HTML. Document link resolution is injected. */
export function createMarkdown(getDocuments = () => []) {
    function inline(raw) {
        const tokens = [];
        const hold = html => {
            tokens.push(html);
            return `\u0001${tokens.length-1}\u0001`;
        };
        let s = esc(raw).replace(/`([^`]+)`/g, (_, code) => hold(`<code>${code}</code>`));
        s = s.replace(/\[([^\]]+)\]\(([^)\n]+)\)/g, (_, title, href) => {
            let decoded = href.trim().replace(/^&lt;|&gt;$/g, '').replace(/&amp;/g, '&');
            try {
                decoded = decodeURIComponent(decoded);
            } catch {
            }
            if (/^https?:\/\//i.test(decoded)) return hold(`<a href="${esc(decoded)}" target="_blank" rel="noopener noreferrer">${title}</a>`);
            if (decoded.startsWith('#')) return hold(`<a href="${esc(decoded)}">${title}</a>`);
            let entry = getDocuments().find(d => d.path === decoded || d.path.endsWith('/' + decoded.replace(/^(?:\.\.\/)+/, '')) || d.title === title);
            return entry ? hold(`<a href="#document:${encodeURIComponent(entry.id)}" data-document="${esc(entry.id)}">${title}</a>`): `${title} <code>${esc(decoded)}</code>`;
        });
        s = s.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>').replace(/~~([^~]+)~~/g, '<del>$1</del>').replace(/(?<!\*)\*([^*]+)\*(?!\*)/g, '<em>$1</em>');
        return s.replace(/\u0001(\d+)\u0001/g, (_, n) => tokens[Number(n)]);
    }
    function markdown(text) {
        const lines = String(text).replace(/\r\n/g, '\n').split('\n'), out = [];
        let i = 0, heading = 0;
        const cells = l => l.trim().replace(/^\|/, '').replace(/\|$/, '').split(/(?<!\\)\|/).map(x => x.trim());
        const block = l => /^(#{1,6}\s|\s*[-*+]\s|\s*\d+[.)]\s|>|```|~~~|\$\$|\s*$|---+$)/.test(l);
        while (i < lines.length) {
            let l = lines[i];
            if (!l.trim()) {
                i ++;
                continue;
            }
            const fence = l.match(/^(```+|~~~+)(.*)$/);
            if (fence) {
                const code = [];
                i ++;
                while (i < lines.length && !lines[i].startsWith(fence[1])) code.push(lines[i ++]);
                i ++;
                out.push(`<pre><code>${esc(code.join('\n'))}</code></pre>`);
                continue;
            }
            if (l.trim() === '$$') {
                const math = [];
                i ++;
                while (i < lines.length && lines[i].trim() !== '$$') math.push(lines[i ++]);
                i ++;
                out.push(`<div class="math">${esc(math.join('\n'))}</div>`);
                continue;
            }
            let h = l.match(/^(#{1,6})\s+(.+?)\s*#*$/);
            if (h) {
                let n = h[1].length;
                out.push(`<h${n} id="heading-${heading++}">${inline(h[2])}</h${n}>`);
                i ++;
                continue;
            }
            if (/^\s*(---+|\*\*\*+)\s*$/.test(l)) {
                out.push('<hr>');
                i ++;
                continue;
            }
            if (i + 1 < lines.length && l.includes('|') && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1])) {
                const headers = cells(l);
                out.push('<table><thead><tr>' + headers.map(c => `<th>${inline(c)}</th>`).join('') + '</tr></thead><tbody>');
                i += 2;
                while (i < lines.length && lines[i].includes('|') && lines[i].trim()) {
                    out.push('<tr>' + cells(lines[i ++]).map(c => `<td>${inline(c)}</td>`).join('') + '</tr>');
                }
                out.push('</tbody></table>');
                continue;
            }
            if (/^>/.test(l)) {
                const quote = [];
                while (i < lines.length && /^>/.test(lines[i])) quote.push(lines[i ++].replace(/^>\s?/, ''));
                out.push('<blockquote>' + markdown(quote.join('\n')) + '</blockquote>');
                continue;
            }
            if (/^\s*([-*+]\s|\d+[.)]\s)/.test(l)) {
                const ordered = /^\s*\d/.test(l), tag = ordered ? 'ol': 'ul';
                out.push('<' + tag + '>');
                while (i < lines.length && /^\s*([-*+]\s|\d+[.)]\s)/.test(lines[i])) {
                    let content = lines[i ++].replace(/^\s*([-*+]\s|\d+[.)]\s)/, '');
                    let check = content.match(/^\[([ xX])\]\s/);
                    if (check) content = content.slice(4);
                    out.push('<li>' + (check ? `<input type="checkbox" disabled ${check[1]!==' '?'checked':''}> `: '') + inline(content) + '</li>');
                }
                out.push('</' + tag + '>');
                continue;
            }
            const paragraph = [l];
            i ++;
            while (i < lines.length && !block(lines[i]) && !(i + 1 < lines.length && lines[i].includes('|') && /^\s*\|?\s*:?-{3,}/.test(lines[i + 1]))) paragraph.push(lines[i ++]);
            out.push('<p>' + paragraph.map(inline).join('<br>') + '</p>');
        }
        return out.join('\n');
    }
    return markdown;
}
