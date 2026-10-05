using Karolina.Core;
using Microsoft.AspNetCore.Builder;
using System.Text.Json;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    private Task provenanceQueue = Task.CompletedTask;
    private readonly object provenanceGate = new();
    private Task? reviewSynchronizer;
    private string? reviewSyncError;
    private DateTimeOffset? reviewLastSynchronized;
    private (string Id,Dictionary<string,string> Fingerprints)? explainingTask;
    private void MapReadingRoutes()
    {
        LoadExplanationProgress();
        app!.MapPost("/api/review/explain/stop",(ReviewExplanationStop input)=> {
            if(string.IsNullOrWhiteSpace(input.Id)||string.IsNullOrWhiteSpace(input.RunId))throw new ArgumentException("停止说明需要任务与轮次编号");
            return Stop(input.Id,input.RunId);
        });
        app!.MapPost("/api/review/explain",(ReviewExplainInput input)=>Send(new ChatInput("请用中文补齐这次任务各文件的改动说明，只分析给定冻结资料，不调用Unity、不写入项目文件。不得把未知来源确认为Agent，不把统计或资源序列化内容当作解释。",input.Model,input.Effort,"read-only",[],null,Mode:"explain-review",ExplanationReviewId:input.Id,ExplanationRevision:input.Revision)));
        app!.MapGet("/api/build",()=>WorkbenchBuild.Read(Path.Combine(AppContext.BaseDirectory,"Web/index.html")));
        app!.MapPost("/api/resource/reveal", (ResourceRevealInput input) => {
            string path = library.ResourcePath(input.Path); if (!File.Exists(path)) throw new ArgumentException("资源已删除或不存在：" + input.Path);
            var start = new System.Diagnostics.ProcessStartInfo("explorer.exe") { UseShellExecute = false }; start.ArgumentList.Add("/select," + path);
            System.Diagnostics.Process.Start(start)?.Dispose(); return new { revealed = true };
        });
    }
    private void QueueFileProvenance(JsonElement notification)
    {
        if (!busy || activeReview == null || notification.GetProperty("method").GetString() != "item/completed") return;
        var p = notification.GetProperty("params");
        if (!p.TryGetProperty("threadId", out var thread) || thread.GetString() != activeThread || !p.TryGetProperty("turnId", out var turn) || activeTurn != null && turn.GetString() != activeTurn) return;
        if (!p.TryGetProperty("item", out var item) || !item.TryGetProperty("type", out var type) || type.GetString() != "fileChange" || !item.TryGetProperty("status", out var status) || status.GetString() != "completed" || !item.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) return;
        string id = activeReview, eventId = item.GetProperty("id").GetString() ?? "未知回执";
        var copy = changes.Clone();
        lock (provenanceGate) provenanceQueue = provenanceQueue.ContinueWith(async _ => {
            foreach (var change in copy.EnumerateArray())
            {
                try
                {
                    string path = change.GetProperty("path").GetString()!;
                    if (Path.IsPathRooted(path)) path = Path.GetRelativePath(root, path);
                    path = path.Replace('\\', '/');
                    if (change.TryGetProperty("diff", out var patch) && patch.ValueKind == JsonValueKind.String) await reviews.RecordAgentPatch(id, path, patch.GetString()!, eventId, app!.Lifetime.ApplicationStopping);
                }
                catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or KeyNotFoundException) { Emit(new { method = "karolina/error", @params = new { message = "文件来源核对未完成：" + e.Message } }); }
            }
        }, TaskScheduler.Default).Unwrap();
    }
    private async Task SynchronizeReviews(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (!await gate.WaitAsync(0, ct)) continue;
            try
            {
                foreach (string id in await reviews.RefreshChanged(ct)) Emit(new { method = "karolina/review-updated", @params = new { id } });
                reviewSyncError = null;
                reviewLastSynchronized = DateTimeOffset.UtcNow;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
            {
                string failure="审批自动同步暂未完成，将重试：" + e.Message;
                if(reviewSyncError != failure)Emit(new { method = "karolina/error", @params = new { message = failure } });
                reviewSyncError = failure;
            }
            finally { gate.Release(); }
        }
    }

    private object ReviewView(TaskReview task)
    {
        reviews.Presentation(task);
        RunRecord? runEvidence = null;
        string? progressError = null;
        if (!string.IsNullOrWhiteSpace(task.RunId))
        {
            try { runEvidence = evidence.Read(task.RunId); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidDataException)
            { progressError = "执行过程记录无法读取：" + e.Message; }
        }
        return new { task.Id, task.PlanId, task.Title, task.State, task.Revision, task.Round, task.Started, task.Updated, task.Summary, task.Feedback, task.ThreadId, task.RunId, task.RunState, task.BaselineKind, task.BaselineDescription, task.SelectionEvidence,
            executionProgress = runEvidence == null ? null : new { runEvidence.State, runEvidence.ProgressStage, runEvidence.ProgressMessage, runEvidence.ProgressUpdated, events = runEvidence.Progress.TakeLast(100).ToArray() }, progressError,
            files = task.Files.Where(f => f.Origin == "agent" && !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)),
            candidates = task.Files.Where(f => f.Origin != "agent" && !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)),
            attempts = task.Attempts.Select(a => new { a.Round, a.Summary, a.Feedback, a.ReturnedAt, files = a.Files.Where(f => !f.Path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) }),
            autoSync = task.State is "执行" or "待审批", feedbackPrompt = reviews.FeedbackPrompt(task) };
    }

    private LibraryDocument ReviewPlan(string id)
    {
        var plan = library.Find(id);
        var snapshot=JsonSerializer.SerializeToElement(library.MetadataSnapshot(id),DocumentLibrary.Json).GetProperty("metadata");
        if(plan.Type != "plan" || snapshot.GetProperty("status").GetString() == "关闭") throw new ArgumentException("请选择未关闭的具体计划");
        if(!snapshot.TryGetProperty("requirements",out var refs)||refs.GetArrayLength()==0)throw new ArgumentException("执行计划需要先关联具体需求");
        if (!snapshot.TryGetProperty("estimateStatus", out _) && (!snapshot.TryGetProperty("plannedChanges", out var estimate) || estimate.GetArrayLength() == 0)) throw new ArgumentException("执行前请先在计划资料索引填写文件预估");
        return plan;
    }
}
