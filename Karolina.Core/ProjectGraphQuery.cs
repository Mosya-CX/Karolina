namespace Karolina.Core;

public sealed record ProjectGraphSlice(ProjectGraphStatus Status, string CenterId, ProjectGraphNode[] Nodes,
    ProjectGraphEdge[] Edges, Dictionary<string, int> Distances, bool Truncated, bool DepthLimitReached, string Scope);
public sealed record ProjectGraphPath(ProjectGraphStatus Status, bool Found, ProjectGraphNode[] Nodes,
    ProjectGraphEdge[] Edges, bool Truncated, string Scope);
public sealed record ProjectGraphOverviewEdge(string From, string To, string Relation, bool Heuristic);
public sealed record ProjectGraphOverview(ProjectGraphStatus Status, ProjectGraphNode[] Nodes, ProjectGraphOverviewEdge[] Edges, bool Truncated = false);

/// <summary>不可变快照上的有界查询；UI 与 MCP 共用同一组节点和关系。</summary>
internal sealed class ProjectGraphQuery
{
    private readonly Dictionary<string, ProjectGraphNode> nodes;
    private readonly Dictionary<string, ProjectGraphEdge[]> incoming, outgoing;
    public ProjectGraphQuery(ProjectGraphSnapshot snapshot)
    {
        nodes = snapshot.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        incoming = snapshot.Edges.GroupBy(e => e.To).ToDictionary(g => g.Key, g => g.ToArray());
        outgoing = snapshot.Edges.GroupBy(e => e.From).ToDictionary(g => g.Key, g => g.ToArray());
    }
    private void Require(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 300) throw new ArgumentException("图谱节点编号无效");
        if (!nodes.ContainsKey(id)) throw new KeyNotFoundException("工程图谱节点不存在");
    }
    private IEnumerable<ProjectGraphEdge> Edges(string id, string direction, string? relation)
    {
        if (direction is not ("in" or "out" or "both" or "impact")) throw new ArgumentException("方向必须为 in、out 或 both");
        var result = Enumerable.Empty<ProjectGraphEdge>();
        if (direction != "out" && incoming.TryGetValue(id, out var ins)) result = result.Concat(ins);
        if (outgoing.TryGetValue(id, out var outs))
            result = result.Concat(direction == "impact" ? outs.Where(e => e.Relation == "声明") : direction == "in" ? [] : outs);
        return result.Where(e => string.IsNullOrWhiteSpace(relation) || e.Relation == relation);
    }
    private static string Other(ProjectGraphEdge edge, string id) => edge.From == id ? edge.To : edge.From;

    public ProjectGraphSlice Neighborhood(ProjectGraphStatus status, string id, int depth, string direction, string? relation, int take)
    {
        Require(id);
        if (depth is < 1 or > 4 || take is < 2 or > 500) throw new ArgumentException("层数范围 1–4；节点上限范围 2–500");
        var distances = new Dictionary<string, int>(StringComparer.Ordinal) { [id] = 0 };
        var queue = new Queue<string>(); queue.Enqueue(id);
        var result = new Dictionary<(string, string, string), ProjectGraphEdge>();
        bool truncated = false, depthLimit = false;
        while (queue.TryDequeue(out string? current))
        {
            foreach (var edge in Edges(current, direction, relation))
            {
                string next = Other(edge, current);
                if (!distances.ContainsKey(next))
                {
                    if (distances[current] >= depth) { depthLimit = true; continue; }
                    if (distances.Count >= take) { truncated = true; continue; }
                    distances[next] = distances[current] + 1; queue.Enqueue(next);
                }
                if (result.Count >= 1000) { truncated = true; continue; }
                result.TryAdd((edge.From, edge.Relation, edge.To), edge);
            }
        }
        return new(status, id, distances.Keys.Select(key => nodes[key]).ToArray(), result.Values.ToArray(), distances,
            truncated, depthLimit, direction == "impact" ? "沿被引用关系与文件声明扩展的可能影响范围；不包含全部运行时关联" : $"{direction} 方向，最多 {depth} 层、{take} 个节点；静态关系");
    }

    public ProjectGraphPath Path(ProjectGraphStatus status, string from, string to, string direction, int depth)
    {
        Require(from); Require(to);
        if (direction is not ("in" or "out" or "both") || depth is < 1 or > 12) throw new ArgumentException("路径方向必须为 in、out、both；层数范围 1–12");
        var visited = new Dictionary<string, int> { [from] = 0 };
        var parents = new Dictionary<string, (string Parent, ProjectGraphEdge Edge)>();
        var queue = new Queue<string>(); queue.Enqueue(from);
        bool truncated = false;
        while (queue.TryDequeue(out string? current) && !visited.ContainsKey(to))
        {
            foreach (var edge in Edges(current, direction, null))
            {
                string next = Other(edge, current);
                if (visited.ContainsKey(next)) continue;
                if (visited[current] >= depth || visited.Count >= 5000) { truncated = true; continue; }
                visited[next] = visited[current] + 1; parents[next] = (current, edge); queue.Enqueue(next);
                if (next == to) break;
            }
        }
        string scope = $"{direction} 方向，最多 {depth} 层、5000 个访问节点；未找到仅代表本次有界查询无结果";
        if (!visited.ContainsKey(to)) return new(status, false, [], [], truncated, scope);
        var pathNodes = new List<ProjectGraphNode> { nodes[to] };
        var pathEdges = new List<ProjectGraphEdge>();
        for (string current = to; current != from;)
        {
            var parent = parents[current]; pathEdges.Add(parent.Edge); current = parent.Parent; pathNodes.Add(nodes[current]);
        }
        pathNodes.Reverse(); pathEdges.Reverse();
        return new(status, true, pathNodes.ToArray(), pathEdges.ToArray(), truncated, scope);
    }
}

public sealed partial class ProjectGraphService
{
    public ProjectGraphOverview Overview()
    {
        lock (gate)
        {
            var current = snapshot ?? throw new InvalidOperationException("工程图谱尚未建立，请先建立索引");
            return new(MakeStatus(), current.Nodes, current.Edges.Select(e => new ProjectGraphOverviewEdge(e.From, e.To, e.Relation, (e.Evidence ?? []).Any(f => f.Confidence == "启发式"))).ToArray());
        }
    }
    public ProjectGraphSlice Neighborhood(string id, int depth = 1, string direction = "both", string? relation = null, int take = 80)
    {
        ProjectGraphQuery index; ProjectGraphStatus status;
        lock (gate) { index = queryIndex ?? throw new InvalidOperationException("工程图谱尚未建立，请先建立索引"); status = MakeStatus(); }
        return index.Neighborhood(status, id, depth, direction, relation, take);
    }
    public ProjectGraphSlice Impact(string id, int depth = 3, int take = 150)
    {
        ProjectGraphQuery index; ProjectGraphStatus status;
        lock (gate) { index = queryIndex ?? throw new InvalidOperationException("工程图谱尚未建立，请先建立索引"); status = MakeStatus(); }
        return index.Neighborhood(status, id, depth, "impact", null, take);
    }
    public ProjectGraphPath FindPath(string from, string to, string direction = "out", int depth = 8)
    {
        ProjectGraphQuery index; ProjectGraphStatus status;
        lock (gate) { index = queryIndex ?? throw new InvalidOperationException("工程图谱尚未建立，请先建立索引"); status = MakeStatus(); }
        return index.Path(status, from, to, direction, depth);
    }
}
