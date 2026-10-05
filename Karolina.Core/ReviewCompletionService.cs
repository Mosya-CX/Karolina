namespace Karolina.Core;

public sealed record ReviewExecutionCompletion(string ReviewId, int UnprovenDescriptions);

/// <summary>执行回复与冻结说明的落库用例；不拥有协议连接、运行锁、UI事件或进度。</summary>
public sealed class ReviewCompletionService(TaskReviewStore reviews)
{
    public async Task<ReviewExecutionCompletion> CompleteExecution(string id, string reply, CancellationToken ct)
    {
        var task = reviews.Get(id);
        string summary = ReviewNarratives.HumanSummary(reply);
        if (string.IsNullOrWhiteSpace(summary))
            summary = "Codex 本轮已完成，未提供整体总结；请补充实际做了什么、验证结果和限制。";
        await reviews.Submit(task.Id, task.Revision, summary[..Math.Min(summary.Length, 12000)], ct);

        int skipped = 0;
        if (!string.IsNullOrWhiteSpace(reply))
        {
            var current = reviews.Get(id);
            var proven = current.Files.Where(f => f.Origin == "agent" &&
                    current.ProvenAgentVersions.TryGetValue(f.Path, out var written) && written?.Hash == f.After?.Hash)
                .ToDictionary(f => f.Path, f => f.Fingerprint);
            var parsed = ReviewNarratives.Parse(reply);
            var eligible = parsed.Where(f => proven.ContainsKey(f.Path)).ToArray();
            await reviews.ApplyNarratives(id, proven, eligible, "Agent执行完成回复（用途与验证仍需人工复核）", ct);
            skipped = parsed.Length - eligible.Length;
        }
        return new(id, skipped);
    }

    public async Task<int> CompleteExplanation(string id, Dictionary<string, string> fingerprints, string reply, CancellationToken ct)
    {
        var parsed = ReviewNarratives.Parse(reply);
        if (parsed.Length == 0) throw new InvalidOperationException("Agent没有返回可保存的逐文件说明，请重试或在对话中补齐");
        await reviews.ApplyNarratives(id, fingerprints, parsed, "Agent根据冻结差异补充说明（不证明文件作者或验收）", ct);
        return parsed.Length;
    }
}
