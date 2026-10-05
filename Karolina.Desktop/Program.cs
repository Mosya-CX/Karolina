namespace Karolina.Desktop;
internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Workbench? host = null;
        try
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karolina");
            Directory.CreateDirectory(local);
            string lastProject = Path.Combine(local, "last-project.txt");
            string? configuredProject = args.FirstOrDefault(a => !a.StartsWith("--"));
            string? rememberedProject = File.Exists(lastProject) ? File.ReadAllText(lastProject).Trim() : null;
            var project = new Karolina.Core.ProjectContext(configuredProject ?? rememberedProject ?? throw new ArgumentException($"未配置 Unity 工程。请先在“{lastProject}”中填写工程根目录，然后直接重新打开 Karolina.Desktop.exe。"), requireUnity: true);
            string root=project.Root;
            using var instance = args.Contains("--serve") ? null : new FileStream(project.StatePath("desktop.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            project.RecoverLegacyIdentity();
            host = new Workbench(root);
            if (args.Contains("--serve")) { Console.WriteLine(host.Start().GetAwaiter().GetResult()); host.Wait().GetAwaiter().GetResult(); return; }
            File.WriteAllText(lastProject, root);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            string profile = Path.Combine(local, "webview-profile-current", project.Key);
            using var window = new DesktopWindow(host, root, profile);
            Application.Run(window);
        }
        catch (Exception e) { if (args.Contains("--serve")) { Console.Error.WriteLine(e.Message); Environment.ExitCode = 1; } else MessageBox.Show(e.Message, "Karolina 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { if (host != null) host.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
