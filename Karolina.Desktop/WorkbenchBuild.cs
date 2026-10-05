using System.Text;
using System.Text.RegularExpressions;
using Karolina.Core;

namespace Karolina.Desktop;

internal sealed record WorkbenchBuild(string Revision, string AssetHash)
{
    // index.html is the single build marker used by both the browser and /api/build.
    public static WorkbenchBuild Read(string indexPath)
    {
        byte[] bytes = File.ReadAllBytes(indexPath);
        var marker = Regex.Match(Encoding.UTF8.GetString(bytes), "<meta\\s+name=\"karolina-build\"\\s+content=\"([^\"]+)\"", RegexOptions.CultureInvariant);
        if (!marker.Success) throw new InvalidDataException("页面缺少Karolina构建标记");
        return new(marker.Groups[1].Value, RequirementStore.Hash(bytes));
    }
}
