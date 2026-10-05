using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Karolina.Core;

public sealed record ReviewFileState(string Hash, long Bytes, string? TextBlob);
public sealed record ReviewNote(string Text, string? Side = null, int? Line = null);
public sealed record RetrospectiveFile(string Path, byte[]? Before, string? ExpectedAfterHash, string Reason);
public sealed class ReviewFile
{
    public string Path { get; set; } = "";
    public string? OriginalPath { get; set; }
    public string Kind { get; set; } = "";
    public ReviewFileState? Before { get; set; }
    public ReviewFileState? After { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Decision { get; set; } = "待审批";
    public List<ReviewNote> Notes { get; set; } = [];
    public string Origin { get; set; } = "unknown";
    public string OriginEvidence { get; set; } = "尚无可核对的写入来源";
    public string Brief { get; set; } = "";
    public ReviewNarrative? Explanation { get; set; }
}
public sealed record ReviewAttempt(int Round, string Summary, string Feedback, DateTimeOffset ReturnedAt, ReviewFile[] Files);
public sealed class TaskReview
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlanId { get; set; } = "";
    public string Title { get; set; } = "";
    public string State { get; set; } = "执行";
    public int Revision { get; set; } = 1;
    public int Round { get; set; } = 1;
    public DateTimeOffset Started { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset Updated { get; set; } = DateTimeOffset.Now;
    public string Summary { get; set; } = "";
    public string Feedback { get; set; } = "";
    public string? ThreadId { get; set; }
    public string? RunId { get; set; }
    public string RunState { get; set; } = "尚未执行";
    public string BaselineKind { get; set; } = "任务开始实测";
    public string BaselineDescription { get; set; } = "执行前真实工作区快照；不证明同期外部写入的作者。";
    public int ImportRevision { get; set; }
    public Dictionary<string, string> SelectionEvidence { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, ReviewFileState> Baseline { get; set; } = new(StringComparer.Ordinal);
    public List<ReviewFile> Files { get; set; } = [];
    public List<ReviewAttempt> Attempts { get; set; } = [];
    public Dictionary<string, ReviewFileState?> ProvenAgentVersions { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>任务开始前的真实工作区快照；从不读取 HEAD、修改索引或回滚 Unity 文件。</summary>
public sealed partial class TaskReviewStore : IDisposable
{
    private const int TextLimit = 2_000_000;
    private readonly string root, storage, blobs;
    private readonly ProjectContext context;
    private readonly SemaphoreSlim gate = new(1, 1);
    public TaskReviewStore(string project)
    {
        context = new ProjectContext(project);
        root = context.Root;
        storage = context.StatePath("reviews");
        blobs = context.StatePath("reviews/text"); Directory.CreateDirectory(blobs);
        WatchScopes();
    }
    private string RecordPath(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("无效任务编号");
        return context.StatePath("reviews/"+id+".json");
    }
    private TaskReview Read(string id) => JsonSerializer.Deserialize<TaskReview>(File.ReadAllText(RecordPath(id)), DocumentLibrary.Json) ?? throw new InvalidDataException("任务记录损坏");
    private void Save(TaskReview task)
    {
        task.Updated = DateTimeOffset.Now;
        string path = RecordPath(task.Id),temp=context.StatePath("reviews/"+task.Id+".json.tmp"); File.WriteAllText(temp, JsonSerializer.Serialize(task, DocumentLibrary.Json)); File.Move(temp, path, true);
    }
    private static void Expected(TaskReview task, int revision)
    {
        if (task.Revision != revision) throw new InvalidOperationException("任务已更新，请重新载入审批；旧页面不能批准新内容");
    }
    private string SafeFile(string relative)
    {
        if (relative.Contains('\\') || relative.Contains(':') || relative.Split('/').Any(p => p is "" or "." or "..")) throw new ArgumentException("文件必须为规范项目相对路径");
        if (!(relative.StartsWith("Assets/", StringComparison.Ordinal) || relative.StartsWith("Packages/", StringComparison.Ordinal) || relative.StartsWith("ProjectSettings/", StringComparison.Ordinal))) throw new ArgumentException("文件不在 Unity 审批范围");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("文件路径越界");
        for (FileSystemInfo? node = new FileInfo(path); node != null && node.FullName.Length >= root.Length; node = node is DirectoryInfo d ? d.Parent : ((FileInfo)node).Directory)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("审批范围含链接路径，不能证明文件归属：" + relative);
        return path;
    }
    private async Task<ReviewFileState?> FileState(string relative, CancellationToken ct)
    {
        string path = SafeFile(relative);
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length; string hash; string? blob = null;
        if (length <= TextLimit)
        {
            byte[] bytes = new byte[checked((int)length)]; await stream.ReadExactlyAsync(bytes, ct);
            hash = RequirementStore.Hash(bytes);
            try
            {
                string text = new UTF8Encoding(false, true).GetString(bytes);
                if (!text.Contains('\0'))
                {
                    blob = hash;
                    string destination = context.StatePath("reviews/text/"+hash);
                    if (!File.Exists(destination)) await File.WriteAllBytesAsync(destination, bytes, ct);
                }
            }
            catch (DecoderFallbackException) { }
        }
        else hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        return new(hash, length, blob);
    }
    public async Task<Dictionary<string, ReviewFileState>> Capture(CancellationToken ct = default)
    {
        var result = new Dictionary<string, ReviewFileState>(StringComparer.Ordinal);
        foreach (string scope in new[] { "Assets", "Packages", "ProjectSettings" })
        {
            string folder = Path.Combine(root, scope); if (!Directory.Exists(folder)) continue;
            var stack = new Stack<string>(); stack.Push(folder);
            while (stack.TryPop(out var directory))
            {
                ct.ThrowIfCancellationRequested();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Unity 审批范围含链接目录：" + directory);
                foreach (string child in Directory.EnumerateDirectories(directory)) stack.Push(child);
                foreach (string file in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
                {
                    string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                    result.Add(relative, await FileState(relative, ct) ?? throw new IOException("快照期间文件消失，请等待外部写入结束后重试：" + relative));
                }
            }
        }
        return result;
    }
    public TaskReview[] List()
    {
        gate.Wait(); try { return Directory.EnumerateFiles(context.StatePath("reviews"), "*.json").Select(p => Read(Path.GetFileNameWithoutExtension(p))).OrderByDescending(t => t.Updated).ToArray(); }
        finally { gate.Release(); }
    }
    public TaskReview Get(string id) { gate.Wait(); try { return Read(id); } finally { gate.Release(); } }
    public async Task<TaskReview> Start(string planId, string title, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (Directory.EnumerateFiles(context.StatePath("reviews"), "*.json").Select(p => Read(Path.GetFileNameWithoutExtension(p))).Any(t => t.State != "已通过")) throw new InvalidOperationException("当前工程已有未结束的审批任务，请继续原任务");
            var task = new TaskReview { PlanId = planId, Title = title, Baseline = await Capture(ct) };
            Save(task); return task;
        }
        finally { gate.Release(); }
    }
    // 仅供用户明确授权的维护导入；产品本身不运行 Git，不把历史参考冒充开始快照。
    public async Task<TaskReview> ImportInterrupted(string planId, string title, string description, string summary, RetrospectiveFile[] files, CancellationToken ct = default, string? replaceId = null)
    {
        if (string.IsNullOrWhiteSpace(description) || string.IsNullOrWhiteSpace(summary) || files.Length == 0 || files.Select(f => f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length)
            throw new ArgumentException("事后整理需要来源说明、摘要和不重复的候选文件");
        foreach (var file in files)
        {
            SafeFile(file.Path);
            if (Path.GetRelativePath(root, SafeFile(file.Path)).Replace('\\', '/') != file.Path || string.IsNullOrWhiteSpace(file.Reason)) throw new ArgumentException("候选路径需规范化并填写关联证据");
        }
        await gate.WaitAsync(ct);
        try
        {
            TaskReview? previous = replaceId == null ? null : Read(replaceId);
            if (previous != null && (previous.ImportRevision != previous.Revision || previous.PlanId != planId || previous.BaselineKind != "事后整理（历史参考）" || previous.State != "待审批" || previous.ThreadId != null || previous.RunId != null || previous.Attempts.Count != 0 || previous.Files.Any(f => f.Decision != "待审批" || f.Notes.Count != 0) || previous.SelectionEvidence.Keys.Except(files.Select(f => f.Path), StringComparer.Ordinal).Any()))
                throw new InvalidOperationException("只能补充尚无人工意见/运行记录的同一事后条目，并保留原收录文件");
            if (Directory.EnumerateFiles(storage, "*.json").Select(p => Read(Path.GetFileNameWithoutExtension(p))).Any(t => t.State != "已通过" && t.Id != replaceId)) throw new InvalidOperationException("当前工程已有未结束的审批任务，请继续原任务");
            var current = await Capture(ct);
            var task = new TaskReview { PlanId = planId, Title = title, BaselineKind = "事后整理（历史参考）", BaselineDescription = description,
                Summary = summary, State = "待审批", RunState = "重做中断，未完成（用户确认）", Baseline = new(previous?.Baseline ?? current, StringComparer.Ordinal) };
            if (previous != null) { task.Id = previous.Id; task.Started = previous.Started; task.Revision = previous.Revision + 1; }
            foreach (var file in files)
            {
                current.TryGetValue(file.Path, out var actual);
                if (actual?.Hash != file.ExpectedAfterHash) throw new InvalidOperationException("整理期间候选内容变化，请重新核对：" + file.Path);
                if (file.Before == null) task.Baseline.Remove(file.Path);
                else
                {
                    string hash = RequirementStore.Hash(file.Before); string? blob = null;
                    if (file.Before.Length <= TextLimit)
                    {
                        try { if (!new UTF8Encoding(false, true).GetString(file.Before).Contains('\0')) { blob = hash; await File.WriteAllBytesAsync(context.StatePath("reviews/text/"+hash), file.Before, ct); } }
                        catch (DecoderFallbackException) { }
                    }
                    task.Baseline[file.Path] = new(hash, file.Before.LongLength, blob);
                }
                task.SelectionEvidence[file.Path] = file.Reason;
            }
            task.Files = Changes(task, current);
            if (task.Files.Count == 0) throw new InvalidOperationException("候选文件没有实际内容变化");
            task.ImportRevision = task.Revision;
            Save(task); return task;
        }
        finally { gate.Release(); }
    }
    private static List<ReviewFile> Changes(TaskReview task, Dictionary<string, ReviewFileState> after)
    {
        var changes = new List<ReviewFile>();
        foreach (string path in task.Baseline.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
            task.Baseline.TryGetValue(path, out var before); after.TryGetValue(path, out var current);
            if (before?.Hash == current?.Hash) continue;
            string fingerprint = RequirementStore.Hash(Encoding.UTF8.GetBytes(path + "\0" + before?.Hash + "\0" + current?.Hash));
            var previous = task.Files.Find(f => f.Path == path && f.Fingerprint == fingerprint);
            changes.Add(new ReviewFile { Path = path, Before = before, After = current, Kind = before == null ? "新增" : current == null ? "删除" : "修改", Fingerprint = fingerprint, Decision = previous?.Decision ?? "待审批", Notes = previous?.Notes ?? [], Origin = previous?.Origin ?? "unknown", OriginEvidence = previous?.OriginEvidence ?? "尚无可核对的写入来源", Brief = previous?.Brief ?? "", Explanation=previous?.Explanation });
        }
        foreach(var added in changes.Where(f=>f.Before==null).ToArray())
        {
            var deleted=changes.SingleOrDefault(f=>f.After==null&&f.Before?.Hash==added.After?.Hash&&StringComparer.OrdinalIgnoreCase.Equals(f.Path,added.Path));
            if(deleted==null)continue;
            added.OriginalPath=deleted.Path;added.Before=deleted.Before;added.Kind="重命名（大小写）";
            added.Fingerprint=RequirementStore.Hash(Encoding.UTF8.GetBytes(deleted.Path+"\0"+added.Path+"\0"+added.Before?.Hash+"\0"+added.After?.Hash));
            var previous=task.Files.Find(f=>f.Path==added.Path&&f.Fingerprint==added.Fingerprint);
            added.Decision=previous?.Decision??"待审批";added.Notes=previous?.Notes??[];changes.Remove(deleted);
            added.Origin=previous?.Origin??"unknown";added.OriginEvidence=previous?.OriginEvidence??"尚无可核对的写入来源";added.Brief=previous?.Brief??"";
            added.Explanation=previous?.Explanation;
        }
        return changes;
    }
    public async Task<TaskReview> Submit(string id, int revision, string summary, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Length > 12000) throw new ArgumentException("请填写本次任务整体做了什么（不超过一万二千字）");
        await gate.WaitAsync(ct);
        try
        {
            var task = Read(id); Expected(task, revision);
            if (task.State is "已通过" or "待再执行") throw new InvalidOperationException("已通过或已退回的任务不能提交旧版本");
            task.Files = Summarize(Changes(task, await Capture(ct))); task.Summary = summary.Trim(); task.State = "待审批"; task.Revision++; Save(task); return task;
        }
        finally { gate.Release(); }
    }
    public async Task<TaskReview> DecideFile(string id, int revision, string path, string fingerprint, bool? accept, ReviewNote[] notes, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var task = Read(id); Expected(task, revision);
            if (task.State != "待审批") throw new InvalidOperationException("任务尚未提交审批");
            var file = task.Files.SingleOrDefault(f => f.Path == path) ?? throw new ArgumentException("文件不属于本任务变化");
            if (file.Origin != "agent" || path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("只能审批确认属于Agent的文件；人工文件和来源待确认文件不进入审批");
            var latest=await Capture(ct);latest.TryGetValue(path,out var actual);
            if (fingerprint != file.Fingerprint || actual?.Hash != file.After?.Hash || file.OriginalPath!=null&&latest.ContainsKey(file.OriginalPath)) throw new InvalidOperationException("文件内容或路径已变化，请刷新并重新提交审批");
            if(accept==true&&!HasExplanation(file))throw new InvalidOperationException("请先补齐当前版本的文件操作说明，再批准");
            if (notes.Length > 100 || notes.Any(n => string.IsNullOrWhiteSpace(n.Text) || n.Text.Length > 4000 || n.Side != null && n.Side is not ("before" or "after") || n.Line != null && (n.Line < 1 || n.Side == null))) throw new ArgumentException("注释无效：填写意见，行号必须为正数并指定变更前/后");
            if ((accept == false || accept == null && file.Decision == "不通过") && notes.Length == 0) throw new ArgumentException("驳回文件需要至少一条解释意见");
            foreach (var note in notes.Where(n => n.Line != null))
            {
                var version = note.Side == "before" ? file.Before : file.After;
                string? text = Text(version);
                if (text == null || note.Line > text.Split('\n').Length) throw new ArgumentException("注释行号不属于这版文本");
            }
            if(accept != null)file.Decision = accept.Value ? "通过" : "不通过";
            file.Notes = notes.ToList(); task.Revision++; Save(task); return task;
        }
        finally { gate.Release(); }
    }
    public async Task<TaskReview> DecideTask(string id, int revision, bool accept, string feedback, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var task = Read(id); Expected(task, revision);
            if (task.State != "待审批") throw new InvalidOperationException("任务尚未提交审批");
            if (feedback.Length > 12000) throw new ArgumentException("整体意见过长");
            task.Feedback = feedback.Trim();
            var fresh = Changes(task, await Capture(ct));
            if (!fresh.Select(f => (f.Path, f.Fingerprint)).SequenceEqual(task.Files.Where(f => !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)).Select(f => (f.Path, f.Fingerprint)))) throw new InvalidOperationException("审批期间工程内容已变化，等待后台同步最新差异");
            if (accept)
            {
                if (fresh.Any(f => f.Origin == "unknown")) throw new InvalidOperationException("还有来源待确认的文件，请先确认人工/Agent归属");
                if (fresh.Any(f => f.Origin == "agent" && f.Decision == "不通过")) throw new InvalidOperationException("还有不通过的文件，请处理后再批准整体任务");
                if (fresh.Any(f => f.Origin == "agent" && !HasExplanation(f))) throw new InvalidOperationException("还有Agent文件缺少当前版本的操作说明，请先补齐再批准");
                task.Files = fresh;
                foreach (var file in task.Files.Where(f => f.Origin == "agent")) file.Decision = "通过";
                task.State = "已通过";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(feedback) && !task.Files.Any(f => f.Decision == "不通过")) throw new ArgumentException("请填写整体退回原因或驳回至少一个文件");
                task.Feedback = feedback.Trim();
                // 当前案保持唯一；轮次证据属于任务审批记录，不写成计划演进。
                task.Attempts.Add(new(task.Round, task.Summary, task.Feedback, DateTimeOffset.Now, JsonSerializer.Deserialize<ReviewFile[]>(JsonSerializer.Serialize(task.Files, DocumentLibrary.Json), DocumentLibrary.Json)!));
                task.State = "待再执行";
            }
            task.Revision++; Save(task); return task;
        }
        finally { gate.Release(); }
    }
    public TaskReview Resume(string id, int revision)
    {
        gate.Wait();
        try
        {
            var task = Read(id); Expected(task, revision);
            if (task.State != "待再执行") throw new InvalidOperationException("只有已退回的任务可以返回执行");
            task.State = "执行"; task.Round++; task.Revision++; Save(task); return task;
        }
        finally { gate.Release(); }
    }
    public void RecordRun(string id, string runId, string? threadId, string runState, string? summary = null)
    {
        gate.Wait(); try { var task = Read(id); task.RunId = runId; if(threadId!=null)task.ThreadId = threadId; task.RunState = runState; if (!string.IsNullOrWhiteSpace(summary)) task.Summary = summary[..Math.Min(summary.Length, 12000)]; task.Revision++; Save(task); }
        finally { gate.Release(); }
    }
    public string FeedbackPrompt(TaskReview task) => $"计划：{task.PlanId}，任务：{task.Title}，第 {task.Round + 1} 次执行。保留既有工作，不自动回滚。\n整体审批意见：{task.Feedback}\n" + string.Join('\n', task.Files.Where(f => f.Decision == "不通过").Select(f => f.Path + "：\n" + string.Join('\n', f.Notes.Select(n => (n.Line == null ? "文件" : $"{(n.Side == "before" ? "变更前" : "变更后")}第 {n.Line} 行") + "：" + n.Text))));
    private string? Text(ReviewFileState? version, bool trimBom = true)
    {
        if (version?.TextBlob == null) return null;
        if (!System.Text.RegularExpressions.Regex.IsMatch(version.TextBlob, "^[a-f0-9]{64}$")) throw new InvalidDataException("文本快照编号损坏");
        byte[] bytes = File.ReadAllBytes(context.StatePath("reviews/text/"+version.TextBlob));
        if (RequirementStore.Hash(bytes) != version.Hash) throw new InvalidDataException("文本快照内容损坏");
        string text = Encoding.UTF8.GetString(bytes); return trimBom ? text.TrimStart('\uFEFF') : text;
    }
    public object Diff(string id, string path)
    {
        var task = Get(id); var file = task.Files.SingleOrDefault(f => f.Path == path) ?? throw new ArgumentException("文件不属于本次任务");
        if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("导入元数据不参与任务审批");
        string? before = IsCodeFile(path)?Text(file.Before):null, after = IsCodeFile(path)?Text(file.After):null;
        bool text = IsCodeFile(path) && (file.Before == null || before != null) && (file.After == null || after != null);
        bool tooManyLines = text && (before?.Count(c=>c=='\n') ?? 0) + (after?.Count(c=>c=='\n') ?? 0) > 6000;
        if(tooManyLines)text=false;
        return new { file, text, isCode=IsCodeFile(path), explanation=file.Explanation, lines = text ? LineDiff(before ?? "", after ?? "") : [], message = text ? "" : IsCodeFile(path) ? "代码超过差异展示容量；先阅读Agent说明。" : "资源审批仅展示Agent操作说明，不展示资源序列化内容。" };
    }
    // 有界 LCS；大文本按共同前后缀显示完整变化，避免二次复杂度冻结页面。
    private static object[] LineDiff(string before, string after)
    {
        string[] a = before.Length == 0 ? [] : before.Replace("\r\n", "\n").Split('\n'), b = after.Length == 0 ? [] : after.Replace("\r\n", "\n").Split('\n');
        var lines = new List<object>(); int old = 1, current = 1;
        void Add(string kind, string text) { lines.Add(new { kind, text, before = kind == "added" ? (int?)null : old++, after = kind == "deleted" ? (int?)null : current++ }); }
        int prefix = 0; while (prefix < Math.Min(a.Length, b.Length) && a[prefix] == b[prefix]) { Add("same", a[prefix]); prefix++; }
        int endA = a.Length, endB = b.Length;
        while (endA > prefix && endB > prefix && a[endA-1] == b[endB-1]) { endA--; endB--; }
        int m = endA-prefix, n = endB-prefix;
        if ((long)m*n <= 1_000_000)
        {
            var lengths = new int[m+1,n+1];
            for (int i=m-1;i>=0;i--) for (int j=n-1;j>=0;j--) lengths[i,j]=a[prefix+i]==b[prefix+j]?1+lengths[i+1,j+1]:Math.Max(lengths[i+1,j],lengths[i,j+1]);
            int x=0,y=0;
            while(x<m||y<n)
                if(x<m&&y<n&&a[prefix+x]==b[prefix+y]) { Add("same",a[prefix+x]); x++;y++; }
                else if(x<m&&(y==n||lengths[x+1,y]>=lengths[x,y+1]))Add("deleted",a[prefix+x++]);
                else Add("added",b[prefix+y++]);
        }
        else { for(int i=prefix;i<endA;i++)Add("deleted",a[i]);for(int j=prefix;j<endB;j++)Add("added",b[j]); }
        while(endA<a.Length) { Add("same",a[endA++]);endB++; }
        return lines.ToArray();
    }
}
