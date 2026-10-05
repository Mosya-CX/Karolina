/** All authenticated calls share the same transport. Theme assets use ordinary same-origin URLs. */
export const session = document.querySelector('meta[name="karolina-session"]').content;
export async function request(path, { method = 'GET', body, contentType = 'application/json', signal, binary = false, keepalive = false } = { }) {
    const response = await fetch('/api/' + path, {
        method,
        signal,
        keepalive,
        headers: { 'X-Karolina-Session': session, ... (body === undefined ? { }: { 'Content-Type': contentType }) },
        body: body === undefined ? undefined: contentType === 'application/json' ? JSON.stringify(body): body
    });
    if (binary && response.ok) return response.blob();
    let result;
    try {
        result = await response.json();
    } catch {
        throw new Error(`服务返回 ${response.status}`);
    }
    if (!response.ok) throw new Error(result.error || `操作失败 ${response.status}`);
    return result;
}
export const api = (path, body, options = { }) => request(path, { ... options, method: body === undefined ? 'GET': 'POST', body });
