namespace Karolina.Core;

public sealed partial class ProjectGraphService
{
    private void WatchProject()
    {
        // 根目录只观察自身；递归观察各工程目录，避免把 Library 等生成树接入观察器。
        WatchFolder(project.Root, false);
        foreach (string folder in Directory.EnumerateDirectories(project.Root))
        {
            if (!ExcludedDirectories.Contains(Path.GetFileName(folder)) && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0) WatchFolder(folder, true);
        }
    }
    private bool disposed;
    private void WatchFolder(string folder, bool recursive)
    {
        lock (gate)
        {
            if (disposed || watchers.Any(w => w.Path.Equals(folder, StringComparison.OrdinalIgnoreCase))) return;
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = recursive,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };
            watcher.Changed += Changed; watcher.Created += Changed; watcher.Deleted += Changed; watcher.Renamed += Changed;
            watcher.Error += (_, _) => MarkStale("文件观察器丢失事件，请刷新索引");
            watchers.Add(watcher); watcher.EnableRaisingEvents = true;
        }
    }

    private void Changed(object sender, FileSystemEventArgs e)
    {
        bool topLevel = Path.GetDirectoryName(e.FullPath)!.Equals(project.Root, StringComparison.OrdinalIgnoreCase);
        if (topLevel && e.ChangeType == WatcherChangeTypes.Deleted) ForgetFolder(e.FullPath);
        if (topLevel && e is RenamedEventArgs moved) ForgetFolder(moved.OldFullPath);
        // Windows 会延迟通知父目录的时间变化；目录本身的 Changed 不改变索引内容。
        if (e.ChangeType == WatcherChangeTypes.Changed && Directory.Exists(e.FullPath)) return;
        string relative = Path.GetRelativePath(project.Root, e.FullPath).Replace('\\', '/');
        bool oldIncluded = e is RenamedEventArgs renamed && !IgnoreFile(Path.GetRelativePath(project.Root, renamed.OldFullPath).Replace('\\', '/'));
        if (IgnoreFile(relative) && !oldIncluded) return;
        if (!IgnoreFile(relative) && Directory.Exists(e.FullPath) && Path.GetDirectoryName(e.FullPath)!.Equals(project.Root, StringComparison.OrdinalIgnoreCase) && (File.GetAttributes(e.FullPath) & FileAttributes.ReparsePoint) == 0)
            WatchFolder(e.FullPath, true);
        MarkStale("工程文件已变化：" + relative);
    }

    private void ForgetFolder(string folder)
    {
        lock (gate)
        {
            foreach (var watcher in watchers.Where(w => w.Path.Equals(folder, StringComparison.OrdinalIgnoreCase)).ToArray())
            { watcher.Dispose(); watchers.Remove(watcher); }
        }
    }

    private void MarkStale(string reason)
    {
        lock (gate) { changeVersion++; stale = true; staleReason = reason; }
    }

    private void CheckFreshness(ProjectGraphSnapshot saved)
    {
        long version = Interlocked.Read(ref changeVersion);
        bool same = false;
        string reason;
        try
        {
            same = saved.SchemaVersion == Schema && saved.Files is not null && StampsMatch(saved.Files);
            reason = same ? "文件清单已核对；仍需读取真实文件确认语义" : "工程文件与索引不一致，或旧版索引缺少依据，请刷新索引";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { reason = "无法核对索引时效：" + e.Message; }
        lock (gate)
        {
            if (!ReferenceEquals(snapshot, saved) || version != changeVersion) return;
            stale = !same; staleReason = reason;
        }
    }

    private bool StampsMatch(ProjectGraphFileStamp[] expected) => expected.SequenceEqual(ReadStamps());

    private ProjectGraphFileStamp[] ReadStamps()
    {
        var result = new List<ProjectGraphFileStamp>();
        foreach (var file in ProjectFiles())
        {
            var info = new FileInfo(file.Full);
            result.Add(new(file.Relative, info.Length, info.LastWriteTimeUtc.Ticks));
        }
        return result.OrderBy(f => f.Path, StringComparer.Ordinal).ToArray();
    }

    private static int[] LineOffsets(string source)
    {
        var lines = new List<int>();
        for (int i = 0; i < source.Length; i++) if (source[i] == '\n') lines.Add(i);
        return lines.ToArray();
    }
    private static int LineAt(int[] lines, int index)
    {
        int found = Array.BinarySearch(lines, index);
        return (found < 0 ? ~found : found) + 1;
    }
    private static string ReferenceField(string source, int index)
    {
        int start = source.LastIndexOf('\n', Math.Max(0, index - 1));
        string prefix = source[(start + 1)..index].Trim();
        int colon = prefix.IndexOf(':');
        return colon > 0 ? prefix[..colon].Trim() : "未识别（不推断组件语义）";
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            foreach (var watcher in watchers) watcher.Dispose();
            watchers.Clear();
        }
    }
}
