using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Karolina.Core;

public sealed record ProjectGraphNode(string Id, string Kind, string Title, string Path, string? Symbol = null);
public sealed record ProjectGraphEdge(string From, string Relation, string To);
public sealed record ProjectGraphWarning(string Path, string Message);
public sealed record ProjectGraphSnapshot(int SchemaVersion, string Root, DateTimeOffset IndexedAt,
    int FilesScanned, int SkippedFiles, int NodesCount, int EdgesCount, int UnresolvedGuids,
    ProjectGraphWarning[] Warnings, ProjectGraphNode[] Nodes, ProjectGraphEdge[] Edges);
public sealed record ProjectGraphStatus(string Root, bool Indexed, bool Indexing, string Stage,
    int FilesScanned, int SkippedFiles, int Nodes, int Edges, int UnresolvedGuids,
    DateTimeOffset? LastSuccessfulIndex, string? Error, ProjectGraphWarning[] Warnings);
public sealed record ProjectGraphSearchResult(ProjectGraphStatus Status, ProjectGraphNode[] Nodes, bool Truncated);
public sealed record ProjectGraphNodeDetail(ProjectGraphStatus Status, ProjectGraphNode Node,
    ProjectGraphEdge[] Incoming, ProjectGraphEdge[] Outgoing,
    ProjectGraphNode[] RelatedNodes);

