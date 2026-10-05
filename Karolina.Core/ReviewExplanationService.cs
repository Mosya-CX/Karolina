namespace Karolina.Core;

// These property names also define the existing persisted JSON and state API contract.
public sealed record ReviewExplanationProgress(string ReviewId, string RunId, string Model, string Effort,
    DateTimeOffset Started, DateTimeOffset Updated, string Phase, string Activity, int TargetFiles,
    long ReceivedCharacters = 0, int SavedFiles = 0, bool Active = true, string? Error = null);

public sealed record ReviewExplanationRestore(ReviewExplanationProgress[] Records, string[] Errors);

public interface IReviewExplanationStore
{
    ReviewExplanationRestore Load();
    void Save(ReviewExplanationProgress progress);
}

/// <summary>唯一拥有说明进度；不依赖 HTTP、Codex、文件路径或宿主回调。</summary>
public sealed class ReviewExplanationService(IReviewExplanationStore store, TimeProvider? clock = null)
{
    private readonly TimeProvider clock = clock ?? TimeProvider.System;
    private readonly object gate = new();
    private readonly Dictionary<string, ReviewExplanationProgress> records = new(StringComparer.Ordinal);
    private bool restored;

    public ReviewExplanationProgress[] Snapshot()
    {
        lock (gate) return records.Values.ToArray();
    }

    // Restore is a startup operation; it must not replace an already running in-memory job.
    public string[] Restore()
    {
        lock (gate)
        {
            if (restored || records.Count != 0) throw new InvalidOperationException("说明服务只能在启动时恢复一次");
            var loaded = store.Load();
            restored = true;
            foreach (var progress in loaded.Records)
                records[progress.ReviewId] = progress.Active
                    ? progress with { Active = false, Phase = "已中断", Activity = "程序上次退出时没有取得生成终态；可重新补充说明。" }
                    : progress;
            return loaded.Errors;
        }
    }

    public void Begin(string reviewId, string runId, int count, string model, string effort)
    {
        if (!Guid.TryParseExact(reviewId, "N", out _) || !Guid.TryParseExact(runId, "N", out _) || count <= 0)
            throw new ArgumentException("说明任务需要有效的任务、运行编号和文件数");
        lock (gate)
        {
            if (records.TryGetValue(reviewId, out var current) && current.Active)
                throw new InvalidOperationException("此审批的说明仍在生成");
            var now = clock.GetUtcNow();
            var progress = new ReviewExplanationProgress(reviewId, runId, model, effort, now, now,
                "准备", "正在创建只读说明会话", count);
            records[reviewId] = progress;
            store.Save(progress); // A start failure propagates so the caller cannot claim that a job started.
        }
    }

    // A returned storage error is reported by the host after this lock has been released.
    public string? Update(string reviewId, string runId, string phase, string activity, long characters = 0,
        int? saved = null, bool? active = null, string? error = null, bool persist = false, bool clearError = false)
    {
        if (characters < 0 || saved < 0) throw new ArgumentException("进度计数不能为负数");
        lock (gate)
        {
            if (!records.TryGetValue(reviewId, out var current) || current.RunId != runId || !current.Active) return null;
            if (saved > current.TargetFiles) throw new ArgumentException("保存文件数超过本轮目标");
            var next = current with { Phase = phase, Activity = activity, Updated = clock.GetUtcNow(),
                ReceivedCharacters = current.ReceivedCharacters + characters, SavedFiles = saved ?? current.SavedFiles,
                Active = active ?? current.Active, Error = clearError ? null : error ?? current.Error };
            records[reviewId] = next;
            if (!persist) return null;
            try { store.Save(next); return null; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                string storageError = "说明进度存储失败：" + e.Message;
                records[reviewId] = next with { Error = string.IsNullOrEmpty(next.Error) ? storageError : next.Error + "；" + storageError };
                return storageError;
            }
        }
    }
}
