const hash = id => { let value = 2166136261; for (const char of id) value = Math.imul(value ^ char.charCodeAt(0), 16777619); return value >>> 0; };
const compare = (a, b) => hash(a) - hash(b) || (a < b ? -1 : a > b ? 1 : 0);
const weight = e => e.relation === 'Unity GUID 引用' ? 3 : e.relation === '继承/实现' ? 2 : e.heuristic || e.relation === '类型引用' ? .6 : 1;

function sphere(i, count, radius) {
    const y = 1 - 2 * (i + .5) / Math.max(1, count), angle = i * 2.3999632297, r = Math.sqrt(Math.max(0, 1 - y * y));
    return { x: Math.cos(angle) * r * radius, y: y * radius, z: Math.sin(angle) * r * radius };
}

// 只使用稳定身份及实际声明边建立几何锚点。path、title、folder 均不参与位置或分组。
function topology(graph) {
    const nodes = [...graph.nodes].sort((a, b) => compare(a.id, b.id)), indices = new Map(nodes.map((n, i) => [n.id, i]));
    const owners = nodes.map((_, i) => i);
    const find = i => { while (owners[i] !== i) { owners[i] = owners[owners[i]]; i = owners[i]; } return i; };
    const edges = [...graph.edges].sort((a, b) => compare(`${a.from}|${a.to}|${a.relation}`, `${b.from}|${b.to}|${b.relation}`));
    for (const e of edges) {
        const a = indices.get(e.from), b = indices.get(e.to);
        if (a !== undefined && b !== undefined && e.relation === '声明' && nodes[a].kind !== 'C# 类型' && nodes[b].kind === 'C# 类型') owners[find(b)] = find(a);
    }
    const groups = new Map();
    nodes.forEach((node, i) => { const owner = find(i); if (!groups.has(owner)) groups.set(owner, []); groups.get(owner).push(node); });
    const bodies = [...groups.values()].map(members => {
        members.sort((a, b) => (a.kind === 'C# 类型') - (b.kind === 'C# 类型') || compare(a.id, b.id));
        return { members, radius: Math.max(18, 20 * Math.sqrt(members.length - 1) + 8) };
    });
    const bodyIndex = new Map(); bodies.forEach((b, i) => b.members.forEach(n => bodyIndex.set(n.id, i)));
    const adjacent = bodies.map(() => new Map());
    for (const edge of edges) {
        const a = bodyIndex.get(edge.from), b = bodyIndex.get(edge.to);
        if (a === undefined || b === undefined || a === b) continue;
        const strength = weight(edge);
        adjacent[a].set(b, (adjacent[a].get(b) || 0) + strength); adjacent[b].set(a, (adjacent[b].get(a) || 0) + strength);
    }
    return { bodies, adjacent };
}

function optimize(adjacent) {
    const degree = adjacent.map(a => [...a.values()].reduce((s, w) => s + w, 0)), total = degree.reduce((s, w) => s + w, 0);
    const labels = degree.map((_, i) => i), sums = [...degree];
    if (total) for (let pass = 0; pass < 24; pass++) {
        let changed = false;
        for (let i = 0; i < adjacent.length; i++) {
            if (!degree[i]) continue;
            const previous = labels[i], votes = new Map(); sums[previous] -= degree[i];
            for (const [j, value] of adjacent[i]) if (j !== i) votes.set(labels[j], (votes.get(labels[j]) || 0) + value);
            const score = id => (votes.get(id) || 0) - 1.3 * degree[i] * sums[id] / total;
            let best = previous, bestScore = score(previous);
            for (const id of [...votes.keys()].sort((a, b) => a - b)) if (score(id) > bestScore + 1e-9) { best = id; bestScore = score(id); }
            labels[i] = best; sums[best] += degree[i]; changed ||= best !== previous;
        }
        if (!changed) break;
    }
    return labels;
}

