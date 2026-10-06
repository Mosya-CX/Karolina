using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Karolina.Core;

public sealed record ProcessResult(int ExitCode, string Stdout, string Stderr);
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(string executable, IEnumerable<string> args, string root, string? input = null, Action<string>? onOutput = null, CancellationToken ct = default, bool ownDescendants = false)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var job = ownDescendants && OperatingSystem.IsWindows() ? new OwnedProcessJob() : null;
        using var child=job!=null?WindowsToolProcess.Start(start,job):null;
        using var process = child?.Process??new Process { StartInfo = start };
        if (child==null&&!process.Start()) throw new IOException("Process did not start");
        var stdout=child?.Output??process.StandardOutput;var stderrReader=child?.Error??process.StandardError;var stdin=child?.Input??process.StandardInput;
        void Terminate()
        {
            job?.Dispose();
            try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { }
        }
        using var registration = ct.Register(Terminate);
        var output = new StringBuilder(); bool overflow = false;
        async Task<string> Capture(StreamReader reader)
        {
            var capture = new StringBuilder(); var buffer = new char[8192]; int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(),ct)) > 0)
                if (capture.Length + count <= 4_000_000) capture.Append(buffer,0,count); else overflow = true;
            return capture.ToString();
        }
        async Task Drain()
        {
            if (onOutput == null) { output.Append(await Capture(stdout)); return; }
            while (await stdout.ReadLineAsync(ct) is { } line) { if (output.Length + line.Length + 2 <= 4_000_000) output.AppendLine(line); else overflow=true; onOutput(line); }
        }
        var drain = Drain(); var error = Capture(stderrReader);
        try
        {
            if (input is not null) await stdin.WriteAsync(input.AsMemory(),ct);
            stdin.Close();
            await process.WaitForExitAsync(ct); await drain;
            string stderr = await error;
            if (overflow) throw new IOException("Process output exceeded 4 MB capture limit; result cannot be displayed completely");
            return new(process.ExitCode, output.ToString(), stderr);
        }
        catch
        {
            Terminate();
            // 父进程退出后，后代可能仍持有管道。读取遵循取消，并且清理有界。
            try { await Task.WhenAll(process.WaitForExitAsync(),drain,error).WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { _=drain.ContinueWith(t=>_ = t.Exception,TaskContinuationOptions.OnlyOnFaulted);_=error.ContinueWith(t=>_ = t.Exception,TaskContinuationOptions.OnlyOnFaulted); }
            throw;
        }
    }
}
public sealed record Change(string Status, string Path, string? OriginalPath = null)
{
    public override string ToString() => $"{Status} {Path}";
}
public sealed record RunProgressEvent(DateTimeOffset At, string Stage, string Source, string Detail, string Outcome);
public sealed class RunRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Started { get; set; } = DateTimeOffset.Now;
    public string Project { get; set; } = "";
    public string Requirement { get; set; } = "";
    public string Plan { get; set; } = "";
    public string Sandbox { get; set; } = "read-only";
    public string Model { get; set; } = "Harness default";
    public string Mode { get; set; } = "execute-task";
    public string Prompt { get; set; } = "";
    public string State { get; set; } = "Created";
    public string? ThreadId { get; set; }
    public int? ExitCode { get; set; }
    public string? Error { get; set; }
    public string? AfterStateError { get; set; }
    public string? ReviewId { get; set; }
    public string ProgressStage { get; set; } = "等待启动";
    public string ProgressMessage { get; set; } = "正在准备任务";
    public DateTimeOffset? ProgressUpdated { get; set; }
    public string? ReasoningItemId { get; set; }
    public string? ReasoningSummary { get; set; }
    public AgentPlanStep[] ActivityPlan { get; set; } = [];
    public List<RunProgressEvent> Progress { get; set; } = [];
    public Change[] Baseline { get; set; } = [];
    public Change[] After { get; set; } = [];
}
public sealed record AgentPlanStep(string Step,string Status);
public sealed class EvidenceStore
{
    public string Root { get; }
    public EvidenceStore(string? root = null) { Root = root ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karolina", "runs"); Directory.CreateDirectory(Root); }
    public string DirectoryFor(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-f0-9]{32}$")) throw new ArgumentException("Invalid run ID");
        var p = System.IO.Path.Combine(Root, id); Directory.CreateDirectory(p); return p;
    }
    public void Save(RunRecord record)
    {
        string path = System.IO.Path.Combine(DirectoryFor(record.Id), "run.json"), temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(record, RequirementStore.Json)); File.Move(temp, path, true);
    }
    public RunRecord? Read(string id)
    {
        string path = System.IO.Path.Combine(DirectoryFor(id), "run.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(path), RequirementStore.Json) : null;
    }
    public void Validation(string id, string tool, string response) => File.WriteAllText(System.IO.Path.Combine(DirectoryFor(id), $"unity-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json"), JsonSerializer.Serialize(new { tool, response, capturedAt = DateTimeOffset.Now }, RequirementStore.Json));
}
public sealed class CodexRunner(EvidenceStore evidence)
{
    public async Task<RunRecord> Run(string executable, RequirementStore requirements, string reqId, string planPath, string instruction, string sandbox, string? model, Action<string>? progress = null, CancellationToken ct = default)
    {
        requirements.AssertCurrent();
        var req = requirements.Find(reqId);
        if (req.Status != "Active" || !requirements.IsEffective(req)) throw new InvalidOperationException("Task requires an effective Active requirement");
        if (sandbox != "read-only" && sandbox != "workspace-write") throw new ArgumentException("Invalid sandbox");
        var plan = requirements.SafePath(planPath, "Docs/Plans");
        string planText = File.ReadAllText(plan);
        string header = new System.Text.RegularExpressions.Regex(@"(?m)^## ").Split(planText, 2)[0];
        var links = System.Text.RegularExpressions.Regex.Matches(header, @"(?m)^(?:Requirements|Requirement sources): ([^\r\n]+)").SelectMany(m => System.Text.RegularExpressions.Regex.Matches(m.Groups[1].Value, @"\bREQ-[A-Z0-9-]+\b").Select(x => x.Value));
        string allowed = sandbox == "read-only" ? "Active|Verification Pending|Completed|Deferred" : "Active";
        if (!links.Contains(reqId, StringComparer.Ordinal) || !System.Text.RegularExpressions.Regex.IsMatch(header, $@"(?m)^Status: ({allowed})\r?$")) throw new InvalidOperationException("Task needs an explicitly linked plan; workspace-write requires Active status");
        if (sandbox == "workspace-write")
        {
            var active = Directory.GetFiles(System.IO.Path.Combine(requirements.Root, "Docs/Plans"), "*.md").Where(p => System.Text.RegularExpressions.Regex.IsMatch(new System.Text.RegularExpressions.Regex(@"(?m)^## ").Split(File.ReadAllText(p),2)[0], @"(?m)^Status: Active\r?$")).ToArray();
            if (active.Length != 1 || !System.IO.Path.GetFullPath(active[0]).Equals(plan, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Exactly one Active plan is required for workspace-write");
        }
        if (string.IsNullOrWhiteSpace(instruction)) throw new ArgumentException("Instruction required");
        using var writeLock = sandbox == "workspace-write" ? WorkspaceLock.Acquire(requirements.Root) : null;
        var run = new RunRecord { Project = requirements.Root, Requirement = req.Id, Plan = planPath, Prompt = instruction, Sandbox = sandbox, Model = string.IsNullOrWhiteSpace(model) ? "Harness default" : model, State = "Running" };
        var snapshots = new TaskReviewStore(requirements.Root); run.Baseline = (await snapshots.Capture(ct)).Select(f => new Change(f.Value.Hash, f.Key)).ToArray(); evidence.Save(run);
        var dir = evidence.DirectoryFor(run.Id);
        using var events = new StreamWriter(System.IO.Path.Combine(dir, "events.jsonl")) { AutoFlush = true };
        bool completed = false, failed = false;
        var args = new List<string> { "exec", "--json", "-C", requirements.Root, "-s", sandbox };
        if (!string.IsNullOrWhiteSpace(model)) { args.Add("-m"); args.Add(model); }
        args.Add("-");
        string prompt = $"Project requirement: {req.Id} ({req.Path}). Execution plan: {planPath}. Read these and AGENTS.md before acting. Requirement catalog: Docs/Requirements/catalog.json. Historical sources are provenance, not current authorities. Scope: {sandbox}.\n\n{instruction}";
        try
        {
            var result = await ProcessRunner.RunAsync(executable, args, requirements.Root, prompt, line =>
            {
                events.WriteLine(line); progress?.Invoke(line);
                try
                {
                    using var json = JsonDocument.Parse(line); var e = json.RootElement;
                    if (!e.TryGetProperty("type", out var type)) return;
                    switch (type.GetString())
                    {
                        case "thread.started": run.ThreadId = e.GetProperty("thread_id").GetString(); break;
                        case "turn.completed": completed = true; break;
                        case "turn.failed": case "error": failed = true; run.Error = line; break;
                    }
                }
                catch (JsonException) { failed = true; run.Error = "Invalid JSONL event"; }
            }, ct);
            File.WriteAllText(System.IO.Path.Combine(dir, "stderr.txt"), result.Stderr);
            run.ExitCode = result.ExitCode;
            run.State = result.ExitCode == 0 && completed && !failed && !string.IsNullOrEmpty(run.ThreadId) ? "Completed — awaiting review" : "Failed";
            if (run.State == "Failed") run.Error ??= $"exit={result.ExitCode}, completed={completed}; {result.Stderr}";
        }
        catch (OperationCanceledException) { run.State = "Cancelled"; }
        catch (Exception e) { run.State = "Failed"; run.Error = e.Message; }
        finally
        {
            evidence.Save(run);
            try { run.After = (await snapshots.Capture()).Select(f => new Change(f.Value.Hash, f.Key)).ToArray(); } catch (Exception e) { run.AfterStateError = e.Message; }
            evidence.Save(run);
        }
        return run;
    }
}
public static class WorkspaceLock
{
    public static FileStream Acquire(string root)
    {
        var project=new ProjectContext(root);
        try { return new FileStream(project.StatePath("workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new InvalidOperationException("This workspace already has an active Karolina write task", e); }
    }
}
