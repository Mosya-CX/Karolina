using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Karolina.Core;
using Karolina.Core.Appearance;

internal static partial class Program
{
    private static Task<int> AppearanceChecks()
    {
        string temp = Path.Combine(Path.GetTempPath(), "karolina-appearance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string builtIn = Path.Combine(temp, "built-in");
            Directory.CreateDirectory(Path.Combine(builtIn, "karolina"));
            var baseManifest = new ThemeManifest { Id = "karolina", Name = "内建", Tokens = new() { ["--k-pink"] = "#ed7fce" }, ColorModes = new() { ["light"] = new() { ["--k-text"] = "#29304e" } } };
            File.WriteAllText(Path.Combine(builtIn, "karolina", "theme.json"), JsonSerializer.Serialize(baseManifest, DocumentLibrary.Json));
            var project = new ProjectContext(temp);
            var store = new AppearanceStore(project, builtIn);
            int count = 0;
            void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
            void Reject(Action action) { bool rejected = false; try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { rejected = true; } Check(rejected, "必须拒绝无效主题"); }
            MemoryStream Package(ThemeManifest manifest, params (string Name, string Text)[] resources)
            {
                var memory = new MemoryStream();
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry("theme.json").Open(), new UTF8Encoding(false))) writer.Write(JsonSerializer.Serialize(manifest, DocumentLibrary.Json));
                    foreach (var (name, text) in resources) { using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false)); writer.Write(text); }
                }
                memory.Position = 0; return memory;
            }
            var custom = new ThemeManifest { Id = "my-theme", Name = "测试包", Tokens = new() { ["--k-button-cut"] = "6px" }, Components = new() { ["button-frame"] = "frame.css" }, Icons = new() { ["chat"] = "icons/chat.svg" } };
            Test("默认主题与偏好可加载", () => { Check(store.Read().ThemeId == "karolina"); Check(store.List().Single().BuiltIn); });
            Test("部分覆盖主题保留自己的组件和图标资源", () => {
                using var zip = Package(custom, ("frame.css", ".k-frame-line{stroke:red}"), ("icons/chat.svg", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"));
                var result = store.Import(zip); Check(!result.BuiltIn && store.List().Length == 2); Check(File.Exists(store.AssetPath(custom.Id, "icons/chat.svg")));
            });
            var preferences = new AppearancePreferences { ThemeId = custom.Id, Mode = "light", Motion = "reduced", Effects = "static", BackgroundOpacity = .4, Tokens = new() { ["--k-pink"] = "#cc88bb" } };
            Test("偏好按工程恢复且不依赖端口", () => { store.Save(preferences); var reopened = new AppearanceStore(new ProjectContext(temp), builtIn); Check(reopened.Read().ThemeId == custom.Id && reopened.Read().Tokens["--k-pink"] == "#cc88bb"); });
            Test("无效设置不覆盖此前偏好", () => { Reject(() => store.Save(preferences with { Motion = "bad" })); Reject(() => store.Save(preferences with { BackgroundOpacity = double.NaN })); Check(store.Read().Motion == "reduced"); });
            Test("拒绝包内越界路径且不写出目录", () => { using var zip = Package(custom with { Id = "escape" }, ("../escape.txt", "bad")); Reject(() => store.Import(zip)); Check(!File.Exists(project.StatePath("appearance/escape.txt"))); });
            Test("拒绝绝对路径与脚本资源", () => { using var zip = Package(custom with { Id = "absolute" }, ("C:/escape.txt", "bad")); Reject(() => store.Import(zip)); using var script = Package(custom with { Id = "script" }, ("run.js", "alert(1)")); Reject(() => store.Import(script)); });
            Test("拒绝不完整资源引用及错误版本", () => { using var missing = Package(custom with { Id = "missing" }); Reject(() => store.Import(missing)); using var version = Package(baseManifest with { Id = "version", SchemaVersion = 99 }); Reject(() => store.Import(version)); });
            Test("拒绝重复路径和 null 变量", () => { using var duplicate = Package(baseManifest with { Id = "duplicate" }, ("A.css", "a"), ("a.css", "b")); Reject(() => store.Import(duplicate)); using var nulls = Package(baseManifest with { Id = "nulls", Tokens = null! }); Reject(() => store.Import(nulls)); });
            Test("重复包默认拒绝，当前包保持完整", () => { using var duplicate = Package(custom, ("frame.css", "new"), ("icons/chat.svg", "new")); Reject(() => store.Import(duplicate)); Check(File.ReadAllText(store.AssetPath(custom.Id, "frame.css")).Contains("stroke:red")); });
            Test("显式替换先校验并保留原包恢复副本", () => { using var invalid = Package(custom); Reject(() => store.Import(invalid, true)); using var replacement = Package(custom with { Name = "新名称" }, ("frame.css", "updated"), ("icons/chat.svg", "updated")); store.Import(replacement, true); Check(store.Find(custom.Id).Manifest.Name == "新名称"); Check(Directory.GetDirectories(project.StatePath("appearance/archive")).Length == 1); });
            Test("内建主题不能被用户包替换", () => { using var zip = Package(baseManifest); Reject(() => store.Import(zip, true)); Check(store.Find("karolina").BuiltIn); });
            Test("导出含当前变量并可在另一工程导入", () => {
                byte[] bytes = store.Export(custom.Id, preferences);
                string otherRoot = Path.Combine(temp, "other"); Directory.CreateDirectory(otherRoot);
                var other = new AppearanceStore(new ProjectContext(otherRoot), builtIn);
                using var exported = new MemoryStream(bytes); var imported = other.Import(exported);
                Check(imported.Manifest.Tokens["--k-pink"] == "#cc88bb"); Check(File.ReadAllText(other.AssetPath(custom.Id, "frame.css")) == "updated"); Check(other.Read().ThemeId == "karolina");
            });
            Test("内建导出产生可导入的自定义编号", () => { using var exported = new MemoryStream(store.Export("karolina")); var imported = store.Import(exported); Check(imported.Manifest.Id == "karolina-custom"); });
            Test("导出保留浅色变量覆盖且不修改内建清单", () => {
                using var exported = new MemoryStream(store.Export("karolina", new AppearancePreferences { Mode = "light", Tokens = new() { ["--k-text"] = "#ff0000" } }));
                var imported = store.Import(exported, true);
                Check(imported.Manifest.Tokens["--k-text"] == "#ff0000" && imported.Manifest.ColorModes["light"]["--k-text"] == "#ff0000");
                Check(store.Find("karolina").Manifest.ColorModes["light"]["--k-text"] == "#29304e");
            });
            Test("资源读取拒绝越界和未知类型", () => { Reject(() => store.AssetPath(custom.Id, "../preferences.json")); Reject(() => store.AssetPath(custom.Id, "run.js")); });
            Check(!Directory.EnumerateDirectories(project.StatePath("appearance")).Any(p => Path.GetFileName(p).StartsWith("import-")), "临时导入目录应清理");
            Console.WriteLine(JsonSerializer.Serialize(new { passed = count, failed = 0 }));
            return Task.FromResult(0);
        }
        catch (Exception error) { Console.Error.WriteLine(error); return Task.FromResult(1); }
        finally
        {
            string checkedTemp = Path.GetFullPath(temp);
            if (!checkedTemp.StartsWith(Path.GetFullPath(Path.GetTempPath()) + "karolina-appearance-", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("测试清理路径越界");
            Directory.Delete(checkedTemp, true);
        }
    }
}
