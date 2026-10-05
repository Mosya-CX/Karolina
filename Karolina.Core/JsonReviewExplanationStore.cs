using System.Text.Json;

namespace Karolina.Core;

/// <summary>说明进度的本地 JSON 适配器；沿用 explanations/{reviewId}.json。</summary>
public sealed class JsonReviewExplanationStore(ProjectContext project) : IReviewExplanationStore
{
    public ReviewExplanationRestore Load()
    {
        string folder = project.StatePath("explanations");
        if (!Directory.Exists(folder)) return new([], []);
        var records = new List<ReviewExplanationProgress>();
        var errors = new List<string>();
        foreach (string item in Directory.EnumerateFiles(folder, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                string path = project.StatePath("explanations/" + Path.GetFileName(item));
                var progress = JsonSerializer.Deserialize<ReviewExplanationProgress>(File.ReadAllText(path), DocumentLibrary.Json);
                if (progress == null || !Guid.TryParseExact(progress.ReviewId, "N", out _) ||
                    Path.GetFileNameWithoutExtension(path) != progress.ReviewId)
                    throw new InvalidDataException("说明记录编号与文件名不一致");
                records.Add(progress);
            }
            catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
            { errors.Add("说明进度读取失败：" + Path.GetFileName(item) + "：" + e.Message); }
        }
        return new(records.ToArray(), errors.ToArray());
    }

    public void Save(ReviewExplanationProgress progress)
    {
        if (!Guid.TryParseExact(progress.ReviewId, "N", out _)) throw new ArgumentException("无效任务编号");
        string path = project.StatePath($"explanations/{progress.ReviewId}.json");
        string temp = project.StatePath($"explanations/{progress.ReviewId}.json.tmp");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(temp, JsonSerializer.Serialize(progress, DocumentLibrary.Json));
        File.Move(temp, path, true);
    }
}
