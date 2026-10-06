using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private static async Task GraphQueryChecks(ProjectGraphService graph, ProjectContext project, string wolfPath)
    {
        string prefab = graph.Search("Wolf.prefab", "Unity资源").Nodes.Single().Id;
        string wolf = graph.Search("WolfController.cs", "代码文件").Nodes.Single().Id;
        string baseType = graph.Search("Demo.BaseController", "C# 类型").Nodes.Single().Id;
        await Case("图谱邻域、路径与影响查询携带引用依据且有界", () =>
        {
            var slice = graph.Neighborhood(prefab, 2, "out");
            Check(slice.Nodes.Any(n => n.Id == wolf) && slice.Nodes.Any(n => n.Id == baseType));
            var guid = slice.Edges.Single(e => e.Relation == "Unity GUID 引用");
            Check(guid.Evidence![0].Line == 3 && guid.Evidence[0].Basis.Contains("m_Script"));
            Check(guid.Evidence[0].Confidence == "GUID 已解析");
            var reference = slice.Edges.Single(e => e.Relation == "类型引用");
            Check(reference.Evidence![0].Line == 1 && reference.Evidence[0].Confidence == "启发式");
            Check(graph.FindPath(prefab, baseType).Found);
            Check(!graph.FindPath(baseType, prefab).Found);
            Check(graph.FindPath(baseType, prefab, "both").Found);
            Check(graph.Impact(wolf).Nodes.Any(n => n.Id == prefab));
            Check(graph.Neighborhood(prefab, 2, take: 2).Truncated);
            Check(graph.Neighborhood(prefab, 1, "out").DepthLimitReached);
            Check(graph.Neighborhood(prefab, relation: "声明").Nodes.Length == 1);
            Throws(() => graph.Neighborhood("missing"));
            Throws(() => graph.Neighborhood(prefab, 99));
            Throws(() => graph.Neighborhood(prefab, direction: "unsafe"));
            Throws(() => graph.FindPath(prefab, baseType, "unsafe"));
            return Task.CompletedTask;
        });
        await Case("只读图谱工具目录、参数与错误回执", () =>
        {
            var tools = new ProjectGraphTools(graph);
            Check(tools.Definitions.GetArrayLength() == 5);
            foreach (var d in tools.Definitions.EnumerateArray()) Check(d.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
            JsonElement Call(string name, string json) => JsonSerializer.SerializeToElement(tools.Call(name, JsonSerializer.Deserialize<JsonElement>(json)));
            var result = Call("graph_neighbors", JsonSerializer.Serialize(new { id = prefab, depth = 2 }));
            Check(!result.GetProperty("isError").GetBoolean());
            var payload = JsonSerializer.Deserialize<JsonElement>(result.GetProperty("content")[0].GetProperty("text").GetString()!);
            Check(payload.GetProperty("nodes").GetArrayLength() > 1);
            foreach (string invalid in new[] { "{}", "[]", "{\"id\":false}", "{\"id\":\"missing\"}", "{\"id\":\"x\",\"depth\":5}", "{\"id\":\"x\",\"take\":\"10\"}", "{\"id\":\"x\",\"write\":true}" })
                Check(Call("graph_neighbors", invalid).GetProperty("isError").GetBoolean());
            Check(Call("delete_file", "{}").GetProperty("isError").GetBoolean());
            Check(!Call("graph_status", "{}").GetProperty("isError").GetBoolean());
            return Task.CompletedTask;
        });
        await Case("循环、深度和边容量限制不伪装完整结果", () =>
        {
            using var fixture = new Fixture();
            var boundedProject = new ProjectContext(fixture.Root);
            var nodes = Enumerable.Range(0, 46).Select(i => new ProjectGraphNode("file:Assets/" + i + ".asset", "Unity资源", i + ".asset", "Assets/" + i + ".asset")).ToArray();
            var edges = nodes.SelectMany(from => nodes.Where(to => to.Id != from.Id).Select(to => new ProjectGraphEdge(from.Id, "Unity GUID 引用", to.Id))).ToArray();
            var snapshot = new ProjectGraphSnapshot(2, boundedProject.Root, DateTimeOffset.UtcNow, 46, 0, nodes.Length, edges.Length, 0, [], nodes, edges, []);
            File.WriteAllText(boundedProject.StatePath("project-graph.json"), JsonSerializer.Serialize(snapshot, DocumentLibrary.Json));
            using var dense = new ProjectGraphService(boundedProject);
            var overview = dense.Overview();
            Check(overview.Nodes.Length == 46 && overview.Edges.Length == 2070 && !overview.Truncated);
            var slice = dense.Neighborhood(nodes[0].Id, 4);
            Check(slice.Nodes.Length == 46 && slice.Edges.Length == 1000 && slice.Truncated);
            Check(slice.Nodes.Select(n => n.Id).Distinct().Count() == slice.Nodes.Length);
            Check(dense.FindPath(nodes[0].Id, nodes[0].Id).Nodes.Length == 1);
            Check(dense.ReferenceContext([nodes[0].Id, nodes[1].Id, nodes[2].Id, nodes[3].Id]).Contains("关联超过300条"));
            var pathNodes = nodes.Take(4).ToArray();
            var pathEdges = Enumerable.Range(0, 3).Select(i => new ProjectGraphEdge(nodes[i].Id, "Unity GUID 引用", nodes[i + 1].Id)).ToArray();
            File.WriteAllText(boundedProject.StatePath("project-graph.json"), JsonSerializer.Serialize(snapshot with { Nodes = pathNodes, NodesCount = 4, Edges = pathEdges, EdgesCount = 3 }, DocumentLibrary.Json));
            using var chain = new ProjectGraphService(boundedProject);
            Check(!chain.FindPath(nodes[0].Id, nodes[3].Id, depth: 1).Found);
            Check(chain.FindPath(nodes[0].Id, nodes[3].Id, depth: 1).Truncated);
            Check(chain.FindPath(nodes[0].Id, nodes[3].Id, depth: 3).Edges.Length == 3);
            return Task.CompletedTask;
        });
        await Case("工程全量覆盖保留孤立文件，排除生成缓存并区分非C#代码", async () =>
        {
            using var fixture = new Fixture();
            var allProject = new ProjectContext(fixture.Root);
            void Write(string path, string text)
            { string full = Path.Combine(fixture.Root, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!); File.WriteAllText(full, text); }
            Write("Assets/Loose.glb", "binary-placeholder");
            Write("Assets/Loose.glb.meta", "guid: 66666666666666666666666666666666");
            Write("Tools/Worker/Worker.cs", "namespace Tools; public class Worker { }");
            Write("Tools/Worker/Worker.csproj", "<Project />");
            Write("bootstrap.lua", "class BogusLuaType {} -- not C#");
            Write("Docs/requirements/Example.md", "# 工程文档");
            Write(".agents/skills/Custom/SKILL.md", "# 工程工具");
            Write("Library/Generated.cs", "class Cache {}");
            Write(".codex/private.md", "private");
            Write("Tools/bin/cache.dll", "generated");
            Write("Generated.csproj", "<Project />");
            using var all = new ProjectGraphService(allProject);
            all.StartIndex(); await WaitForIndex(all);
            var overview = all.Overview();
            Check(overview.Nodes.Any(n => n.Path == "Assets/Loose.glb"));
            Check(overview.Nodes.Any(n => n.Path == "bootstrap.lua" && n.Kind == "代码文件"));
            Check(!overview.Nodes.Any(n => n.Kind == "C# 类型" && n.Path == "bootstrap.lua"));
            Check(overview.Nodes.Any(n => n.Path == "Tools/Worker/Worker.csproj"));
            Check(overview.Nodes.Any(n => n.Path == ".agents/skills/Custom/SKILL.md"));
            Check(!overview.Nodes.Any(n => n.Path.StartsWith("Library/") || n.Path.StartsWith(".codex/") || n.Path.StartsWith("Tools/bin/") || n.Path == "Generated.csproj" || n.Path.EndsWith(".meta")));
            Check(overview.Status.Coverage!.IsolatedNodes > 0);
            Check(overview.Status.Coverage.FileNodes == overview.Nodes.Count(n => n.Kind != "C# 类型"));
            Check(!overview.Truncated && overview.Nodes.Length == overview.Status.Nodes);
        });
        await Case("顶层目录删除再创建仍能观察其内部文件变动", async () =>
        {
            using var fixture = new Fixture();
            string folder = Path.Combine(fixture.Root, "Tools");
            Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "Old.cs"), "class Old {}");
            using var watched = new ProjectGraphService(new ProjectContext(fixture.Root));
            watched.StartIndex(); await WaitForIndex(watched);
            Directory.Delete(folder, true); await WaitUntil(() => watched.Status().IsStale);
            // 等目录删除通知处理完再创建；覆盖 FileSystemWatcher 的目录生命周期。
            await Task.Delay(200);
            Directory.CreateDirectory(Path.Combine(folder, "Nested"));
            string file = Path.Combine(folder, "Nested", "New.cs"); File.WriteAllText(file, "class New {}");
            await Task.Delay(200);
            watched.StartIndex(); await WaitForIndex(watched); Check(!watched.Status().IsStale);
            File.AppendAllText(file, "\n// changed\n"); await WaitUntil(() => watched.Status().IsStale);
        });
        await Case("文件与meta变动标记过期，重建与重启核对恢复", async () =>
        {
            Check(!graph.Status().IsStale, "新建索引应有效：" + graph.Status().StaleReason);
            File.AppendAllText(wolfPath, "\n// changed\n");
            await WaitUntil(() => graph.Status().IsStale);
            graph.StartIndex(); await WaitForIndex(graph); Check(!graph.Status().IsStale);
            using (var restored = new ProjectGraphService(project))
            { await WaitUntil(() => !restored.Status().IsStale); }
            File.AppendAllText(wolfPath + ".meta", "\n# modified\n");
            await WaitUntil(() => graph.Status().IsStale);
            graph.StartIndex(); await WaitForIndex(graph); Check(!graph.Status().IsStale);
            string saved = File.ReadAllText(project.StatePath("project-graph.json"));
            File.WriteAllText(project.StatePath("project-graph.json"), "{broken");
            using (var broken = new ProjectGraphService(project)) Check(broken.Status().Error != null && !broken.Status().Indexed);
            Check(File.ReadAllText(project.StatePath("project-graph.json")) == "{broken");
            File.WriteAllText(project.StatePath("project-graph.json"), saved);
            var old = JsonSerializer.Deserialize<ProjectGraphSnapshot>(saved, DocumentLibrary.Json)!;
            File.WriteAllText(project.StatePath("project-graph.json"), JsonSerializer.Serialize(old with { SchemaVersion = 1, Files = null }, DocumentLibrary.Json));
            using (var legacy = new ProjectGraphService(project))
            { Check(legacy.Status().Indexed); Check(legacy.Status().IsStale); Check(legacy.Neighborhood(prefab).Nodes.Length > 1); }
            File.WriteAllText(project.StatePath("project-graph.json"), saved);
        });
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (int i = 0; i < 200; i++) { if (condition()) return; await Task.Delay(20); }
        throw new TimeoutException("等待图谱时效状态超时");
    }
}