function communities(adjacent) {
    const degree = adjacent.map(a => [...a.values()].reduce((s, w) => s + w, 0));
    let current = adjacent, labels = adjacent.map((_, i) => i);
    for (let level = 0; level < 4; level++) {
        const moved = optimize(current), ids = [...new Set(moved)].sort((a, b) => a - b), indices = new Map(ids.map((id, i) => [id, i]));
        labels = labels.map(id => indices.get(moved[id]));
        if (ids.length === current.length) break;
        const combined = ids.map(() => new Map());
        current.forEach((neighbors, a) => { for (const [b, value] of neighbors) {
            const from = indices.get(moved[a]), to = indices.get(moved[b]); combined[from].set(to, (combined[from].get(to) || 0) + value);
        } });
        current = combined;
    }
    const groups = [], seen = new Set(), isolated = [];
    for (let i = 0; i < adjacent.length; i++) {
        if (!degree[i]) { isolated.push(i); continue; }
        if (seen.has(i)) continue;
        const members = [i]; seen.add(i);
        for (let q = 0; q < members.length; q++) for (const next of adjacent[members[q]].keys()) {
            if (!seen.has(next) && labels[next] === labels[i]) { seen.add(next); members.push(next); }
        }
        groups.push(members);
    }
    return { groups, isolated, degree };
}

// 三维网格排斥和加权弹簧。在 Worker 内计算，固定迭代、固定初始化，不持续晃动节点。
function settle(bodies, edges, iterations = 160) {
    if (!bodies.length) return;
    const average = bodies.reduce((sum, b) => sum + b.radius, 0) / bodies.length;
    const initialRadius = Math.cbrt(bodies.length) * (average * 2 + 32);
    bodies.forEach((b, i) => Object.assign(b, sphere(i, bodies.length, initialRadius * (.4 + .6 * Math.cbrt((i + 1) / bodies.length)))));
    const cell = bodies.reduce((max, b) => Math.max(max, b.radius * 2 + 24), 72);
    for (let step = 0; step < iterations + 55; step++) {
        const repelOnly = step >= iterations, force = new Float64Array(bodies.length * 3), grid = new Map();
        bodies.forEach((b, i) => { const key = `${Math.floor(b.x / cell)},${Math.floor(b.y / cell)},${Math.floor(b.z / cell)}`; if (!grid.has(key)) grid.set(key, []); grid.get(key).push(i); });
        bodies.forEach((a, i) => {
            const gx = Math.floor(a.x / cell), gy = Math.floor(a.y / cell), gz = Math.floor(a.z / cell);
            for (let x = gx - 1; x <= gx + 1; x++) for (let y = gy - 1; y <= gy + 1; y++) for (let z = gz - 1; z <= gz + 1; z++) for (const j of grid.get(`${x},${y},${z}`) || []) {
                if (j <= i) continue;
                const b = bodies[j], dx = b.x - a.x, dy = b.y - a.y, dz = b.z - a.z, distance = Math.hypot(dx, dy, dz) || .01, gap = a.radius + b.radius + 20;
                if (distance >= gap) continue;
                const strength = (gap - distance) * .48 / distance;
                for (const [axis, delta] of [dx, dy, dz].entries()) { force[i * 3 + axis] -= delta * strength; force[j * 3 + axis] += delta * strength; }
            }
        });
        if (!repelOnly) for (const { a, b, weight: strength } of edges) {
            const left = bodies[a], right = bodies[b], delta = [right.x - left.x, right.y - left.y, right.z - left.z];
            const distance = Math.hypot(...delta) || .01, desired = left.radius + right.radius + 36;
            const pull = (distance - desired) / distance * .035 * Math.min(3, Math.log2(2 + strength));
            delta.forEach((value, axis) => { force[a * 3 + axis] += value * pull; force[b * 3 + axis] -= value * pull; });
        }
        bodies.forEach((b, i) => {
            if (!repelOnly) { force[i * 3] -= b.x * .001; force[i * 3 + 1] -= b.y * .001; force[i * 3 + 2] -= b.z * .001; }
            const length = Math.hypot(force[i * 3], force[i * 3 + 1], force[i * 3 + 2]), limit = Math.min(1, Math.max(10, average * .3) / (length || 1));
            b.x += force[i * 3] * limit; b.y += force[i * 3 + 1] * limit; b.z += force[i * 3 + 2] * limit;
        });
    }
}

