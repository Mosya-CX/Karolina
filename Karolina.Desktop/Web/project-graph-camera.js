export const dot = (a, b) => a.x * b.x + a.y * b.y + a.z * b.z;
export function cameraFrame(camera) {
    const cy = Math.cos(camera.yaw), sy = Math.sin(camera.yaw), cp = Math.cos(camera.pitch), sp = Math.sin(camera.pitch);
    return { right: { x: cy, y: 0, z: -sy }, up: { x: -sy * sp, y: cp, z: -cy * sp }, back: { x: sy * cp, y: sp, z: cy * cp } };
}
export function projectPoint(point, camera, width, height) {
    const frame = cameraFrame(camera), p = { x: point.x - camera.target.x, y: point.y - camera.target.y, z: point.z - camera.target.z };
    const depth = camera.distance - dot(p, frame.back), focal = height / (2 * Math.tan(Math.PI / 8));
    return { x: width / 2 + dot(p, frame.right) * focal / depth, y: height / 2 - dot(p, frame.up) * focal / depth, depth, focal, visible: depth > 1 && depth < (camera.far ?? Infinity) };
}
export function fitCamera(camera, radius, width, height) {
    const halfAngle = Math.atan(Math.tan(Math.PI / 8) * Math.min(1, width / Math.max(1, height)));
    camera.distance = Math.max(100, radius / Math.sin(halfAngle) * 1.08); camera.target = { x: 0, y: 0, z: 0 };
}
export function panCamera(camera, dx, dy, height) {
    const { right, up } = cameraFrame(camera), factor = camera.distance * 2 * Math.tan(Math.PI / 8) / Math.max(1, height);
    for (const axis of ['x', 'y', 'z']) camera.target[axis] += (-dx * right[axis] + dy * up[axis]) * factor;
}
export function rotateCamera(camera, dx, dy) {
    camera.yaw += dx * .008; camera.pitch = Math.max(-Math.PI / 2 + .02, Math.min(Math.PI / 2 - .02, camera.pitch + dy * .008));
}
export function zoomCamera(camera, factor, radius) { camera.distance = Math.max(25, Math.min(radius * 40 + 100, camera.distance / factor)); }

// 点精灵重叠时选择最靠前的可见节点，避免点中被遮挡的后方节点。
export function pickNode(projected, x, y) {
    let best = null;
    for (const p of projected) {
        if (!p.visible || Math.hypot(p.x - x, p.y - y) > Math.max(5, p.size / 2)) continue;
        if (!best || p.depth < best.depth) best = p;
    }
    return best?.id || null;
}
