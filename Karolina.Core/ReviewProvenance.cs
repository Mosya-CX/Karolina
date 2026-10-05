using System.Text.RegularExpressions;

namespace Karolina.Core;

public static class ReviewProvenance
{
    // Reconstruct the complete output, preserving the final newline and line endings.
    // Unverifiable patches never confer ownership.
    public static bool PatchMatches(string before, string after, string patch)
    {
        string newline = before.Contains("\r\n") ? "\r\n" : "\n";
        if (before.Replace("\r\n", "").Contains('\r') || before.Contains("\r\n") && before.Replace("\r\n", "").Contains('\n')) return false;
        bool ended = before.EndsWith('\n'), finalNewline = ended;
        string normalized = before.Replace("\r\n", "\n");
        var original = before.Length == 0 ? Array.Empty<string>() : (ended ? normalized[..^1] : normalized).Split('\n');
        var output = new List<string>(); var lines = patch.Replace("\r\n", "\n").Split('\n'); int cursor = 0, hunks = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            var header = Regex.Match(lines[i], @"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@"); if (!header.Success) continue;
            int oldCount = header.Groups[2].Success ? int.Parse(header.Groups[2].Value) : 1;
            int newCount = header.Groups[4].Success ? int.Parse(header.Groups[4].Value) : 1;
            int oldStart = int.Parse(header.Groups[1].Value);
            int start = oldCount == 0 ? oldStart : Math.Max(0, oldStart - 1);
            if (start < cursor || start > original.Length) return false;
            while (cursor < start) output.Add(original[cursor++]);
            int removed = 0, added = 0; char previousPrefix = '\0'; bool newNoNewline = false, oldNoNewline = false; hunks++;
            while (++i < lines.Length)
            {
                string line = lines[i];
                if (line.StartsWith("\\ No newline")) { if(previousPrefix is '-' or ' ')oldNoNewline=true;if(previousPrefix is '+' or ' ')newNoNewline=true;continue; }
                if (removed == oldCount && added == newCount) { i--; break; }
                if (line.Length == 0 || line[0] is not (' ' or '+' or '-')) return false;
                string text = line[1..]; previousPrefix = line[0];
                if (previousPrefix is ' ' or '-') { if (cursor >= original.Length || original[cursor] != text) return false; cursor++; removed++; }
                if (previousPrefix is ' ' or '+') { output.Add(text); added++; }
            }
            if (removed != oldCount || added != newCount) return false;
            if (oldNoNewline && (ended || cursor != original.Length)) return false;
            if (cursor == original.Length) finalNewline = newCount > 0 && !newNoNewline;
            else if (newNoNewline) return false;
        }
        while (cursor < original.Length) output.Add(original[cursor++]);
        string reconstructed = string.Join(newline, output) + (output.Count > 0 && finalNewline ? newline : "");
        return hunks > 0 && reconstructed == after;
    }
}
