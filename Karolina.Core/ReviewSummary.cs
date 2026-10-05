using System.Text.RegularExpressions;

namespace Karolina.Core;

public sealed record SemanticChange(string Object, string Field, string? Before, string? After);
public sealed record SemanticSummary(string Kind, string Brief, SemanticChange[] Changes, int TotalChanges, string Limitation);
public static class ReviewSummary
{
    private const int DisplayLimit = 100;
    public static SemanticSummary Describe(ReviewFile file, string? before, string? after)
    {
        bool yaml = (before ?? after)?.StartsWith("%YAML", StringComparison.Ordinal) == true;
        if (yaml)
        {
            var a = Fields(before); var b = Fields(after); var changes = new List<SemanticChange>();
            foreach (var key in a.Keys.Union(b.Keys).Order(StringComparer.Ordinal))
            {
                a.TryGetValue(key, out var old); b.TryGetValue(key, out var current);
                if (old?.Value == current?.Value) continue;
                var field = current ?? old!; changes.Add(new(field.Object, field.Field, old?.Value, current?.Value));
            }
            int added = changes.Count(c => c.Before == null), deleted = changes.Count(c => c.After == null);
            string brief = $"{file.Kind} Unity资源：{changes.Count}处可解析字段变化（新增{added}、移除{deleted}）；涉及{changes.Select(c => c.Object).Distinct().Count()}个对象/组件";
            return new("unity", brief, changes.Take(DisplayLimit).ToArray(), changes.Count, "冻结文本的静态字段摘要；数组按出现次序对照，引用显示GUID/fileID，不能代替Unity场景与行为验收。超出100项可查看原始差异。");
        }
        if (before == null && file.Before != null || after == null && file.After != null) return new("binary", $"{file.Kind}资源：{file.Before?.Bytes ?? 0} → {file.After?.Bytes ?? 0}字节", [], 0, "二进制/过大资源无法推断视觉或行为变化，请在对应编辑器查看。");
        var oldLines = Lines(before); var newLines = Lines(after);
        var removed = oldLines.Except(newLines, StringComparer.Ordinal).ToArray(); var addedLines = newLines.Except(oldLines, StringComparer.Ordinal).ToArray();
        string[] symbols = addedLines.Concat(removed).Select(l => Regex.Match(l, @"\b(?:class|struct|interface|enum|record)\s+([A-Za-z_][A-Za-z0-9_]*)")).Where(m => m.Success).Select(m => m.Groups[1].Value).Distinct().Take(8).ToArray();
        string kind = Path.GetExtension(file.Path).ToLowerInvariant() is ".cs" or ".js" or ".ts" or ".shader" or ".hlsl" ? "code" : "text";
        return new(kind, $"{file.Kind}{(kind == "code" ? "代码" : "文本")}：新增/变更{addedLines.Length}种行，移除/变更{removed.Length}种行" + (symbols.Length > 0 ? "；涉及类型 " + string.Join("、", symbols) : ""), [], addedLines.Length + removed.Length, "按冻结内容自动生成的静态摘要，不推断业务效果；重复行按唯一文本统计，完整行级审查见差异。");
    }
    private sealed record FieldValue(string Object, string Field, string Value);
    private static Dictionary<string, FieldValue> Fields(string? source)
    {
        var result = new Dictionary<string, FieldValue>(StringComparer.Ordinal); if (source == null) return result;
        string id = "root", type = "资源", label = "资源"; var stack = new List<(int Indent, string Name)>(); var occurrences = new Dictionary<string, int>();
        foreach (string line in Lines(source))
        {
            var header = Regex.Match(line, @"^--- !u!(\d+) &(\S+)");
            if (header.Success) { id = header.Groups[2].Value; type = ClassName(header.Groups[1].Value); label = type + " #" + id; stack.Clear(); occurrences.Clear(); result[id + "/$object"] = new(label, "对象/组件", type); continue; }
            var match = Regex.Match(line, @"^(\s*)(?:-\s*)?([A-Za-z_][\w.]*):\s*(.*)$");
            if (!match.Success) continue;
            int indent = match.Groups[1].Value.Length; string key = match.Groups[2].Value, value = match.Groups[3].Value;
            while (stack.Count > 0 && stack[^1].Indent >= indent) stack.RemoveAt(stack.Count - 1);
            string field = string.Join('/', stack.Select(s => s.Name).Append(key));
            if (value.Length == 0) { stack.Add((indent, key)); continue; }
            string identity = id + "/" + field; occurrences.TryGetValue(identity, out int occurrence); occurrences[identity] = occurrence + 1;
            if (key == "m_Name") label = type + "「" + value + "」 #" + id;
            result[identity + "/" + occurrence] = new(label, field + (occurrence > 0 ? $" [{occurrence + 1}]" : ""), value);
        }
        return result;
    }
    private static string ClassName(string id) => id switch { "1" => "GameObject", "4" => "Transform", "114" => "MonoBehaviour", "224" => "RectTransform", "95" => "Animator", "21" => "Material", "23" => "MeshRenderer", "65" => "BoxCollider", "54" => "Rigidbody", "33" => "MeshFilter", "137" => "SkinnedMeshRenderer", "74" => "AnimationClip", _ => "Unity组件(" + id + ")" };
    private static string[] Lines(string? source) => (source ?? "").Replace("\r\n", "\n").TrimStart('\uFEFF').Split('\n');
}
