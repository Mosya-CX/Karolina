using System.Text.Json;

namespace Karolina.Core;

public sealed record LibraryDocument(string Id, string Title, string Type, string? Status, string Domain, string Path, string MetadataPath, string Code, string Section = "", string[]? Tags = null, ResourceReference[]? ResourceRefs = null);

/// <summary>正文与元数据分开存储。目录只读中文分类，历史来源不参与当前合同解析。</summary>
public sealed partial class DocumentLibrary : IDisposable
{
    private readonly object cacheGate = new();
    private LibraryDocument[]? cached;
    private readonly FileSystemWatcher watcher;
    private long changeVersion, loadedVersion = -1;
    public DocumentLibrary(string root)
    {
        Root = ProjectContext.Normalize(root);
        watcher = new FileSystemWatcher(Root, "*.meta.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size };
        watcher.Changed += (_, _) => Invalidate(); watcher.Created += (_, _) => Invalidate(); watcher.Deleted += (_, _) => Invalidate(); watcher.Renamed += (_, _) => Invalidate(); watcher.Error += (_, _) => Invalidate();
        watcher.EnableRaisingEvents = true;
    }
    private void Invalidate() => Interlocked.Increment(ref changeVersion);
    public void Dispose() => watcher.Dispose();
    public string Root { get; }
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public LibraryDocument[] List(bool refresh = true)
    {
        lock (cacheGate) return !refresh && cached != null && loadedVersion == Interlocked.Read(ref changeVersion) ? cached : LoadIndex();
    }
    private LibraryDocument[] LoadIndex()
    {
        long observed = Interlocked.Read(ref changeVersion);
        // 只有索引失效或显式刷新才扫描目录；阅读正文不逐文件查询时间戳。
        var paths = new[] { "requirements", "plans", "rules", "engineering", "resources", "tools" }.Select(c => System.IO.Path.Combine(Root, "Docs", c))
            .Where(Directory.Exists).SelectMany(d => Directory.EnumerateFiles(d, "*.meta.json", SearchOption.AllDirectories)).Order(StringComparer.Ordinal).ToArray();
        var result = new List<LibraryDocument>();
        foreach (var meta in paths)
            {
                using var json = JsonDocument.Parse(File.ReadAllText(SafePath(System.IO.Path.GetRelativePath(Root, meta))));
                var e = json.RootElement;
                string Get(string name) => e.TryGetProperty(name, out var p) ? p.GetString() ?? "" : "";
                string path = Get("path");
                if (path != System.IO.Path.GetRelativePath(Root, meta)[..^10].Replace('\\', '/') + ".md") throw new InvalidDataException("元数据与正文路径不一致：" + meta);
                if (!File.Exists(SafePath(path))) throw new InvalidDataException("文档正文不存在：" + path);
                var entry = new LibraryDocument(Get("id"), Get("title"), Get("type"), Get("type") == "rule" ? null : Get("status"), Get("domain"), path, System.IO.Path.GetRelativePath(Root, meta).Replace('\\', '/'), Get("code") is { Length: > 0 } code ? code : Get("id"), Get("section"));
                if (!System.Text.RegularExpressions.Regex.IsMatch(entry.Id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,119}$") || string.IsNullOrWhiteSpace(entry.Title) || entry.Type != "rule" && string.IsNullOrWhiteSpace(entry.Status)) throw new InvalidDataException("文档缺少有效编号、标题或状态：" + meta);
                if (entry.Type == "requirement" && entry.Status is not ("激活" or "废弃") || entry.Type == "plan" && entry.Status is not ("准备" or "执行" or "测试" or "校正" or "验收" or "关闭")) throw new InvalidDataException("文档状态不属于此类别的生命周期：" + meta);
                if(entry.Type=="rule"&&(entry.Section is not ("facts" or "execution" or "index" or "templates")||!entry.Path.StartsWith("Docs/rules/"+entry.Section+"/",StringComparison.Ordinal)))throw new InvalidDataException("规则正文与板块不一致："+meta);
                result.Add(entry with { Tags = e.TryGetProperty("tags", out var tags) ? NormalizeTags(JsonSerializer.Deserialize<string[]>(tags, Json) ?? []) : [], ResourceRefs = ReadReferences(e) });
            }
        if (result.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != result.Count) throw new InvalidDataException("文档编号重复");
        loadedVersion = observed;
        cached = result.OrderBy(e => e.Type).ThenBy(e => e.Type == "plan" ? NaturalKey(e.Code) : e.Domain, StringComparer.Ordinal).ThenBy(e => e.Title, StringComparer.Ordinal).ToArray();
        return cached;
    }
    private static string NaturalKey(string value) => System.Text.RegularExpressions.Regex.Replace(value, "[0-9]+", m => m.Value.PadLeft(20, '0'));
    public LibraryDocument Find(string id) => List(false).SingleOrDefault(e => e.Id == id) ?? throw new KeyNotFoundException("文档不存在：" + id);
    public string Read(string id) => File.ReadAllText(SafePath(Find(id).Path));
    public JsonElement Metadata(string id) { using var json = JsonDocument.Parse(File.ReadAllText(SafePath(Find(id).MetadataPath))); return json.RootElement.Clone(); }
    public object MetadataSnapshot(string id)
    {
        byte[] bytes = File.ReadAllBytes(SafePath(Find(id).MetadataPath)); using var json = JsonDocument.Parse(bytes);
        return new { metadata = json.RootElement.Clone(), hash = RequirementStore.Hash(bytes) };
    }
    public string Hash(string id) => RequirementStore.Hash(File.ReadAllBytes(SafePath(Find(id).Path)));
    public object Snapshot(string id)
    {
        var document = Find(id); var bytes = File.ReadAllBytes(SafePath(document.Path));
        return new { document, markdown = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'), hash = RequirementStore.Hash(bytes) };
    }
    public LibraryDocument Create(string type, string title, string domain, string[]? requirements = null, string? englishTitle = null, string? englishDomain = null, string? ruleSection = null)
    {
        if (type is not ("requirement" or "plan" or "rule")) throw new ArgumentException("无效文档类别");
        static string Name(string value)
        {
            value = value.Trim();
            if (value.Length is < 1 or > 70 || value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || value.Contains("..") || value.EndsWith('.') || value.EndsWith(' ')) throw new ArgumentException("标题和模块名不能为空或包含非法路径字符");
            return value;
        }
        title = Name(title); domain = Name(domain);
        using var guard = WorkspaceLock.Acquire(Root);
        string category = type switch { "requirement" => "requirements", "plan" => "plans", _ => "rules" };
        if (List().Any(d => d.Type == type && d.Domain == domain && d.Title == title)) throw new InvalidOperationException("此标题已存在，请使用不同标题");
        string prefix = type switch { "requirement" => "REQ", "plan" => "PLAN", _ => "RULE" };
        string id; int sequence = 1;
        var known = List().Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        do { id = $"{prefix}-USER-{sequence++:D4}"; } while (known.Contains(id));
        string slug = string.IsNullOrWhiteSpace(englishTitle) ? "new-" + type : englishTitle.Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$") || slug.Length > 90) throw new ArgumentException("英文文件标题请使用小写英文、数字及连字符");
        string folder = string.IsNullOrWhiteSpace(englishDomain) ? "custom" : englishDomain.Trim().ToLowerInvariant();
        if(type=="rule") { folder=ruleSection??"execution"; if(folder is not ("facts" or "execution" or "index" or "templates"))throw new ArgumentException("规则只能归入工程事实、执行细则、项目索引或模板"); }
        if (!System.Text.RegularExpressions.Regex.IsMatch(folder, "^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$") || folder.Length > 60) throw new ArgumentException("英文模块目录请使用小写英文、数字及连字符");
        string relative = $"Docs/{category}/{folder}/{id}_{slug}.md", path = SafePath(relative), metadata = path[..^3] + ".meta.json";
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string code = type == "plan" ? (List(false).Where(d => d.Type == "plan").Select(d => System.Text.RegularExpressions.Regex.Match(d.Code, "^[0-9]+").Value).Where(v => v.Length > 0).Select(int.Parse).DefaultIfEmpty(-1).Max() + 1).ToString("D4") : id;
        string catalogPath = SafePath("Docs/catalog.json");
        var catalog = File.Exists(catalogPath) ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(catalogPath))!.AsObject() : new System.Text.Json.Nodes.JsonObject { ["schemaVersion"] = 2, ["entries"] = new System.Text.Json.Nodes.JsonArray() };
        if (catalog["schemaVersion"]?.GetValue<int>() != 2 || catalog["entries"] is not System.Text.Json.Nodes.JsonArray) throw new InvalidDataException("当前资料索引不是版本 2");
        string template = type switch { "requirement" => "## 目标实现\n\n说明用户可见结果和范围。\n\n## 技术方案\n\n说明选定架构、算法、公式及选择原因。\n\n## 边界情况\n\n列出无效输入、失败、并发、恢复与兼容行为。\n\n## 验收条件\n\n给出可观察且可判定的结果。\n\n## 附录\n\n登记资源、配置和具体数值。", "plan" => "## 参考需求\n\n列出具体需求与章节，同时更新元数据 requirements。\n\n## 实施细节\n\n给出核心算法、类型与数据结构、数据流和依赖方向。\n\n## 执行步骤\n\n- [ ] 准备：确认需求、方案与测试设计\n- [ ] 执行：按实施细节落地\n- [ ] 测试：机器检查与独立 Agent 审查\n- [ ] 校正：处理机器、Agent 或人工发现的问题后复测\n- [ ] 验收：用户人工使用确认\n- [ ] 关闭：登记人工验收结论并冻结本次计划\n\n## Agent 测试与验收\n\n写明测试函数、场景、输入、预期结果和失败处理。\n\n## 进度与结果\n\n准备阶段，尚未执行。", _ => "## 适用范围\n\n## 执行规则\n\n## 例外与处理\n\n## 检查标准\n" };
        if (type == "plan" && requirements is { Length: > 0 }) template = template.Replace("列出具体需求与章节，同时更新元数据 requirements。", ReferenceMarkdown(path, requirements));
        File.WriteAllText(path, $"# {title}\n\n{template}\n");
        try
        {
            var node = JsonSerializer.SerializeToNode(new { id, code, title, type, domain, path = relative, version = 1, createdAt = DateTimeOffset.Now }, Json)!.AsObject();
            node["tags"] = JsonSerializer.SerializeToNode(new[] { domain }, Json);
            if (type == "plan") { node["plannedChanges"] = new System.Text.Json.Nodes.JsonArray(); node["resourceRefs"] = new System.Text.Json.Nodes.JsonArray(); }
            if(type=="rule")node["section"]=folder;
            if (type != "rule") node["status"] = type == "requirement" ? "激活" : "准备";
            if (type == "plan") { node["requirements"] = JsonSerializer.SerializeToNode(requirements ?? []); node["requirementRefs"] = RequirementRefs(requirements ?? []); }
            // 需求演进写入正文，元数据只保存索引、来源与证据。
            File.WriteAllText(metadata, node.ToJsonString(Json));
            catalog["entries"]!.AsArray().Add(JsonSerializer.SerializeToNode(new { id, path = relative, metadata = System.IO.Path.GetRelativePath(Root, metadata).Replace('\\', '/') }));
            File.WriteAllText(catalogPath + ".tmp", catalog.ToJsonString(Json)); File.Move(catalogPath + ".tmp", catalogPath, true);
        }
        catch { File.Delete(path); File.Delete(metadata); throw; }
        Invalidate(); return Find(id);
    }
    private System.Text.Json.Nodes.JsonArray RequirementRefs(string[] ids)
    {
        if (ids.Length > 50) throw new ArgumentException("每份计划最多引用 50 份需求");
        var result = new System.Text.Json.Nodes.JsonArray();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            var entry = Find(id);
            if (entry.Type != "requirement") throw new ArgumentException("计划引用必须是具体需求案");
            var metadata = Metadata(id);
            result.Add(JsonSerializer.SerializeToNode(new { id, title = entry.Title, status = entry.Status, version = metadata.TryGetProperty("version", out var v) ? v.GetInt32() : 1, sections = new[] { "目标实现", "技术方案", "边界情况", "附录" }, path = entry.Path, hash = Hash(id) }));
        }
        return result;
    }
    private string ReferenceMarkdown(string planPath, string[] ids) => string.Join('\n', ids.Distinct().Select(Find).Select(d => $"- [{d.Title}]({System.IO.Path.GetRelativePath(System.IO.Path.GetDirectoryName(planPath)!, SafePath(d.Path)).Replace('\\', '/')})：目标实现、技术方案、边界情况与附录。"));
    public void UpdateDetails(string id, string expectedMetadataHash, string? status, string[]? requirements, string? conclusion)
    {
        using var guard = WorkspaceLock.Acquire(Root);
        var entry = Find(id); string path = SafePath(entry.MetadataPath);
        byte[] latestMetadata = File.ReadAllBytes(path);
        if (RequirementStore.Hash(latestMetadata) != expectedMetadataHash) throw new InvalidOperationException("元数据已被外部修改，请重新打开文档信息");
        var obj = System.Text.Json.Nodes.JsonNode.Parse(latestMetadata)!.AsObject();
        AssertIdentity(entry, obj);
        entry = entry with { Status = obj["status"]?.GetValue<string>() };
        if (entry.Type == "rule") throw new InvalidOperationException("规则没有状态和计划引用，请编辑规则正文");
        if (entry.Type == "plan" && entry.Status is "关闭") throw new InvalidOperationException("计划已结束，请新建计划");
        if (requirements != null)
        {
            if (entry.Type != "plan") throw new ArgumentException("此类别不保存计划引用");
            var incoming = RequirementRefs(requirements);
            var original = obj["requirementRefs"]?.AsArray();
            var references = new System.Text.Json.Nodes.JsonArray();
            foreach (var reference in incoming)
            {
                string referenceId = reference!["id"]!.GetValue<string>();
                var previous = original?.FirstOrDefault(r => r?["id"]?.GetValue<string>() == referenceId);
                references.Add((previous ?? reference)!.DeepClone());
            }
            obj["requirementRefs"] = references; obj["requirements"] = JsonSerializer.SerializeToNode(requirements.Distinct().ToArray());
        }
        if (status != null && status != entry.Status)
        {
            string[] allowed = entry.Type == "requirement" ? ["激活", "废弃"] : entry.Status switch
            { "准备" => ["执行"], "执行" => ["测试"], "测试" => ["校正", "验收"], "校正" => ["执行", "测试"], "验收" => ["校正", "关闭"], _ => [] };
            if (!allowed.Contains(status)) throw new ArgumentException("此生命周期不支持所选转换");
            if (entry.Type == "plan" && status == "执行" && obj["requirements"]!.AsArray().Count == 0) throw new InvalidOperationException("计划进入执行前必须引用具体需求");
            if (entry.Type == "plan" && status == "执行" && obj["estimateStatus"] == null && (obj["plannedChanges"]?.AsArray().Count ?? 0) == 0) throw new InvalidOperationException("计划进入执行前必须在资料索引中预估新增/修改/删除文件");
            if (entry.Type == "plan" && status is "关闭")
            {
                if (string.IsNullOrWhiteSpace(conclusion) || conclusion.Length > 4000) throw new ArgumentException("结束计划需要填写验收或取消结论");
                obj["closedAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.Now); obj["conclusion"] = conclusion.Trim();
            }
            obj["status"] = status;
        }
        obj["version"] = (obj["version"]?.GetValue<int>() ?? 1) + 1; obj["updatedAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.Now);
        string catalogPath = SafePath("Docs/catalog.json"); var catalog = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(catalogPath))!.AsObject();
        if (entry.Type == "plan" && obj["status"]?.GetValue<string>() is "执行" or "测试" or "校正" or "验收")
        {
            if (catalog["activePlan"]?.GetValue<string>() is { } active && active != id) throw new InvalidOperationException("已有执行中的计划，请先完成当前计划");
            catalog["activePlan"] = id;
        }
        else if (catalog["activePlan"]?.GetValue<string>() == id) catalog["activePlan"] = null;
        string bodyPath = SafePath(entry.Path), oldBody = File.ReadAllText(bodyPath), body = oldBody;
        if (entry.Type == "requirement" && status != null && status != entry.Status)
            body = AppendEvolution(body, $"需求适用状态由「{entry.Status}」改为「{status}」。\n\n原因与影响：{(string.IsNullOrWhiteSpace(conclusion) ? "请在正文补充状态调整依据及承接需求。" : conclusion.Trim())}", obj["version"]!.GetValue<int>());
        obj.Remove("history"); obj.Remove("evolution");
        if (requirements != null)
        {
            string reference = "## 参考需求\n\n" + ReferenceMarkdown(bodyPath, requirements) + "\n\n";
            var pattern = new System.Text.RegularExpressions.Regex(@"^## 参考需求[^\r\n]*\r?\n.*?(?=^## |\z)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline);
            body = pattern.IsMatch(body) ? pattern.Replace(body, _ => reference, 1) : body.Insert(body.IndexOf('\n') + 1, "\n" + reference);
        }
        byte[] oldMetadata = File.ReadAllBytes(path), oldCatalog = File.ReadAllBytes(catalogPath);
        File.WriteAllText(path + ".tmp", obj.ToJsonString(Json)); File.WriteAllText(catalogPath + ".tmp", catalog.ToJsonString(Json)); File.WriteAllText(bodyPath + ".tmp", body);
        try { File.Move(bodyPath + ".tmp", bodyPath, true); File.Move(path + ".tmp", path, true); File.Move(catalogPath + ".tmp", catalogPath, true); }
        catch { File.WriteAllText(bodyPath, oldBody); File.WriteAllBytes(path, oldMetadata); File.WriteAllBytes(catalogPath, oldCatalog); throw; }
        finally { Invalidate(); }
    }
    public void Save(string id, string markdown, string expectedHash, string? changeSummary = null)
    {
        if (string.IsNullOrWhiteSpace(markdown) || markdown.Length > 2_000_000) throw new ArgumentException("正文为空或超过容量限制");
        using var guard = WorkspaceLock.Acquire(Root);
        var entry = Find(id); string path = SafePath(entry.Path), meta = SafePath(entry.MetadataPath);
        if (entry.Type == "plan" && entry.Status is "关闭") throw new InvalidOperationException("此计划已经结束，请新建计划承接后续工作；关闭记录不重新编辑执行。 ");
        if (Hash(id) != expectedHash) throw new InvalidOperationException("正文已被外部修改，请重新载入后再保存");
        var obj = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(meta))!.AsObject();
        AssertIdentity(entry, obj);
        if (entry.Type == "plan" && obj["status"]?.GetValue<string>() is "关闭") throw new InvalidOperationException("此计划已由外部关闭，请新建计划承接后续工作");
        int version = obj["version"]?.GetValue<int>() ?? 1;
        if (entry.Type == "requirement" && !string.IsNullOrWhiteSpace(changeSummary))
        {
            if (changeSummary.Length > 12000) throw new ArgumentException("需求变更说明超过容量限制");
            markdown = AppendEvolution(markdown, changeSummary.Trim(), version + 1);
        }
        obj.Remove("history"); obj.Remove("evolution");
        obj["version"] = version + 1; obj["updatedAt"] = JsonSerializer.SerializeToNode(DateTimeOffset.Now);
        string archive = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karolina", "document-revisions", new ProjectContext(Root).Key, entry.Id);
        var revisionsRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karolina", "document-revisions")) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(archive).StartsWith(revisionsRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("修订备份路径越界");
        for (DirectoryInfo? node = new(archive); node != null; node = node.Parent) if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("修订备份目录不能使用链接");
        Directory.CreateDirectory(archive); string stamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfffffff");
        File.Copy(path, System.IO.Path.Combine(archive, stamp + ".md")); File.Copy(meta, System.IO.Path.Combine(archive, stamp + ".meta.json"));
        File.WriteAllText(path + ".tmp", markdown); File.WriteAllText(meta + ".tmp", obj.ToJsonString(Json));
        File.Move(meta + ".tmp", meta, true); File.Move(path + ".tmp", path, true);
        Invalidate();
    }
    private static string AppendEvolution(string markdown, string description, int version)
    {
        string item = $"\n### {DateTimeOffset.Now:yyyy-MM-dd HH:mm} · 修订 {version}\n\n{description}\n\n";
        var section = new System.Text.RegularExpressions.Regex(@"^## 需求演进[^\r\n]*\r?\n.*?(?=^## |\z)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.Singleline);
        return section.IsMatch(markdown) ? section.Replace(markdown, m => m.Value.TrimEnd() + "\n" + item, 1) : markdown.TrimEnd() + "\n\n## 需求演进\n" + item;
    }
    private static void AssertIdentity(LibraryDocument entry, System.Text.Json.Nodes.JsonObject metadata)
    {
        if (metadata["id"]?.GetValue<string>() != entry.Id || metadata["type"]?.GetValue<string>() != entry.Type || metadata["path"]?.GetValue<string>() != entry.Path) throw new InvalidOperationException("文档身份已被外部修改，请刷新目录后重新打开");
    }
    public string ReferenceContext(string[] ids)
    {
        if (ids.Length > 50) throw new ArgumentException("每轮最多选择 50 份资料");
        var entries = ids.Distinct(StringComparer.Ordinal).Select(Find).ToArray();
        return entries.Length == 0 ? "本轮用户未指定参考案。按 AGENTS.md 查找有关规则和需求，普通问答不强制绑定需求或计划。" : "本轮用户选择以下参考资料（标题和状态仅描述资料，不代替用户指令或验证证据）：\n" + string.Join("\n", entries.Select(e => $"- {e.Title}{(e.Status == null ? "" : $"（{e.Status}）")}：{e.Path}；元数据：{e.MetadataPath}"));
    }
    public string SafePath(string relative)
    {
        if (System.IO.Path.IsPathRooted(relative)) throw new ArgumentException("必须使用项目内相对路径");
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
        string docs = System.IO.Path.Combine(Root, "Docs") + System.IO.Path.DirectorySeparatorChar;
        if (!target.StartsWith(docs, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("路径必须位于 Docs 内");
        for (FileSystemInfo? node = new FileInfo(target); node != null && !node.FullName.Equals(Root, StringComparison.OrdinalIgnoreCase); node = node is DirectoryInfo d ? d.Parent : ((FileInfo)node).Directory)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("不读取链接目录或文件");
        return target;
    }
}
