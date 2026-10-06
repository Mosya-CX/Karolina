namespace Karolina.Core;

public sealed record ProjectGraphCoverage(string[] Included, string[] Excluded, int FileNodes, int CodeFiles, int UnityResources, int Documents, int OtherFiles, int IsolatedNodes);

public sealed partial class ProjectGraphService
{
    // 同一文件策略供节点索引、时效清单和观察器使用；不读取本机助手私有状态。
    private static bool IgnoreFile(string relative)
    {
        if (relative.Split('/').Any(part => ExcludedDirectories.Contains(part))) return true;
        string extension = Path.GetExtension(relative);
        if (new[] { ".tmp", ".log", ".user", ".userprefs", ".suo", ".pidb", ".pdb", ".mdb" }.Contains(extension, StringComparer.OrdinalIgnoreCase)) return true;
        return !relative.Contains('/') && new[] { ".csproj", ".sln", ".unityproj" }.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private IEnumerable<(string Relative, string Full)> ProjectFiles(Action<string, string>? warn = null)
    {
        var stack = new Stack<string>(); stack.Push(project.Root);
        int count = 0;
        while (stack.TryPop(out string? folder))
        {
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            { warn?.Invoke(Path.GetRelativePath(project.Root, folder).Replace('\\', '/'), "链接目录未遍历"); continue; }
            foreach (string child in Directory.EnumerateDirectories(folder).Order(StringComparer.Ordinal).Reverse())
                if (!ExcludedDirectories.Contains(Path.GetFileName(child))) stack.Push(child);
            foreach (string file in Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal))
            {
                string relative = Path.GetRelativePath(project.Root, file).Replace('\\', '/');
                if (IgnoreFile(relative)) continue;
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                { warn?.Invoke(relative, "链接文件未读取"); continue; }
                if (++count > MaxFiles * 2) throw new InvalidDataException("工程事实源文件数超过上限");
                yield return (relative, file);
            }
        }
    }

    private static string FileKind(string path)
    {
        if (new[] { ".cs", ".lua", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".py", ".ps1", ".sh", ".shader", ".hlsl", ".cginc", ".c", ".cpp", ".h" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) return "代码文件";
        if (Path.GetExtension(path).Equals(".md", StringComparison.OrdinalIgnoreCase)) return "工程文档";
        if (new[] { "Assets/", "Packages/", "ProjectSettings/" }.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return "Unity资源";
        return "工程文件";
    }

    private static ProjectGraphCoverage Coverage(ProjectGraphSnapshot value)
    {
        var linked = value.Edges.SelectMany(e => new[] { e.From, e.To }).ToHashSet(StringComparer.Ordinal);
        return new(value.Nodes.Select(n => n.Path.Contains('/') ? n.Path.Split('/')[0] + "/" : "工程根目录文件").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), ExcludedDirectories.Order(StringComparer.Ordinal).Concat(["Unity .meta 展示节点", "临时/日志文件", "根目录自动生成的 .csproj/.sln"]).ToArray(),
            value.Nodes.Count(n => n.Kind != "C# 类型"), value.Nodes.Count(n => n.Kind == "代码文件"), value.Nodes.Count(n => n.Kind == "Unity资源"), value.Nodes.Count(n => n.Kind == "工程文档"), value.Nodes.Count(n => n.Kind == "工程文件"), value.Nodes.Count(n => !linked.Contains(n.Id)));
    }
}