export function layoutGraph(graph) {
    const positions = new Map(), groupById = new Map();
    if (!graph.nodes.length) return { positions, groupById, center: { x: 0, y: 0, z: 0 }, radius: 1, groups: 0, dimensions: 3, method: 'relations-3d' };
    const { bodies, adjacent } = topology(graph), { groups, isolated, degree } = communities(adjacent), ownership = new Map();
    groups.forEach((members, i) => members.forEach(id => ownership.set(id, i)));
    const clusters = groups.map(members => {
        members.sort((a, b) => degree[b] - degree[a] || a - b);
        const local = members.map(id => ({ radius: bodies[id].radius })), index = new Map(members.map((id, i) => [id, i])), edges = [];
        for (const id of members) for (const [other, strength] of adjacent[id]) if (other > id && index.has(other)) edges.push({ a: index.get(id), b: index.get(other), weight: strength });
        settle(local, edges);
        const center = local.reduce((p, b) => ({ x: p.x + b.x / local.length, y: p.y + b.y / local.length, z: p.z + b.z / local.length }), { x: 0, y: 0, z: 0 });
        local.forEach(b => { b.x -= center.x; b.y -= center.y; b.z -= center.z; });
        return { members, local, radius: local.reduce((max, b) => Math.max(max, Math.hypot(b.x, b.y, b.z) + b.radius), 20) };
    });
    const weights = new Map();
    adjacent.forEach((neighbors, a) => { for (const [b, strength] of neighbors) {
        const left = ownership.get(a), right = ownership.get(b); if (a >= b || left === right) continue;
        const key = `${Math.min(left, right)},${Math.max(left, right)}`; weights.set(key, (weights.get(key) || 0) + strength);
    } });
    const macroEdges = [...weights].map(([key, strength]) => { const [a, b] = key.split(',').map(Number); return { a, b, weight: strength }; });
    settle(clusters, macroEdges, 180);
    const place = (index, center, group) => {
        const members = bodies[index].members;
        members.forEach((node, i) => {
            const offset = i ? sphere(i - 1, members.length - 1, 20 * Math.sqrt(members.length - 1)) : { x: 0, y: 0, z: 0 };
            positions.set(node.id, { x: center.x + offset.x, y: center.y + offset.y, z: center.z + offset.z }); groupById.set(node.id, group);
        });
    };
    clusters.forEach((cluster, group) => cluster.members.forEach((index, i) => place(index, { x: cluster.x + cluster.local[i].x, y: cluster.y + cluster.local[i].y, z: cluster.z + cluster.local[i].z }, group)));
    const connectedRadius = clusters.reduce((max, b) => Math.max(max, Math.hypot(b.x, b.y, b.z) + b.radius), 0);
    const largest = isolated.reduce((max, i) => Math.max(max, bodies[i].radius), 18);
    const shellRadius = Math.max(connectedRadius + largest * 2 + 70, Math.sqrt(isolated.reduce((area, id) => area + (bodies[id].radius + 16) ** 2, 0)));
    // 无关联节点按稳定身份均匀分散到空间外围。没有目录区域，也没有伪造的连接。
    isolated.forEach((id, i) => place(id, sphere(i, isolated.length, shellRadius), -1));
    const radius = [...positions.values()].reduce((max, p) => Math.max(max, Math.hypot(p.x, p.y, p.z)), 1) + 20;
    return { positions, groupById, center: { x: 0, y: 0, z: 0 }, radius, groups: clusters.length, dimensions: 3, method: 'relations-3d' };
}
