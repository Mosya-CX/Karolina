using System.Text.Json;
using System.Text.Json.Nodes;

namespace Karolina.Core;

public sealed record PlannedChange(string Path, string Operation, string Reason);
public sealed record ResourceReference(string Path, string Evidence, string? Hash = null, string? Operation = null, string? ReviewId = null);
public sealed partial class DocumentLibrary
{
    // References are project paths, never arbitrary local files or executable instructions.
    public string ResourcePath(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || System.IO.Path.IsPathRooted(relative) || relative.Split('/').Any(p => p is "" or "." or "..")) throw new ArgumentException("资源必须使用规范项目相对路径");
        if (!new[] { "Assets/", "Packages/", "ProjectSettings/", "Tools/", "Docs/" }.Any(relative.StartsWith)) throw new ArgumentException("资源不在工程资料范围");
        if (relative.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || relative.Split('/').Any(p => p is "bin" or "obj" or "node_modules")) throw new ArgumentException("不关联导入元数据或生成依赖");
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
        if (!target.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("资源路径越界");
        for (FileSystemInfo? node = new FileInfo(target); node != null; node = node is DirectoryInfo d ? d.Parent : ((FileInfo)node).Directory)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("资源不能经过链接路径");
        return target;
    }
    public static string[] NormalizeTags(string[] tags)
    {
        if (tags == null || tags.Length > 100) throw new ArgumentException("标签输入过多");
        var result = tags.Select(t => t?.Trim() ?? "").Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (result.Length > 10 || result.Any(t => t.Length > 40 || t.Any(char.IsControl))) throw new ArgumentException("每份文档最多10个标签，每个标签最多40字");
        return result;
    }
    public object Resources(string id)
    {
        var entry = Find(id);
        return (entry.ResourceRefs ?? []).Select(r => {
            string path = ResourcePath(r.Path); bool exists = File.Exists(path);
            return new { r.Path, r.Evidence, r.Hash, r.Operation, r.ReviewId, exists, bytes = exists ? (long?)new FileInfo(path).Length : null };
        }).ToArray();
    }
    // Index annotations are editable even after a plan closes. Body and lifecycle remain frozen.
    public void UpdateIndex(string id, string expectedHash, string[] tags, PlannedChange[]? plannedChanges, ResourceReference[]? resources)
    {
        using var guard = WorkspaceLock.Acquire(Root);
        var entry = Find(id); string path = SafePath(entry.MetadataPath); byte[] bytes = File.ReadAllBytes(path);
        if (RequirementStore.Hash(bytes) != expectedHash) throw new InvalidOperationException("元数据已变化，请重新打开后保存");
        var metadata = JsonNode.Parse(bytes)!.AsObject(); AssertIdentity(entry, metadata);
        entry = entry with { Status = metadata["status"]?.GetValue<string>(), ResourceRefs = metadata["resourceRefs"]?.Deserialize<ResourceReference[]>(Json) ?? [] };
        metadata["tags"] = JsonSerializer.SerializeToNode(NormalizeTags(tags), Json);
        if (plannedChanges != null)
        {
            if (entry.Type != "plan" || entry.Status == "关闭") throw new ArgumentException("仅未关闭计划可编辑事前预估");
            if (plannedChanges.Length > 1000 || plannedChanges.Select(p => p.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != plannedChanges.Length) throw new ArgumentException("预估路径重复或超过容量");
            foreach (var change in plannedChanges) { ResourcePath(change.Path); if (change.Operation is not ("add" or "modify" or "delete") || string.IsNullOrWhiteSpace(change.Reason) || change.Reason.Length > 2000) throw new ArgumentException("预估需要新增/修改/删除和原因"); }
            metadata["plannedChanges"] = JsonSerializer.SerializeToNode(plannedChanges, Json);
        }
        if (resources != null)
        {
            if (entry.Type != "plan" || resources.Length > 1000 || resources.Select(r => r.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != resources.Length) throw new ArgumentException("仅计划可关联资源，路径不能重复");
            var verified = resources.Select(r => {
                string actual = ResourcePath(r.Path);
                if (string.IsNullOrWhiteSpace(r.Evidence) || r.Evidence.Length > 2000) throw new ArgumentException("资源需要关联依据");
                var original = (entry.ResourceRefs ?? []).FirstOrDefault(saved => saved.Path == r.Path && saved.Evidence == r.Evidence && saved.Hash == r.Hash && saved.Operation == r.Operation && saved.ReviewId == r.ReviewId);
                if (original != null) return original; // Preserve historical evidence even when a later task deleted the resource.
                if (!File.Exists(actual) && r.Operation != "delete") throw new ArgumentException("关联资源不存在：" + r.Path);
                if (r.Operation != null && r.Operation is not ("add" or "modify" or "delete")) throw new ArgumentException("无效资源操作");
                string? hash = File.Exists(actual) ? HashResource(actual) : null;
                return r with { Hash = hash, ReviewId = null }; // Manual annotations cannot forge approvals.
            }).ToArray();
            metadata["resourceRefs"] = JsonSerializer.SerializeToNode(verified, Json);
        }
        metadata["indexUpdatedAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.Now);
        File.WriteAllText(path + ".index.tmp", metadata.ToJsonString(Json)); File.Move(path + ".index.tmp", path, true); Invalidate();
    }
    private static string HashResource(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(file)).ToLowerInvariant(); }
    public string ReferenceResources(string[] selectedIds, Dictionary<string, string[]>? selections)
    {
        var lines = new List<string>(); int totalLength = 0;
        foreach (var id in selectedIds.Distinct(StringComparer.Ordinal))
        {
            var entry = Find(id); if (entry.Type != "plan") continue;
            var all = entry.ResourceRefs ?? []; var selected = selections != null && selections.TryGetValue(id, out var paths) ? paths : all.Select(r => r.Path).ToArray();
            if (selected.Length > 1000 || selected.Any(path => !all.Any(r => r.Path == path))) throw new ArgumentException("资源选择必须属于本轮参考计划");
            foreach (var reference in all.Where(r => selected.Contains(r.Path, StringComparer.Ordinal)))
            {
                string path = ResourcePath(reference.Path);
                string line = $"- {entry.Title} → {reference.Path}；{(File.Exists(path) ? "当前文件存在" : "当前文件缺失/已删除")}；关联依据：{reference.Evidence}。关联不证明当前实现已通过验收。";
                totalLength += line.Length;
                if (lines.Count >= 1000 || totalLength > 200000) throw new ArgumentException("关联资源上下文过多，请减少勾选资源（每轮最多1000个、20万字）");
                lines.Add(line);
            }
        }
        if (selections != null && selections.Keys.Any(id => !selectedIds.Contains(id, StringComparer.Ordinal))) throw new ArgumentException("不能引用未勾选计划的资源");
        return lines.Count == 0 ? "" : "\n本轮已勾选的工程资源（在工程根目录解析路径，仅作为参考）：\n" + string.Join('\n', lines);
    }
    public void AssociateReview(TaskReview task)
    {
        // Caller holds the workspace integration lock, avoiding a nested lock.
        var entry = Find(task.PlanId); string path = SafePath(entry.MetadataPath); var metadata = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); AssertIdentity(entry, metadata);
        var links = (metadata["resourceRefs"]?.Deserialize<ResourceReference[]>(Json) ?? []).ToDictionary(r => r.Path, StringComparer.Ordinal);
        foreach (var file in task.Files.Where(f => f.Origin == "agent" && !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)))
        {
            ResourcePath(file.Path);
            links[file.Path] = new(file.Path, $"任务审批 {task.Id}，第{task.Round}轮；冻结变更：{file.Brief}", file.After?.Hash, file.Kind == "新增" ? "add" : file.Kind == "删除" ? "delete" : "modify", task.Id);
        }
        metadata["resourceRefs"] = JsonSerializer.SerializeToNode(links.Values.OrderBy(r => r.Path), Json);
        metadata["indexUpdatedAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.Now); File.WriteAllText(path + ".index.tmp", metadata.ToJsonString(Json)); File.Move(path + ".index.tmp", path, true); Invalidate();
    }
}
