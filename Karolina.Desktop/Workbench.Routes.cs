using System.Text.Json;
using Karolina.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    public async Task<string> Start()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase);
        app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1") { context.Response.StatusCode = 403; return; }
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = $"default-src 'self'; script-src 'self'; style-src 'self' 'nonce-{token}'; font-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
            if (context.Request.Path.StartsWithSegments("/api") && (context.Request.Headers["X-Karolina-Session"] != token || context.Request.Headers.TryGetValue("Origin", out var origin) && origin != $"http://{context.Request.Host}")) { context.Response.StatusCode = 403; return; }
            try { await next(); }
            catch (Exception e) { context.Response.StatusCode = e is ArgumentException or InvalidDataException or KeyNotFoundException ? 400 : 409; await context.Response.WriteAsJsonAsync(new { error = e.Message }); }
        });
        app.MapGet("/", () => Results.Text(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Web/index.html")).Replace("__SESSION__", token), "text/html; charset=utf-8"));
        app.MapGet("/app.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web/app.js"), "text/javascript"));
        app.MapGet("/style.css", () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web/style.css"), "text/css"));
        app.MapGet("/review.js", () => Results.File(Path.Combine(AppContext.BaseDirectory, "Web/review.js"), "text/javascript"));
        app.MapGet("/api/state", () =>
        {
            heartbeat = DateTimeOffset.UtcNow;
            lock (chats) return Results.Json(new
            {
                project = Path.GetFileName(root), root,
                codex = new { connected = codex.Connected, executable = codex.Executable, error = codex.Connected ? null : codex.Error, account = account.ValueKind == JsonValueKind.Undefined ? (JsonElement?)null : account },
                models, reviewExplanations = ExplanationProgress(), windowReady = MinimizeWindow != null, busy = IsBusy, toolBusy = toolCancellation != null,
                activeThread, activeTurn, currentRun = CurrentRunProgress, graph = projectGraph.Status(),
                unity = new { state = unityState, error = unityError, lastResult = unityLastResult, busy = unityGate.CurrentCount == 0 }, chats = chats.ToArray()
            });
        });
        app.MapPost("/api/codex/connect", async () =>
        {
            await codexConnectionGate.WaitAsync();
            try
            {
                if (!codex.Connected) { if(busy)throw new InvalidOperationException("当前Codex轮次尚未结束，不能重连"); await codex.Connect(root, app.Lifetime.ApplicationStopping); }
                account = await codex.Request("account/read", new { refreshToken = false }, app!.Lifetime.ApplicationStopping);
                var list = new List<JsonElement>(); string? next = null;
                do { var response = await codex.Request("model/list", new { cursor = next, limit = 100 }, app!.Lifetime.ApplicationStopping); list.AddRange(response.GetProperty("data").EnumerateArray().Where(e => !e.GetProperty("hidden").GetBoolean()).Select(e => e.Clone())); next = response.TryGetProperty("nextCursor", out var c) ? c.GetString() : null; if (list.Count > 1000) throw new InvalidDataException("模型列表过大"); } while (next != null);
                models = list.ToArray(); return Results.Json(new { connected = true });
            }
            finally { codexConnectionGate.Release(); }
        });
        app.MapGet("/api/documents", () => library.List());
        app.MapGet("/api/document/{id}", (string id) => library.Snapshot(id));
        app.MapGet("/api/document/{id}/metadata", (string id) => library.MetadataSnapshot(id));
        app.MapGet("/api/document/{id}/resources", (string id) => library.Resources(id));
        app.MapPost("/api/document/index", (DocumentIndexInput input) => { library.UpdateIndex(input.Id, input.ExpectedMetadataHash, input.Tags, input.PlannedChanges, input.ResourceRefs); return new { saved = true }; });
        app.MapPost("/api/document/create", (DocumentInput input) => library.Create(input.Type, input.Title, input.Domain, input.Requirements, input.EnglishTitle, input.EnglishDomain, input.RuleSection));
        app.MapPost("/api/document/details", (DocumentDetails input) => { library.UpdateDetails(input.Id, input.ExpectedMetadataHash, input.Status, input.Requirements, input.Conclusion); return new { saved = true }; });
        app.MapPost("/api/document/save", (DocumentSave input) => { library.Save(input.Id, input.Markdown, input.ExpectedHash, input.ChangeSummary); return new { saved = true }; });
        app.MapPost("/api/chat/send", Send);
        app.MapPost("/api/chat/stop", () => Stop());
        app.MapGet("/api/chat/{id}", async (string id) => { lock (chats) if (!chats.Any(c => c.Id == id)) throw new ArgumentException("会话不属于当前工程"); return await codex.Request("thread/read", new { threadId = id, includeTurns = true }, app!.Lifetime.ApplicationStopping); });
        app.MapPost("/api/approval", async (ApprovalInput input) =>
        {
            if (!approvals.TryRemove(input.Id, out var request)) throw new ArgumentException("审批请求已失效");
            string method = request.GetProperty("method").GetString()!;
            await codex.Respond(request.GetProperty("id"), new { decision = method is "execCommandApproval" or "applyPatchApproval" ? (input.Accept ? "approved" : "denied") : (input.Accept ? "accept" : "decline") });
            return Results.Json(new { responded = true });
        });
        app.MapGet("/api/events", (long after) => { lock (events) return new { cursor, truncated = events.TryPeek(out var first) && after < first.Cursor - 1, events = events.Where(e => e.Cursor > after).Select(e => new { cursor = e.Cursor, value = e.Event }).ToArray() }; });
        app.MapGet("/api/reviews", () => reviews.List().Select(ReviewView));
        app.MapGet("/api/workspace/preferences", () => workspacePreferences.Read());
        app.MapPost("/api/workspace/preferences", (WorkspacePreferences preferences) => { workspacePreferences.Save(preferences); return new { saved=true }; });
        app.MapGet("/api/settings", () => settingsStore.Read());
        app.MapPost("/api/settings/reset", () => Results.Json(settingsStore.ResetCorruptWithBackup()));
        app.MapPost("/api/settings", (WorkbenchSettings settings) =>
        {
            WorkbenchSettingsStore.Validate(settings);
            foreach (var profile in settings.ModelProfiles.Values.Where(p => !string.IsNullOrWhiteSpace(p.Model)))
            {
                var model = models.FirstOrDefault(m => m.TryGetProperty("model", out var value) && value.GetString() == profile.Model);
                if (model.ValueKind == JsonValueKind.Undefined)
                {
                    if (models.Length > 0) throw new ArgumentException("默认模型已不在 Codex 当前支持列表中，请刷新模型后重新选择");
                    continue;
                }
                if (!model.GetProperty("supportedReasoningEfforts").EnumerateArray().Any(e => e.GetProperty("reasoningEffort").GetString() == profile.Effort))
                    throw new ArgumentException("该模型不支持所选思考挡位，请从当前模型列表重新选择");
            }
            return Results.Json(settingsStore.Save(settings));
        });
        app.MapGet("/api/project-graph/status", () => projectGraph.Status());
        app.MapGet("/api/project-graph/nodes", (string? query, string? kind, int? take) => projectGraph.Search(query, kind, take ?? 250));
        app.MapGet("/api/project-graph/node", (string id) => projectGraph.Node(id));
        app.MapPost("/api/project-graph/index", () => projectGraph.StartIndex());
        app.MapGet("/api/review/{id}", (string id) => ReviewView(reviews.Get(id)));
        app.MapGet("/api/review/{id}/diff", (string id, string path) => reviews.Diff(id, path));
        app.MapPost("/api/review/start", async (ReviewStartInput input) => { using var guard = WorkspaceLock.Acquire(root); var plan = ReviewPlan(input.PlanId); return ReviewView(await reviews.Start(plan.Id, plan.Title, app.Lifetime.ApplicationStopping)); });
        app.MapPost("/api/review/submit", async (ReviewSubmitInput input) => { using var guard = WorkspaceLock.Acquire(root); return ReviewView(await reviews.Submit(input.Id, input.Revision, input.Summary, app.Lifetime.ApplicationStopping)); });
        app.MapPost("/api/review/summary", (ReviewSubmitInput input) => { using var guard = WorkspaceLock.Acquire(root); return ReviewView(reviews.SaveSummary(input.Id, input.Revision, input.Summary)); });
        app.MapPost("/api/review/origin", async (ReviewOriginInput input) => { using var guard = WorkspaceLock.Acquire(root); return ReviewView(await reviews.SetOrigin(input.Id, input.Revision, input.Path, input.Fingerprint, input.Origin, input.Reason, app.Lifetime.ApplicationStopping)); });
        app.MapPost("/api/review/file", async (ReviewFileInput input) => { using var guard = WorkspaceLock.Acquire(root); return ReviewView(await reviews.DecideFile(input.Id, input.Revision, input.Path, input.Fingerprint, input.Accept, input.Notes ?? [], app.Lifetime.ApplicationStopping)); });
        app.MapPost("/api/review/decision", async (ReviewTaskInput input) => { using var guard = WorkspaceLock.Acquire(root); var task = await reviews.DecideTask(input.Id, input.Revision, input.Accept, input.Feedback ?? "", app.Lifetime.ApplicationStopping); if (input.Accept) { try { library.AssociateReview(task); } catch (Exception e) when (e is IOException or ArgumentException or InvalidOperationException) { Emit(new { method = "karolina/error", @params = new { message = "审批已保存，但计划资源索引未完成，请在文档关联中补充：" + e.Message } }); } } return ReviewView(task); });
        app.MapPost("/api/unity", UnityAction);
        app.MapGet("/api/diagnostics", () => Results.Json(Diagnostics()));
        app.MapGet("/api/window", () => new { handle = WindowHandle, content = ContentHandle, visible = WindowVisible, tray = TrayAvailable });
        app.MapPost("/api/window/minimize", () => { MinimizeWindow?.Invoke(); return new { minimized = MinimizeWindow != null, handle = WindowHandle }; });
        app.MapPost("/api/window/restore", () => { RestoreWindow?.Invoke(); return new { restored = RestoreWindow != null }; });
        app.MapPost("/api/window/editor", (EditorInput input) => { EditorDirty = input.Dirty; return new { recorded = true }; });
        app.MapPost("/api/shutdown", () => { app.Lifetime.StopApplication(); return new { stopped = true }; });
        MapToolRoutes();
        MapAppearanceRoutes();
        MapReadingRoutes();
        await app.StartAsync(); reviewSynchronizer = ObserveBackground(SynchronizeReviews(app.Lifetime.ApplicationStopping), "后台审批同步", app.Lifetime.ApplicationStopping); return app.Urls.Single();
    }
    private object Diagnostics()
    {
        int? reviewCount = null;
        string? recordsError = null;
        try { reviewCount=reviews.List().Length; }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException or ArgumentException)
        { recordsError="审批记录读取失败："+e.Message; }
        return new { run, desktopError=DesktopError,
            reviewSynchronization=new { error=reviewSyncError, lastSucceeded=reviewLastSynchronized },
            approvals=approvals.Values.ToArray(),
            project=new { root, project.Key, project.StateDirectory, reviews=reviewCount, recordsError, version=typeof(Workbench).Assembly.GetName().Version?.ToString() },
            editorDirty=EditorDirty, purpose="记录请求、停止、失败和验证回执，便于追查；不代替需求、计划或验收结论。", location=evidence.Root };
    }

}
