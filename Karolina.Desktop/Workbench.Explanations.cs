using System.Text.Json;
using Karolina.Core;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    private readonly ReviewExplanationService explanationService;

    private ReviewExplanationProgress[] ExplanationProgress() => explanationService.Snapshot();

    private void LoadExplanationProgress()
    {
        foreach(string message in explanationService.Restore())
            Emit(new { method = "karolina/error", @params = new { message } });
    }

    private void BeginExplanationProgress(string id, int count, string model, string effort) =>
        explanationService.Begin(id, run!.Id, count, model, effort);

    private void UpdateExplanationProgress(string phase, string activity, long characters = 0,
        int? saved = null, bool? active = null, string? error = null, bool persist = false, bool clearError = false)
    {
        if(explainingTask is not {} target || run == null) return;
        string? failure=explanationService.Update(target.Id,run.Id,phase,activity,characters,saved,active,error,persist,clearError);
        // The service has released its lock before the host publishes events or takes the chat lock.
        if(failure != null) Emit(new { method = "karolina/error", @params = new { message = failure } });
    }
    private void ReceiveExplanationProgress(JsonElement notification)
    {
        if (!busy || explainingTask == null || !notification.TryGetProperty("params", out var p) ||
            !p.TryGetProperty("threadId", out var thread) || thread.GetString() != activeThread ||
            p.TryGetProperty("turnId", out var turn) && activeTurn != null && turn.GetString() != activeTurn) return;
        string method = notification.GetProperty("method").GetString()!;
        if (method == "item/agentMessage/delta")
            UpdateExplanationProgress("生成说明", "正在输出逐文件说明，完成后统一校验和保存", p.TryGetProperty("delta", out var delta) ? delta.GetString()?.Length ?? 0 : 0);
        else if (method.StartsWith("item/reasoning/", StringComparison.Ordinal))
            UpdateExplanationProgress("分析", "正在分析冻结资料与文件用途");
        else if (method == "item/started" && p.TryGetProperty("item", out var item))
            UpdateExplanationProgress("分析", item.TryGetProperty("type", out var type) && type.GetString() == "commandExecution" ? "正在执行只读资料检查" : "正在分析冻结资料");
    }
}
