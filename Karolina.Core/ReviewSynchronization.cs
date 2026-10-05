using System.Text;
using System.Collections.Concurrent;

namespace Karolina.Core;

public sealed partial class TaskReviewStore
{
    private readonly List<FileSystemWatcher> watchers = [];
    private readonly ConcurrentDictionary<string, (long Ticks, long Bytes, ReviewFileState State)> cache = new(StringComparer.Ordinal);
    private long filesystemVersion = 1, synchronizedVersion;
    private readonly ConcurrentDictionary<string, string> summaryCache = new(StringComparer.Ordinal);
    private void WatchScopes()
    {
        foreach (string scope in new[] { "Assets", "Packages", "ProjectSettings" })
        {
            string folder = Path.Combine(root, scope); if (!Directory.Exists(folder)) continue;
            var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size };
            void Dirty(string path, bool descendants = false) {
                string relative = Path.GetRelativePath(root, path).Replace('\\', '/'); bool knownFile = cache.TryRemove(relative, out _);
                if (descendants && !knownFile) foreach (var key in cache.Keys.Where(key => key.StartsWith(relative + "/", StringComparison.Ordinal))) cache.TryRemove(key, out _);
                Interlocked.Increment(ref filesystemVersion);
            }
            watcher.Changed += (_, e) => Dirty(e.FullPath, Directory.Exists(e.FullPath)); watcher.Created += (_, e) => Dirty(e.FullPath, Directory.Exists(e.FullPath)); watcher.Deleted += (_, e) => Dirty(e.FullPath, true);
            watcher.Renamed += (_, e) => { Dirty(e.OldFullPath, true); Dirty(e.FullPath, true); };
            watcher.Error += (_, _) => { cache.Clear(); Interlocked.Increment(ref filesystemVersion); };
            watchers.Add(watcher); watcher.EnableRaisingEvents = true;
        }
    }
    public void Dispose() { foreach (var watcher in watchers) watcher.Dispose(); }
    private List<ReviewFile> Summarize(List<ReviewFile> files)
    {
        foreach (var file in files.Where(f => string.IsNullOrWhiteSpace(f.Brief))) file.Brief = summaryCache.GetOrAdd(file.Fingerprint, _ => ReviewSummary.Describe(file, Text(file.Before), Text(file.After)).Brief);
        return files;
    }
    public TaskReview Presentation(TaskReview task) { Summarize(task.Files.Where(f => !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).ToList()); return task; }
    private async Task<Dictionary<string, ReviewFileState>> IncrementalCapture(CancellationToken ct)
    {
        var result = new Dictionary<string, ReviewFileState>(StringComparer.Ordinal);
        foreach (string scope in new[] { "Assets", "Packages", "ProjectSettings" })
        {
            string folder = Path.Combine(root, scope); if (!Directory.Exists(folder)) continue;
            var stack = new Stack<string>(); stack.Push(folder);
            while (stack.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new IOException("审批范围不能包含链接目录");
                foreach (var child in Directory.EnumerateDirectories(directory)) stack.Push(child);
                foreach (var path in Directory.EnumerateFiles(directory))
                {
                    string relative = Path.GetRelativePath(root, path).Replace('\\', '/'); SafeFile(relative);
                    var info = new FileInfo(path); long stamp = info.LastWriteTimeUtc.Ticks, bytes = info.Length;
                    if (cache.TryGetValue(relative, out var previous) && previous.Ticks == stamp && previous.Bytes == bytes) result[relative] = previous.State;
                    else
                    {
                        long observed = Interlocked.Read(ref filesystemVersion);
                        var state = await FileState(relative, ct) ?? throw new IOException("文件写入尚未结束：" + relative); result[relative] = state;
                        info.Refresh();
                        if (observed == Interlocked.Read(ref filesystemVersion) && info.Exists && info.LastWriteTimeUtc.Ticks == stamp && info.Length == bytes) cache[relative] = (stamp, bytes, state);
                    }
                }
            }
        }
        return result;
    }
    public async Task<string[]> RefreshChanged(CancellationToken ct, bool force = false)
    {
        long version = Interlocked.Read(ref filesystemVersion);
        if (!force && version == Interlocked.Read(ref synchronizedVersion) || !await gate.WaitAsync(0, ct)) return [];
        try
        {
            var tasks = Directory.EnumerateFiles(storage, "*.json").Select(p => Read(Path.GetFileNameWithoutExtension(p))).Where(t => t.State is "执行" or "待审批").ToArray();
            if (tasks.Length == 0) { synchronizedVersion = version; return []; }
            var snapshot = await IncrementalCapture(ct); var updated = new List<string>();
            foreach (var task in tasks)
            {
                var changes = Changes(task, snapshot);
                if (changes.Select(f => (f.Path, f.Fingerprint)).SequenceEqual(task.Files.Where(f => !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(f => (f.Path, f.Fingerprint)))) continue;
                task.Files = Summarize(changes); task.Revision++; Save(task); updated.Add(task.Id);
            }
            synchronizedVersion = version; return updated.ToArray();
        }
        finally { gate.Release(); }
    }
    public async Task<TaskReview> SetOrigin(string id, int revision, string path, string fingerprint, string origin, string reason, CancellationToken ct)
    {
        if (origin is not ("agent" or "human" or "unknown") || string.IsNullOrWhiteSpace(reason) || reason.Length > 2000) throw new ArgumentException("确认来源需要人工/Agent归属和依据");
        await gate.WaitAsync(ct);
        try
        {
            var task = Read(id); Expected(task, revision);
            if (task.State is not ("执行" or "待审批")) throw new InvalidOperationException("已通过/退回的审批版本冻结");
            var file = task.Files.SingleOrDefault(f => f.Path == path && f.Fingerprint == fingerprint) ?? throw new ArgumentException("候选文件版本已经变化");
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(".meta不参与审批");
            var actual = await FileState(path, ct); if (actual?.Hash != file.After?.Hash) throw new InvalidOperationException("当前文件已变化，请等待后台同步");
            file.Origin = origin; file.OriginEvidence = "人工确认：" + reason.Trim(); file.Decision = "待审批";
            if(origin=="human")file.Explanation=null;
            if (origin == "agent") task.ProvenAgentVersions[path] = actual; else task.ProvenAgentVersions.Remove(path);
            if (task.State == "执行" && task.RunState is not ("running" or "启动" or "unknown" or "拓展工具执行")) task.State = "待审批";
            task.Revision++; Save(task); return task;
        }
        finally { gate.Release(); }
    }
    // Only successful file-change events with a frozen post-write hash qualify. Shell commands/MCP calls do not supply this proof.
    public async Task RecordAgentPatch(string id, string path, string patch, string eventId, CancellationToken ct)
    {
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return;
        await gate.WaitAsync(ct);
        try
        {
            SafeFile(path); var task = Read(id); if (task.State is not ("执行" or "待审批")) return;
            var current = await FileState(path, ct);
            task.Baseline.TryGetValue(path, out var baseline);
            var old = task.ProvenAgentVersions.TryGetValue(path, out var proven) ? proven : baseline;
            string? Raw(ReviewFileState? state) => state == null ? "" : Text(state, false);
            string? before = Raw(old), after = Raw(current);
            if (before == null || after == null || !ReviewProvenance.PatchMatches(before, after, patch)) return; // Unverifiable/mixed edits stay unknown.
            var snapshot = new Dictionary<string, ReviewFileState>(task.Baseline, StringComparer.Ordinal);
            foreach (var change in task.Files) { if (change.After == null) snapshot.Remove(change.Path); else snapshot[change.Path] = change.After; }
            if (current == null) snapshot.Remove(path); else snapshot[path] = current;
            task.Files = Summarize(Changes(task, snapshot));
            var changed = task.Files.SingleOrDefault(f => f.Path == path); if (changed == null) return;
            changed.Origin = "agent"; changed.OriginEvidence = "Codex成功文件写入回执：" + eventId + "；核对写后内容指纹。同期人工编辑同一文件时仍需人工核对归属。";
            task.ProvenAgentVersions[path] = current;
            task.Revision++; Save(task);
        }
        finally { gate.Release(); }
    }
    public TaskReview SaveSummary(string id, int revision, string summary)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 12000) throw new ArgumentException("请填写不超过一万二千字的任务摘要");
        gate.Wait(); try { var task = Read(id); Expected(task, revision); if (task.State is not ("执行" or "待审批")) throw new InvalidOperationException("此审批版本已冻结"); task.Summary = summary.Trim(); task.Revision++; Save(task); return task; } finally { gate.Release(); }
    }
}
