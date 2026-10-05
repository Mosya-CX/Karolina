using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Karolina.Desktop;

/// <summary>窗口和托盘由 WinForms 管理；WebView2 只渲染工作台页面。</summary>
internal sealed class DesktopWindow : Form
{
    private readonly Workbench host;
    private readonly string profile;
    private readonly NotifyIcon tray;
    private readonly System.Drawing.Icon applicationIcon = LoadApplicationIcon();
    private readonly WebView2 view = new() { Dock = DockStyle.Fill, TabIndex = 0 };
    private Process? browser;
    private Task initialization = Task.CompletedTask;
    private bool exiting, allowClose;

    public DesktopWindow(Workbench host, string root, string profile)
    {
        this.host = host; this.profile = profile;
        Text = "Karolina · 工程工作台"; Icon = applicationIcon;
        StartPosition = FormStartPosition.CenterScreen; Size = new(1380, 880); MinimumSize = new(900, 650);
        Controls.Add(view);
        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Karolina", null, (_, _) => Restore());
        menu.Items.Add("最小化", null, (_, _) => { Show(); WindowState = FormWindowState.Minimized; });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("完全退出", null, (_, _) => RequestExit(true));
        string trayTitle = "Karolina · " + Path.GetFileName(root);
        tray = new NotifyIcon { Icon = Icon, Text = trayTitle[..Math.Min(63, trayTitle.Length)], ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Restore();
        Shown += async (_, _) => { initialization = InitializeContent(); await initialization; };
        VisibleChanged += (_, _) => host.WindowVisible = Visible;
        FormClosing += (_, e) =>
        {
            if (allowClose) return;
            e.Cancel = true;
            if (!exiting && e.CloseReason == CloseReason.UserClosing) Hide();
            else OnUi(() => RequestExit(false));
        };
    }

    private static System.Drawing.Icon LoadApplicationIcon()
    {
        using var source = typeof(DesktopWindow).Assembly.GetManifestResourceStream("Karolina.Desktop.ApplicationIcon")
            ?? throw new InvalidDataException("Karolina 图标资源缺失");
        using var icon = new System.Drawing.Icon(source);
        return (System.Drawing.Icon)icon.Clone();
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0112 && (message.WParam.ToInt64() & 0xfff0) == 0xf060 && !exiting)
        {
            Hide(); message.Result = IntPtr.Zero; return;
        }
        base.WndProc(ref message);
    }

    private void OnUi(Action action)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(() => { if (!IsDisposed) action(); }); }
        catch (InvalidOperationException) when (exiting || IsDisposed) { }
    }

    private void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); view.Focus(); }

    private async void RequestExit(bool confirm)
    {
        if (exiting) return;
        if (confirm && (host.IsBusy || host.EditorDirty) && MessageBox.Show(this, "当前有未保存正文或正在执行的 Codex 任务。完全退出会终止任务，已写入工程的文件保留。是否退出？", "完全退出 Karolina", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        exiting = true; host.Close();
        try { await initialization.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (TimeoutException) { Environment.ExitCode = 1; }
        finally { allowClose = true; Close(); }
    }

    private async Task InitializeContent()
    {
        try
        {
            string url = await host.Start();
            if (exiting) { host.Close(); return; }
            host.WindowHandle = Handle.ToInt64(); host.WindowVisible = Visible; host.TrayAvailable = true;
            host.MinimizeWindow = () => OnUi(() => WindowState = FormWindowState.Minimized);
            host.RestoreWindow = () => OnUi(Restore);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile, options: new CoreWebView2EnvironmentOptions { Language = "zh-CN", ExclusiveUserDataFolderAccess = true });
            if (!Path.GetFullPath(environment.UserDataFolder).Equals(Path.GetFullPath(profile), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("WebView2 用户数据目录被环境或系统策略覆盖；请取消该覆盖，Karolina 只使用自己的独立目录。");
            // 关闭期间继续泵送消息，待初始化完成后统一 Dispose；不会挂接已关闭的窗口。
            await view.EnsureCoreWebView2Async(environment);
            browser = Process.GetProcessById(checked((int)view.CoreWebView2.BrowserProcessId));
            if (exiting) return;
            var core = view.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            view.ZoomFactor = 1;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            var origin = new Uri(url);
            core.NavigationStarting += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) && target.Scheme == origin.Scheme && target.Authority == origin.Authority) return;
                e.Cancel = true;
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var target) && target.Scheme is "http" or "https")
                    OnUi(() => OpenExternal(target));
            };
            core.ProcessFailed += (_, e) => OnUi(() =>
            {
                if (exiting) return;
                host.ReportDesktopError("页面进程状态：" + e.ProcessFailedKind);
                // GPU/utility 等辅助进程由 Runtime 恢复；暂时无响应也不能终止 Agent。
                if (e.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited)) return;
                if (MessageBox.Show(this, "页面进程异常结束：" + e.ProcessFailedKind + "。已写入的工程文件保留，未保存网页输入可能已不可用。是否完全退出 Karolina？选择“否”会保留后台 Codex 任务。", "Karolina 页面异常", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                    RequestExit(false);
            });
            core.Navigate(url);
            host.ContentHandle = view.Handle.ToInt64();
            view.Focus();
            _ = WatchHost();
        }
        catch (Exception e)
        {
            if (!exiting) OnUi(() =>
            {
                MessageBox.Show(this, "页面初始化失败：" + e.Message + "\n请确认已安装 Microsoft Edge WebView2 Runtime。", "Karolina 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                RequestExit(false);
            });
        }
    }

    private void OpenExternal(Uri target)
    {
        try { Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception e) { MessageBox.Show(this, e.Message, "无法打开外部链接", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private async Task WatchHost() { await host.Wait(); OnUi(() => RequestExit(false)); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            host.Close(); tray.Visible = false; tray.ContextMenuStrip?.Dispose(); tray.Dispose();
            view.Dispose();
            if (browser != null)
            {
                try { if (!browser.HasExited && !browser.WaitForExit(3000)) { browser.Kill(true); browser.WaitForExit(); } }
                catch (InvalidOperationException) { }
                finally { browser.Dispose(); browser = null; }
            }
        }
        base.Dispose(disposing);
        if (disposing) applicationIcon.Dispose();
    }
}