/// <summary>Read-only, project-scoped CodeGraph and Unity GUID reference index.</summary>
public sealed class ProjectGraphService
{
    private const int Schema = 1;
    private const int MaxFiles = 100_000;
    private const int MaxNodes = 100_000;
    private const int MaxEdges = 500_000;
    private const int MaxTextBytes = 2 * 1024 * 1024;
    private const int MaxWarnings = 250;
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".karolina", "Library", "Temp", "Obj", "Builds", "Logs", "UserSettings",
        "artifacts", "bin", "obj", "node_modules", ".vs", ".idea"
    };
    private static readonly HashSet<string> SerializedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".asset", ".prefab", ".unity", ".mat", ".controller", ".overridecontroller", ".anim",
        ".rendertexture", ".mask", ".lighting", ".giparams", ".physicmaterial", ".terrainlayer",
        ".guiskin", ".flare", ".fontsettings", ".preset", ".spriteatlas", ".mixer", ".playable",
        ".shadervariants", ".asmdef", ".asmref"
    };
    private static readonly Regex GuidReference = new(@"\bguid:\s*([a-fA-F0-9]{32})\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NamespaceDeclaration = new(@"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*)\s*(?<terminator>[;{])", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex TypeDeclaration = new(@"\b(?:(?:class|struct|interface|enum)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)|record(?:\s+(?:class|struct))?\s+(?<name>[A-Za-z_][A-Za-z0-9_]*))\s*(?:<[^;{}]*>)?(?:\s*\([^;{}]*\))?(?:\s*:\s*(?<bases>[^;{}]+))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Identifier = new(@"\b[A-Za-z_][A-Za-z0-9_]*\b", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private readonly ProjectContext project;
    private readonly object gate = new();
    private readonly string storePath;
    private ProjectGraphSnapshot? snapshot;
    private bool indexing;
    private string stage = "尚未索引";
    private int filesScanned, skippedFiles;
    private string? error;

    public ProjectGraphService(ProjectContext project)
    {
        this.project = project;
        storePath = project.StatePath("project-graph.json");
        if (!File.Exists(storePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<ProjectGraphSnapshot>(File.ReadAllText(storePath), DocumentLibrary.Json)
                ?? throw new InvalidDataException("工程图谱文件为空");
            ValidateSnapshot(saved);
            snapshot = saved;
            stage = "已载入上次成功索引";
            filesScanned = saved.FilesScanned;
            skippedFiles = saved.SkippedFiles;
        }
        catch (Exception e) when (e is JsonException or InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            error = "旧图谱不可读取，原文件已保留；请重新建立索引：" + e.Message;
            stage = "索引需要重建";
        }
    }

    public ProjectGraphStatus Status()
    {
        lock (gate) return MakeStatus();
    }

    public ProjectGraphStatus StartIndex()
    {
        lock (gate)
        {
            if (indexing) return MakeStatus();
            indexing = true;
            stage = "准备扫描工程";
            filesScanned = 0;
            skippedFiles = 0;
            error = null;
            _ = Task.Run(BuildAndSave);
            return MakeStatus();
        }
    }

    public ProjectGraphSearchResult Search(string? query, string? kind, int take = 250)
    {
        ProjectGraphSnapshot current;
        lock (gate) current = snapshot ?? throw new InvalidOperationException("工程图谱尚未建立，请开始索引");
        if (current.Root != project.Root) throw new InvalidDataException("图谱属于其他工程，拒绝读取");
        string[] terms = (query ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var filtered = current.Nodes.Where(n => string.IsNullOrWhiteSpace(kind) || n.Kind == kind)
            .Where(n => terms.All(term => Contains(n.Title, term) || Contains(n.Path, term) || Contains(n.Symbol ?? "", term)))
            .OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.Path, StringComparer.Ordinal).ToArray();
        int limit = Math.Clamp(take, 1, 500);
        return new(Status(), filtered.Take(limit).ToArray(), filtered.Length > limit);
    }

    public ProjectGraphNodeDetail Node(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 300) throw new ArgumentException("图谱节点编号无效");
        ProjectGraphSnapshot current;
        lock (gate) current = snapshot ?? throw new InvalidOperationException("工程图谱尚未建立，请开始索引");
        var node = current.Nodes.SingleOrDefault(n => n.Id == id) ?? throw new KeyNotFoundException("工程图谱节点不存在");
        var incoming = current.Edges.Where(e => e.To == id).Take(200).ToArray();
        var outgoing = current.Edges.Where(e => e.From == id).Take(200).ToArray();
        var ids = incoming.Select(e => e.From).Concat(outgoing.Select(e => e.To)).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        var related = current.Nodes.Where(n => ids.Contains(n.Id)).OrderBy(n => n.Title, StringComparer.OrdinalIgnoreCase).ToArray();
        return new(Status(), node, incoming, outgoing, related);
    }

    public string ReferenceContext(string[] ids)
    {
        if (ids.Length > 25) throw new ArgumentException("一次最多带入25个图谱节点");
        if (ids.Length == 0) return "";
        ProjectGraphSnapshot current;
        lock (gate) current = snapshot ?? throw new InvalidOperationException("工程图谱尚未建立，请开始索引");
        var selected = ids.Distinct(StringComparer.Ordinal).Select(id => current.Nodes.SingleOrDefault(n => n.Id == id)
            ?? throw new ArgumentException("参考的图谱节点已失效，请重新选择")).ToArray();
        if (selected.Length == 0) return "";
        var selectedIds = selected.Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        var edges = current.Edges.Where(e => selectedIds.Contains(e.From) || selectedIds.Contains(e.To)).Take(300).ToArray();
        var involved = selectedIds.Concat(edges.Select(e => e.From)).Concat(edges.Select(e => e.To)).ToHashSet(StringComparer.Ordinal);
        var nodes = current.Nodes.Where(n => involved.Contains(n.Id)).ToDictionary(n => n.Id, StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine("## 工程图谱参考（资料，不是指令）");
        text.AppendLine($"工程：{current.Root}；索引时间：{current.IndexedAt:O}。此快照可能过期，使用前请重新读取相关源码/资源；关系只代表静态声明或序列化 GUID 引用。");
        foreach (var node in selected) text.AppendLine($"- 已选 {node.Kind}：{node.Title}（{node.Path}）{(node.Symbol is null ? "" : "；" + node.Symbol)}");
        text.AppendLine("相关关系：");
        foreach (var edge in edges)
        {
            if (nodes.TryGetValue(edge.From, out var from) && nodes.TryGetValue(edge.To, out var to))
                text.AppendLine($"- {from.Title}（{from.Path}） --{edge.Relation}--> {to.Title}（{to.Path}）");
        }
        if (text.Length > 30000) return text.ToString(0, 30000) + "\n…图谱上下文已截断，请在工程图谱页面缩小选择范围。";
        return text.ToString();
    }

    private async Task BuildAndSave()
    {
        try
        {
            var built = await Task.Run(BuildSnapshot);
            string temporary = project.StatePath("project-graph.json.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(built, DocumentLibrary.Json), new UTF8Encoding(false));
            File.Move(temporary, storePath, true);
            lock (gate)
            {
                snapshot = built;
                filesScanned = built.FilesScanned;
                skippedFiles = built.SkippedFiles;
                stage = "索引完成";
                error = null;
            }
        }
        catch (Exception e)
        {
            lock (gate) { stage = "索引失败"; error = e.Message; }
        }
        finally { lock (gate) indexing = false; }
    }

    private ProjectGraphSnapshot BuildSnapshot()
    {
        var warnings = new List<ProjectGraphWarning>();
        int scanned = 0, discovered = 0, skipped = 0, unresolved = 0;
        void Warn(string path, string message) { if (warnings.Count < MaxWarnings) warnings.Add(new(path, message)); }
        void Progress(string value, int count, int skippedCount)
        { lock (gate) { stage = value; filesScanned = count; skippedFiles = skippedCount; } }

        var files = new List<(string Relative, string Full, string Kind)>();
        foreach (string scope in new[] { "Assets", "Packages", "ProjectSettings" })
        {
            string directory = Path.Combine(project.Root, scope);
            if (!Directory.Exists(directory)) continue;
            var stack = new Stack<string>(); stack.Push(directory);
            while (stack.TryPop(out var folder))
            {
                if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                { skipped++; Warn(Path.GetRelativePath(project.Root, folder).Replace('\\', '/'), "链接目录未遍历"); continue; }
                foreach (string child in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).Reverse())
                {
                    string name = Path.GetFileName(child);
                    if (ExcludedDirectories.Contains(name)) continue;
                    stack.Push(child);
                }
                foreach (string file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    { skipped++; Warn(Path.GetRelativePath(project.Root, file).Replace('\\', '/'), "链接文件未读取"); continue; }
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = Path.GetRelativePath(project.Root, file).Replace('\\', '/');
                    string kind = Path.GetExtension(file).Equals(".cs", StringComparison.OrdinalIgnoreCase) ? "代码文件" : "Unity资源";
                    files.Add((relative, file, kind));
                    discovered++;
                    if (files.Count > MaxFiles) throw new InvalidDataException($"工程文件超过索引上限 {MaxFiles:N0}；未替换上次成功快照");
                    if (discovered % 500 == 0) Progress($"枚举工程文件 · 已发现 {discovered:N0}", scanned, skipped);
                }
            }
        }

        var nodes = new List<ProjectGraphNode>(files.Count);
        var edges = new HashSet<ProjectGraphEdge>();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var fileNodeByPath = new Dictionary<string, string>(StringComparer.Ordinal);
        var sourceByFile = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            string id = "file:" + file.Relative;
            AddNode(new(id, file.Kind, Path.GetFileName(file.Relative), file.Relative));
            fileNodeByPath[file.Relative] = id;
        }

        var typeSources = new List<(string Relative, string FullPath, string Namespace, string Name, string Id, string[] Bases, string Clean)>();
        Progress("分析 C# 类型", scanned, skipped);
        foreach (var file in files.Where(f => f.Kind == "代码文件"))
        {
            scanned++;
            if (!TryReadSmallText(file.Full, out string source, out string issue))
            { skipped++; Warn(file.Relative, issue); continue; }
            string clean = StripCommentsAndLiterals(source);
            sourceByFile[file.Relative] = clean;
            var namespaceScopes = FindNamespaceScopes(clean);
            foreach (Match declaration in TypeDeclaration.Matches(clean))
            {
                string name = declaration.Groups["name"].Value;
                string ns = string.Join(".", namespaceScopes
                    .Where(scope => scope.Start <= declaration.Index && declaration.Index < scope.End)
                    .OrderBy(scope => scope.Start)
                    .Select(scope => scope.Name));
                string full = string.IsNullOrEmpty(ns) ? name : ns + "." + name;
                string symbolId = "type:" + StableHash(file.Relative + "\0" + full);
                var bases = declaration.Groups["bases"].Success
                    ? Identifier.Matches(declaration.Groups["bases"].Value).Select(m => m.Value).Where(t => t != "where").Distinct(StringComparer.Ordinal).ToArray()
                    : [];
                AddNode(new(symbolId, "C# 类型", full, file.Relative, full));
                AddEdge(new(fileNodeByPath[file.Relative], "声明", symbolId));
                typeSources.Add((file.Relative, file.Full, ns, name, symbolId, bases, clean));
            }
            if (scanned % 50 == 0) Progress("分析 C# 类型", scanned, skipped);
        }

        var symbolsByName = typeSources.GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        foreach (var type in typeSources)
        {
            foreach (string baseName in type.Bases)
            {
                if (symbolsByName.TryGetValue(baseName, out var candidates) && candidates.Length == 1 && candidates[0].Id != type.Id)
                    AddEdge(new(type.Id, "继承/实现", candidates[0].Id));
            }
        }
        foreach (var file in files.Where(f => f.Kind == "代码文件"))
        {
            if (!sourceByFile.TryGetValue(file.Relative, out string? clean)) continue;
            var identifiers = Identifier.Matches(clean).Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
            foreach (string name in identifiers)
            {
                if (!symbolsByName.TryGetValue(name, out var candidates) || candidates.Length != 1 || candidates[0].Relative == file.Relative) continue;
                AddEdge(new(fileNodeByPath[file.Relative], "类型引用", candidates[0].Id));
            }
            if (scanned % 50 == 0) Progress("分析 C# 类型引用", scanned, skipped);
        }

        Progress("解析 Unity GUID 引用", scanned, skipped);
        var guidTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var duplicateGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            string meta = file.Full + ".meta";
            if (!File.Exists(meta)) continue;
            if ((File.GetAttributes(meta) & FileAttributes.ReparsePoint) != 0)
            { skipped++; Warn(file.Relative + ".meta", "链接 .meta 未读取"); continue; }
            try
            {
                var info = new FileInfo(meta);
                if (info.Length > 64 * 1024) { skipped++; Warn(file.Relative + ".meta", ".meta 超过 64 KB，未读取 GUID"); continue; }
                var match = GuidReference.Match(File.ReadAllText(meta));
                if (!match.Success) { skipped++; continue; }
                string guid = match.Groups[1].Value;
                if (guidTargets.ContainsKey(guid)) { guidTargets.Remove(guid); duplicateGuids.Add(guid); }
                else if (!duplicateGuids.Contains(guid)) guidTargets.Add(guid, fileNodeByPath[file.Relative]);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or DecoderFallbackException)
            { skipped++; Warn(file.Relative + ".meta", "GUID 无法读取：" + e.Message); }
        }
        foreach (string guid in duplicateGuids) Warn(".meta", "发现重复 GUID，相关引用不生成关系边：" + guid);

        var unresolvedSamples = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.Where(f => f.Kind == "Unity资源" && SerializedExtensions.Contains(Path.GetExtension(f.Relative))))
        {
            scanned++;
            if (!TryReadSmallText(file.Full, out string source, out string issue))
            { skipped++; Warn(file.Relative, issue); continue; }
            foreach (Match match in GuidReference.Matches(source))
            {
                string guid = match.Groups[1].Value;
                if (guidTargets.TryGetValue(guid, out string? target) && target != fileNodeByPath[file.Relative])
                    AddEdge(new(fileNodeByPath[file.Relative], "Unity GUID 引用", target));
                else if (!guidTargets.ContainsKey(guid) && !guid.StartsWith("0000000000000000", StringComparison.OrdinalIgnoreCase))
                { unresolved++; if (unresolvedSamples.Count < 100) unresolvedSamples.Add(file.Relative); }
            }
            if (scanned % 50 == 0) Progress("解析 Unity GUID 引用", scanned, skipped);
        }
        foreach (string sample in unresolvedSamples) Warn(sample, "包含未解析 GUID 引用；目标可能缺失、外部或属于重复 GUID");
        if (nodes.Count > MaxNodes) throw new InvalidDataException($"图谱节点超过上限 {MaxNodes:N0}；未替换上次成功快照");
        if (edges.Count > MaxEdges) throw new InvalidDataException($"图谱关系超过上限 {MaxEdges:N0}；未替换上次成功快照");
        Progress("写入索引快照", scanned, skipped);
        return new(Schema, project.Root, DateTimeOffset.UtcNow, scanned, skipped, nodes.Count, edges.Count, unresolved,
            warnings.ToArray(), nodes.OrderBy(n => n.Kind, StringComparer.Ordinal).ThenBy(n => n.Path, StringComparer.Ordinal).ThenBy(n => n.Title, StringComparer.Ordinal).ToArray(),
            edges.OrderBy(e => e.From, StringComparer.Ordinal).ThenBy(e => e.Relation, StringComparer.Ordinal).ThenBy(e => e.To, StringComparer.Ordinal).ToArray());

        void AddNode(ProjectGraphNode node)
        {
            if (!nodeIds.Add(node.Id)) return;
            nodes.Add(node);
            if (nodes.Count > MaxNodes) throw new InvalidDataException($"图谱节点超过上限 {MaxNodes:N0}；未替换上次成功快照");
        }
        void AddEdge(ProjectGraphEdge edge)
        {
            edges.Add(edge);
            if (edges.Count > MaxEdges) throw new InvalidDataException($"图谱关系超过上限 {MaxEdges:N0}；未替换上次成功快照");
        }
    }

    private static NamespaceScope[] FindNamespaceScopes(string source)
    {
        var scopes = new List<NamespaceScope>();
        foreach (Match declaration in NamespaceDeclaration.Matches(source))
        {
            string name = Regex.Replace(declaration.Groups["name"].Value, @"\s", "");
            int bodyStart = declaration.Index + declaration.Length;
            if (declaration.Groups["terminator"].Value == ";")
            {
                scopes.Add(new(name, bodyStart, source.Length));
                continue;
            }

            int openBrace = source.IndexOf('{', declaration.Index + declaration.Length - 1);
            if (openBrace < 0 || !TryFindClosingBrace(source, openBrace, out int closeBrace)) continue;
            scopes.Add(new(name, openBrace + 1, closeBrace));
        }
        return scopes.ToArray();
    }

    private static bool TryFindClosingBrace(string source, int openBrace, out int closeBrace)
    {
        int depth = 0;
        for (int i = openBrace; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
            {
                closeBrace = i;
                return true;
            }
        }
        closeBrace = -1;
        return false;
    }

    private sealed record NamespaceScope(string Name, int Start, int End);

    private ProjectGraphStatus MakeStatus()
    {
        var current = snapshot;
        return new(project.Root, current != null, indexing, stage, filesScanned, skippedFiles,
            current?.NodesCount ?? 0, current?.EdgesCount ?? 0, current?.UnresolvedGuids ?? 0,
            current?.IndexedAt, error, current?.Warnings ?? []);
    }

    private void ValidateSnapshot(ProjectGraphSnapshot value)
    {
        if (string.IsNullOrWhiteSpace(value.Root) || value.Nodes is null || value.Edges is null || value.Warnings is null || value.SchemaVersion != Schema ||
            !Path.GetFullPath(value.Root).Equals(project.Root, StringComparison.OrdinalIgnoreCase) ||
            value.Nodes.Length > MaxNodes || value.Edges.Length > MaxEdges || value.NodesCount != value.Nodes.Length || value.EdgesCount != value.Edges.Length)
            throw new InvalidDataException("图谱格式/工程根路径/容量不匹配");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in value.Nodes)
            if (node is null || string.IsNullOrWhiteSpace(node.Id) || string.IsNullOrWhiteSpace(node.Kind) || string.IsNullOrWhiteSpace(node.Path) || !ids.Add(node.Id))
                throw new InvalidDataException("图谱包含空节点或重复节点");
        var edges = new HashSet<ProjectGraphEdge>();
        foreach (var edge in value.Edges)
            if (edge is null || !ids.Contains(edge.From) || !ids.Contains(edge.To) || string.IsNullOrWhiteSpace(edge.Relation) || !edges.Add(edge))
                throw new InvalidDataException("图谱包含无效或重复关系");
        if (value.Warnings.Any(warning => warning is null || warning.Path is null || warning.Message is null))
            throw new InvalidDataException("图谱包含无效告警记录");
    }

    private static bool TryReadSmallText(string path, out string text, out string issue)
    {
        text = "";
        try
        {
            var info = new FileInfo(path);
            if (info.Length > MaxTextBytes) { issue = "文件超过 2 MB，未读取源码/序列化引用"; return false; }
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.AsSpan().Contains((byte)0)) { issue = "二进制文件未解析"; return false; }
            text = new UTF8Encoding(false, true).GetString(bytes);
            issue = ""; return true;
        }
        catch (DecoderFallbackException) { issue = "文件不是有效 UTF-8 文本，未解析"; return false; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { issue = "文件无法读取：" + e.Message; return false; }
    }

    private static string StripCommentsAndLiterals(string source)
    {
        var chars = source.ToCharArray();
        int i = 0;
        while (i < chars.Length)
        {
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '/')
            { int end = i + 2; while (end < chars.Length && chars[end] is not ('\r' or '\n')) end++; Blank(i, end); i = end; continue; }
            if (chars[i] == '/' && i + 1 < chars.Length && chars[i + 1] == '*')
            { int end = source.IndexOf("*/", i + 2, StringComparison.Ordinal); end = end < 0 ? chars.Length : end + 2; Blank(i, end); i = end; continue; }
            int quoteAt = i;
            while (quoteAt < chars.Length && (chars[quoteAt] == '$' || chars[quoteAt] == '@')) quoteAt++;
            if (quoteAt < chars.Length && chars[quoteAt] == '"')
            {
                int quoteCount = 1; while (quoteAt + quoteCount < chars.Length && chars[quoteAt + quoteCount] == '"') quoteCount++;
                int end = quoteAt + quoteCount;
                if (quoteCount >= 3)
                {
                    string delimiter = new('"', quoteCount); int close = source.IndexOf(delimiter, end, StringComparison.Ordinal); end = close < 0 ? chars.Length : close + quoteCount;
                }
                else
                {
                    bool verbatim = source.AsSpan(i, quoteAt - i).Contains('@');
                    while (end < chars.Length)
                    {
                        if (!verbatim && chars[end] == '\\') { end = Math.Min(chars.Length, end + 2); continue; }
                        if (chars[end] == '"')
                        {
                            if (verbatim && end + 1 < chars.Length && chars[end + 1] == '"') { end += 2; continue; }
                            end++; break;
                        }
                        end++;
                    }
                }
                Blank(i, end); i = end; continue;
            }
            if (chars[i] == '\'')
            {
                int end = i + 1;
                while (end < chars.Length) { if (chars[end] == '\\') { end = Math.Min(chars.Length, end + 2); continue; } if (chars[end++] == '\'') break; }
                Blank(i, end); i = end; continue;
            }
            i++;
        }
        return new string(chars);

        void Blank(int start, int end) { for (int p = start; p < end; p++) if (chars[p] is not ('\r' or '\n')) chars[p] = ' '; }
    }

    private static string StableHash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
    private static bool Contains(string source, string term) => source.Contains(term, StringComparison.OrdinalIgnoreCase);
}
