using System.Text.Json;

namespace Karolina.Core.Appearance;

public sealed record PanelLayoutPreferences
{
    public Dictionary<string, double> Sizes { get; init; } = [];
    public string? WriterId { get; init; }
    public long Sequence { get; init; }
}

/// <summary>Panel geometry has its own project state and does not overwrite a theme or editor draft.</summary>
public sealed class PanelLayoutStore(ProjectContext project)
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> writers = [];
    private string FilePath => project.StatePath("appearance/layout.json");
    private static readonly Dictionary<string, (double Min, double Max)> Bounds = new()
    {
        ["sidebar"] = (180, 640), ["toc"] = (100, 480), ["reviewFiles"] = (160, 560),
        ["reviewHeight"] = (240, 1400), ["composer"] = (180, 1100)
    };

    public PanelLayoutPreferences Read()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return new();
            var value = JsonSerializer.Deserialize<PanelLayoutPreferences>(File.ReadAllText(FilePath), DocumentLibrary.Json)
                ?? throw new InvalidDataException("面板布局为空");
            Validate(value);
            return value;
        }
    }

    public void Save(PanelLayoutPreferences value)
    {
        Validate(value);
        lock (gate)
        {
            if (value.WriterId is { } writer && writers.TryGetValue(writer, out long previous) && value.Sequence <= previous) return;
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = project.StatePath("appearance/layout.json.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(new PanelLayoutPreferences { Sizes = value.Sizes }, DocumentLibrary.Json));
            File.Move(temporary, FilePath, true);
            if (value.WriterId is { } id) writers[id] = value.Sequence;
        }
    }

    private static void Validate(PanelLayoutPreferences value)
    {
        if (value.Sizes == null || value.Sizes.Count > Bounds.Count) throw new ArgumentException("无效面板布局");
        if (value.WriterId != null && (!Guid.TryParseExact(value.WriterId, "D", out _) || value.Sequence <= 0)) throw new ArgumentException("无效布局保存序号");
        foreach (var (key, size) in value.Sizes)
            if (!Bounds.TryGetValue(key, out var bounds) || !double.IsFinite(size) || size < bounds.Min || size > bounds.Max)
                throw new ArgumentException("无效面板尺寸：" + key);
    }
}
