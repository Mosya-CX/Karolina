using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Karolina.Core.Appearance;

/// <summary>Owns project appearance preferences and theme packages independently of browser origins.</summary>
public sealed class AppearanceStore(ProjectContext project, string builtInRoot)
{
    private readonly object gate = new();
    private const long MaxFileBytes = 64L * 1024 * 1024;
    private const long MaxPackageBytes = 256L * 1024 * 1024;
    private static readonly Regex IdPattern = new("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex TokenPattern = new("^--k-[a-z0-9-]{1,100}$", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".json", ".css", ".svg", ".png", ".webp", ".jpg", ".jpeg", ".ttf", ".otf", ".woff", ".woff2", ".frag", ".glsl", ".txt" };
    private string UserRoot => project.StatePath("appearance/themes");
    private string PreferencePath => project.StatePath("appearance/preferences.json");

    public AppearancePreferences Read()
    {
        lock (gate)
        {
            if (!File.Exists(PreferencePath)) return new();
            var result = JsonSerializer.Deserialize<AppearancePreferences>(File.ReadAllText(PreferencePath), DocumentLibrary.Json)
                ?? throw new InvalidDataException("外观偏好内容为空");
            ValidatePreferences(result);
            return result;
        }
    }

    public void Save(AppearancePreferences value)
    {
        ValidatePreferences(value);
        lock (gate)
        {
            _ = Find(value.ThemeId);
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            string temp = project.StatePath("appearance/preferences.json.tmp");
            File.WriteAllText(temp, JsonSerializer.Serialize(value, DocumentLibrary.Json));
            File.Move(temp, PreferencePath, true);
        }
    }

    public ThemeDescriptor[] List()
    {
        lock (gate)
        {
            var result = new List<ThemeDescriptor>();
            foreach (var (folder, builtIn) in new[] { (builtInRoot, true), (UserRoot, false) })
            {
                if (!Directory.Exists(folder)) continue;
                foreach (string path in Directory.EnumerateDirectories(folder).OrderBy(p => p, StringComparer.Ordinal))
                {
                    string id = Path.GetFileName(path);
                    ValidateId(id);
                    string manifestPath = ResourcePath(path, "theme.json");
                    var manifest = ReadManifest(manifestPath);
                    if (manifest.Id != id) throw new InvalidDataException("主题编号与目录不一致：" + id);
                    ValidateManifest(manifest, path);
                    if (result.Any(t => t.Manifest.Id == id)) throw new InvalidDataException("重复主题编号：" + id);
                    result.Add(new(manifest, builtIn, "/theme-assets/" + id + "/"));
                }
            }
            return result.ToArray();
        }
    }

    public ThemeDescriptor Find(string id)
    {
        ValidateId(id);
        return List().SingleOrDefault(t => t.Manifest.Id == id) ?? throw new ArgumentException("找不到主题：" + id);
    }

    public string AssetPath(string id, string asset)
    {
        ValidateId(id);
        lock (gate)
        {
            string builtIn = Path.Combine(builtInRoot, id);
            string folder = Directory.Exists(builtIn) ? builtIn : project.StatePath("appearance/themes/" + id);
            return ResourcePath(folder, asset);
        }
    }

    public ThemeDescriptor Import(Stream archiveStream, bool replace = false)
    {
        string staging = project.StatePath("appearance/import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var zip = new ZipArchive(archiveStream, ZipArchiveMode.Read, true);
            if (zip.Entries.Count is 0 or > 512) throw new InvalidDataException("主题包必须包含 1 至 512 个文件");
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long expanded = 0;
            foreach (var entry in zip.Entries)
            {
                string name = entry.FullName;
                if (name.EndsWith('/')) { ValidateRelative(name.TrimEnd('/')); continue; }
                ValidateRelative(name);
                if (!Extensions.Contains(Path.GetExtension(name))) throw new InvalidDataException("主题包不支持此类型：" + name);
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000) throw new InvalidDataException("主题包不能包含链接");
                if (!names.Add(name)) throw new InvalidDataException("主题包包含重复路径：" + name);
                if (entry.Length > MaxFileBytes || (expanded += entry.Length) > MaxPackageBytes) throw new InvalidDataException("主题包展开体积超限");
                string target = ResourcePath(staging, name, false);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var input = entry.Open();
                using var output = File.Create(target);
                var buffer = new byte[81920];
                long actual = 0;
                int count;
                while ((count = input.Read(buffer)) > 0)
                {
                    actual += count;
                    if (actual > entry.Length || actual > MaxFileBytes) throw new InvalidDataException("主题包文件长度不符");
                    output.Write(buffer, 0, count);
                }
                if (actual != entry.Length) throw new InvalidDataException("主题包文件不完整：" + name);
            }
            var manifest = ReadManifest(ResourcePath(staging, "theme.json"));
            ValidateManifest(manifest, staging);
            lock (gate)
            {
                if (Directory.Exists(Path.Combine(builtInRoot, manifest.Id))) throw new ArgumentException("内建主题不能覆盖；请修改包内的 id");
                Directory.CreateDirectory(UserRoot);
                string destination = project.StatePath("appearance/themes/" + manifest.Id);
                string? previous = null;
                if (Directory.Exists(destination))
                {
                    if (!replace) throw new ArgumentException("主题已存在；勾选替换同名主题或修改包内的 id");
                    previous = project.StatePath("appearance/archive/" + manifest.Id + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(Path.GetDirectoryName(previous)!);
                    Directory.Move(destination, previous);
                }
                try { Directory.Move(staging, destination); }
                catch { if (previous != null) Directory.Move(previous, destination); throw; }
                return new(manifest, false, "/theme-assets/" + manifest.Id + "/");
            }
        }
        finally
        {
            // The private, freshly created staging directory is never a user-selected path.
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
        }
    }

    public byte[] Export(string id, AppearancePreferences? appearance = null)
    {
        lock (gate)
        {
            var descriptor = Find(id);
            string folder = Path.GetDirectoryName(AssetPath(id, "theme.json"))!;
            var manifest = descriptor.Manifest;
            if (descriptor.BuiltIn) manifest = manifest with { Id = id + "-custom", Name = manifest.Name + " · 自定义" };
            if (appearance != null)
            {
                ValidatePreferences(appearance);
                manifest = manifest with {
                    Tokens = new(manifest.Tokens),
                    ColorModes = manifest.ColorModes.ToDictionary(pair => pair.Key, pair => new Dictionary<string, string>(pair.Value))
                };
                foreach (var pair in appearance.Tokens) manifest.Tokens[pair.Key] = pair.Value;
                foreach (var mode in manifest.ColorModes.Values)
                    foreach (var pair in appearance.Tokens) mode[pair.Key] = pair.Value;
            }
            using var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                var files = new Stack<string>(); files.Push(folder);
                while (files.TryPop(out string? directory))
                {
                    foreach (string child in Directory.EnumerateDirectories(directory))
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("主题目录包含链接");
                        files.Push(child);
                    }
                    foreach (string file in Directory.EnumerateFiles(directory))
                    {
                        string relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
                        _ = ResourcePath(folder, relative);
                        if (!Extensions.Contains(Path.GetExtension(relative))) continue;
                        var entry = archive.CreateEntry(relative, CompressionLevel.Fastest);
                        using var output = entry.Open();
                        if (relative == "theme.json") JsonSerializer.Serialize(output, manifest, DocumentLibrary.Json);
                        else { using var input = File.OpenRead(file); input.CopyTo(output); }
                    }
                }
            }
            return memory.ToArray();
        }
    }

    private static ThemeManifest ReadManifest(string path)
    {
        if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("主题声明超限");
        return JsonSerializer.Deserialize<ThemeManifest>(File.ReadAllText(path), DocumentLibrary.Json)
            ?? throw new InvalidDataException("主题声明为空");
    }

    private static void ValidateManifest(ThemeManifest manifest, string folder)
    {
        if (manifest.SchemaVersion != 1) throw new ArgumentException("不支持的主题版本");
        ValidateId(manifest.Id);
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Length > 100) throw new ArgumentException("主题名称必须为 1 至 100 个字符");
        ValidateTokens(manifest.Tokens);
        if (manifest.ColorModes == null || manifest.Assets == null || manifest.Icons == null || manifest.Components == null || manifest.Styles == null || manifest.Fonts == null || manifest.Effect == null) throw new ArgumentException("主题字段不能为 null");
        foreach (var mode in manifest.ColorModes)
        {
            if (mode.Key is not ("dark" or "light")) throw new ArgumentException("主题配色模式只能是 dark/light");
            ValidateTokens(mode.Value);
        }
        foreach (string resource in manifest.Assets.Values.Concat(manifest.Icons.Values).Concat(manifest.Components.Values).Concat(manifest.Styles).Concat(manifest.Fonts.Select(f => f.Source))) _ = ResourcePath(folder, resource);
        if (manifest.Components.Values.Concat(manifest.Styles).Any(s => Path.GetExtension(s) != ".css")) throw new ArgumentException("组件样式必须为 CSS 文件");
        if (manifest.Effect.Fragment != null) _ = ResourcePath(folder, manifest.Effect.Fragment);
        if (!double.IsFinite(manifest.Effect.Intensity) || manifest.Effect.Intensity is < 0 or > 1) throw new ArgumentException("效果强度必须在 0 至 1 之间");
    }

    private static void ValidatePreferences(AppearancePreferences value)
    {
        ValidateId(value.ThemeId);
        if (value.Mode is not ("dark" or "light") || value.Motion is not ("system" or "full" or "reduced" or "off") || value.Effects is not ("auto" or "shader" or "static")) throw new ArgumentException("无效外观设置");
        if (!double.IsFinite(value.BackgroundOpacity) || value.BackgroundOpacity is < 0 or > 1) throw new ArgumentException("背景强度必须在 0 至 1 之间");
        ValidateTokens(value.Tokens);
    }

    private static void ValidateTokens(Dictionary<string, string>? tokens)
    {
        if (tokens == null || tokens.Count > 512) throw new ArgumentException("设计变量数量无效");
        foreach (var pair in tokens)
            if (!TokenPattern.IsMatch(pair.Key) || pair.Value == null || pair.Value.Length > 2048 || pair.Value.Contains('{') || pair.Value.Contains('}') || pair.Value.Contains(';') || pair.Value.Contains('<')) throw new ArgumentException("无效设计变量：" + pair.Key);
    }

    private static void ValidateId(string id)
    {
        if (id == null || !IdPattern.IsMatch(id)) throw new ArgumentException("主题 id 只能使用小写英文、数字和连字符，且以字母开头");
    }

    private static void ValidateRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p is "" or "." or ".." || p.EndsWith('.') || p.EndsWith(' '))) throw new ArgumentException("主题资源路径必须为普通相对路径：" + path);
    }

    private static string ResourcePath(string folder, string relative, bool requireFile = true)
    {
        ValidateRelative(relative);
        string root = Path.GetFullPath(folder);
        string target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("主题资源越界");
        for (string? node = target; node != null && node.Length >= root.Length; node = Path.GetDirectoryName(node))
            if ((File.Exists(node) || Directory.Exists(node)) && (File.GetAttributes(node) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("主题资源不能使用链接");
        if (requireFile && (!Extensions.Contains(Path.GetExtension(target)) || !File.Exists(target))) throw new ArgumentException("主题资源不存在或类型不支持：" + relative);
        return target;
    }
}
