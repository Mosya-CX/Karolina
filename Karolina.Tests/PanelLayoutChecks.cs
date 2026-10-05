using Karolina.Core;
using Karolina.Core.Appearance;

internal static partial class Program
{
    private static Task<int> PanelLayoutChecks()
    {
        string temp = Path.Combine(Path.GetTempPath(), "karolina-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            var store = new PanelLayoutStore(new ProjectContext(temp));
            int count = 0;
            void Test(string name, Action action) { action(); count++; Console.WriteLine("PASS " + name); }
            PanelLayoutPreferences Value(double size, string? writer = null, long sequence = 0) => new() { Sizes = new() { ["sidebar"] = size }, WriterId = writer, Sequence = sequence };
            void Reject(PanelLayoutPreferences value) { bool rejected = false; try { store.Save(value); } catch (ArgumentException) { rejected = true; } Check(rejected); }
            Test("默认布局为空", () => Check(store.Read().Sizes.Count == 0));
            Test("尺寸重建后恢复", () => { store.Save(Value(320)); Check(new PanelLayoutStore(new ProjectContext(temp)).Read().Sizes["sidebar"] == 320); });
            Test("未知面板拒绝", () => Reject(new() { Sizes = new() { ["unknown"] = 300 } }));
            Test("过小尺寸拒绝", () => Reject(Value(100)));
            Test("过大尺寸拒绝", () => Reject(Value(700)));
            Test("非有限尺寸拒绝", () => { Reject(Value(double.NaN)); Reject(Value(double.PositiveInfinity)); });
            Test("空尺寸拒绝", () => Reject(new() { Sizes = null! }));
            Test("拒绝不破坏已保存布局", () => Check(store.Read().Sizes["sidebar"] == 320));
            string writer = Guid.NewGuid().ToString("D");
            Test("退出保存先到达时忽略迟到旧请求", () => { store.Save(Value(360, writer, 2)); store.Save(Value(300, writer, 1)); Check(store.Read().Sizes["sidebar"] == 360); });
            Test("另一窗口保持正常最后写入语义", () => { store.Save(Value(340, Guid.NewGuid().ToString("D"), 1)); Check(store.Read().Sizes["sidebar"] == 340); });
            Test("无效保存序号拒绝", () => { Reject(Value(330, writer, 0)); Reject(Value(330, "bad", 1)); });
            Test("恢复默认只清尺寸", () => { store.Save(new()); Check(store.Read().Sizes.Count == 0); });
            Console.WriteLine($"TOTAL {count} passed");
            return Task.FromResult(0);
        }
        finally { Directory.Delete(temp, true); }
    }
}
