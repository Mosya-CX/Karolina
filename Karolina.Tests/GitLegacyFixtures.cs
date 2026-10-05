using System.Diagnostics;
using System.Text;

namespace Karolina.Core;

// 仅保留既有 Git 回归的历史测试夹具，不进入 Karolina 产品程序集。
public sealed record ReviewChange(string Status, string Path, string? OriginalPath, bool WorktreeText, bool IndexText, bool WorktreeOther, bool IndexOther);
public sealed record WorkingChange(string Status, string Path, string? OriginalPath, bool Text, bool Other);
public sealed record GitCommit(string Hash, string Title, string Author, string Date);
public sealed record GitConnection(string Provider, string? Remote, string[] Accounts, bool Authenticated, string Message);
public sealed record GitIndexLock(string Path, bool Exists, long Bytes, DateTime? Modified, bool GitRunning);
public sealed class GitService
{
    private readonly string root;
    private readonly Func<bool> hasRunningGit;
    public GitService(string root, Func<bool>? hasRunningGit = null)
    {
        this.root = root;
        this.hasRunningGit = hasRunningGit ?? (() => Process.GetProcessesByName("git").Any(p => { using (p) return !p.HasExited; }));
    }
    public async Task<GitConnection> Connection(CancellationToken ct = default)
    {
        string? remote = null;
        try { var target = await PushTarget(ct); remote = (await Git(ct, "config", "--get", $"remote.{target.Remote}.url")).Trim(); }
        catch (Exception e) when (e is IOException or InvalidOperationException) { try { remote = (await Git(ct, "config", "--get", "remote.origin.url")).Trim(); } catch (IOException) { } }
        bool github = remote != null && (Uri.TryCreate(remote, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com" || remote.StartsWith("git@github.com:", StringComparison.Ordinal));
        var accounts = await ProcessRunner.RunAsync("git", ["credential-manager", "github", "list"], root, ct: ct);
        string[] names = accounts.ExitCode == 0 ? accounts.Stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries) : [];
        return new("GitHub", remote, names, github && names.Length > 0, !github ? "本期只支持 GitHub；请先在 Git 工具配置 github.com 远端。" : names.Length > 0 ? "本机凭据已就绪；远端权限在 Pull/Push 时由 GitHub 核验。" : "尚未连接 GitHub，点击连接后在浏览器登录。");
    }
    private async Task RequireGithub(CancellationToken ct)
    {
        var connection = await Connection(ct);
        if (connection.Remote == null || !(Uri.TryCreate(connection.Remote, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com")) throw new InvalidOperationException("本期 Pull/Push 只支持 GitHub HTTPS 远端；SSH 或其它托管请使用现有 Git 工具。");
    }
    public async Task<string> ConnectGithub(CancellationToken ct = default)
    {
        await RequireGithub(ct);
        var result = await ProcessRunner.RunAsync("git", ["credential-manager", "github", "login", "--url", "https://github.com", "--browser"], root, ct: ct);
        if (result.ExitCode != 0) throw new IOException("GitHub 登录失败，请检查浏览器授权或 Git Credential Manager。" );
        return "GitHub 凭据连接完成；Pull/Push 会继续核验仓库权限。";
    }
    public async Task<GitCommit[]> History(CancellationToken ct = default)
    {
        var head = await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "HEAD"], root, ct: ct);
        if (head.ExitCode != 0) return [];
        string raw = await Git(ct, "log", "-n", "100", "--format=%H%x00%s%x00%an%x00%aI%x00");
        var fields = raw.Split('\0'); var result = new List<GitCommit>();
        for (int i = 0; i + 3 < fields.Length; i += 4) result.Add(new(fields[i].Trim(), fields[i+1], fields[i+2], fields[i+3]));
        return result.ToArray();
    }
    public Task<string> CommitDiff(string hash, CancellationToken ct = default)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(hash, "^[0-9a-f]{40,64}$")) throw new ArgumentException("无效提交编号");
        return Git(ct, "show", "--format=fuller", "--no-ext-diff", "--no-textconv", "--no-color", "--root", "--first-parent", hash, "--");
    }
    public async Task<GitIndexLock> IndexLock(CancellationToken ct = default)
    {
        string path = System.IO.Path.GetFullPath((await Git(ct, "rev-parse", "--path-format=absolute", "--git-path", "index.lock")).Trim());
        var info = new FileInfo(path); bool running = hasRunningGit();
        return new(path, info.Exists, info.Exists ? info.Length : 0, info.Exists ? info.LastWriteTimeUtc : null, running);
    }
    public async Task<string> RecoverIndexLock(CancellationToken ct = default)
    {
        var state = await IndexLock(ct);
        if (!state.Exists) return "索引锁已不存在，请刷新。";
        if (state.GitRunning || state.Bytes != 0 || state.Modified > DateTime.UtcNow.AddMinutes(-1)) throw new InvalidOperationException("锁仍可能正在使用或包含数据；请关闭其它 Git 操作后在现有 Git 工具处理。");
        for (FileSystemInfo? node = new FileInfo(state.Path); node != null; node = node is DirectoryInfo d ? d.Parent : ((FileInfo)node).Directory)
            if (node.Exists && (node.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不恢复链接路径的索引锁。");
        // 禁止其它写入者打开旧锁；保留文件而非直接删除。非空/新锁永不自动清理。
        using var held = new FileStream(state.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete);
        if (held.Length != 0 || File.GetLastWriteTimeUtc(state.Path) != state.Modified || (await IndexLock(ct)).GitRunning) throw new InvalidOperationException("锁状态已改变，请重试。");
        string archive = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Karolina", "recovery", "git-locks", RequirementStore.Hash(Encoding.UTF8.GetBytes(root)), "latest-index.lock");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archive)!); File.Move(state.Path, archive, true);
        return "已移走无进程占用的过期空索引锁；现有暂存内容保持原样。";
    }
    private async Task<string> GitInput(string input, CancellationToken ct, params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", new[] { "--no-optional-locks", "-c", "core.fsmonitor=false" }.Concat(args), root, input: input, ct: ct);
        if (result.ExitCode != 0) throw new IOException(result.Stderr);
        return result.Stdout;
    }
    private async Task<string> Git(CancellationToken ct, params string[] args)
    {
        var result = await ProcessRunner.RunAsync("git", new[] { "--no-optional-locks", "-c", "core.fsmonitor=false" }.Concat(args), root, ct: ct);
        if (result.ExitCode != 0) throw new IOException(result.Stderr);
        return result.Stdout;
    }
    public Task<string> Branch(CancellationToken ct = default) => Git(ct, "branch", "--show-current");
    public async Task<WorkingChange[]> WorkingChanges(CancellationToken ct = default)
    {
        var status = await Status(ct);
        var head = await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "HEAD"], root, ct: ct);
        var stats = new Dictionary<string, bool>(StringComparer.Ordinal);
        if (head.ExitCode == 0)
            foreach (string item in (await Git(ct, "diff", "HEAD", "--numstat", "-z", "--no-ext-diff", "--no-textconv", "--no-renames")).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = item.Split('\t', 3); if (fields.Length != 3) throw new InvalidDataException("Git 差异统计不完整");
                stats[fields[2]] = fields[0] != "-" && fields[1] != "-" && (fields[0] != "0" || fields[1] != "0");
            }
        var result = new List<WorkingChange>();
        foreach (var change in status)
        {
            bool text = stats.GetValueOrDefault(change.Path) || change.Status.Contains('R') && change.OriginalPath != null && stats.GetValueOrDefault(change.OriginalPath);
            bool other = stats.ContainsKey(change.Path) && !text || change.Status.Contains('R') && change.OriginalPath != null && !text;
            if (change.Status == "??" || head.ExitCode != 0 && File.Exists(System.IO.Path.Combine(root, change.Path)))
            {
                string content = await Diff(change with { Status = "??" }, ct);
                text = content.StartsWith("Untracked file (not a Git patch):\n") && content.Length > "Untracked file (not a Git patch):\n".Length; other = !text;
            }
            if (text || other) result.Add(new(change.Status, change.Path, change.OriginalPath, text, other));
        }
        return result.ToArray();
    }
    public async Task<string> AreaDiff(Change change, string area, CancellationToken ct = default)
    {
        if (area is not ("worktree" or "index" or "combined")) throw new ArgumentException("无效差异区域");
        if (area == "combined")
        {
            var head = await ProcessRunner.RunAsync("git", ["rev-parse", "--verify", "HEAD"], root, ct: ct);
            if (head.ExitCode != 0) change = change with { Status = "??" };
        }
        if (change.Status == "??")
        {
            string content = await Diff(change, ct);
            if (content.StartsWith("Untracked file (not a Git patch):\n"))
            {
                string text = content["Untracked file (not a Git patch):\n".Length..].Replace("\r\n", "\n");
                if (text.Length == 0) return $"新建空文件：{change.Path}（0 字节，没有正文差异）。";
                bool newline = text.EndsWith('\n');
                var lines = (newline ? text[..^1] : text).Split('\n');
                return $"diff --git a/{change.Path} b/{change.Path}\nnew file\n@@ -0,0 +1,{lines.Length} @@\n" + string.Join('\n', lines.Select(line => "+" + line)) + (newline ? "\n" : "\n\\ No newline at end of file\n");
            }
            return content.Replace("Untracked binary file.", "二进制新文件，无法显示文本差异。").Replace("Untracked file exceeds 1 MB; open it in your editor.", "新文件超过 1 MB，请在编辑器查看。");
        }
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "--no-color", "--find-renames", "--unified=4" };
        if (area == "index") args.Add("--cached");
        if (area == "combined") args.Add("HEAD");
        args.Add("--"); args.Add(":(literal)" + change.Path);
        if (change.Status.Contains('R') && change.OriginalPath != null) args.Add(":(literal)" + change.OriginalPath);
        string patch = await Git(ct, args.ToArray());
        return patch.Length == 0 ? "当前区域已无文本差异，请刷新文件列表。文件可能只改变权限或已被外部操作更新。" : patch;
    }
    public async Task<string> Action(string action, string[] paths, string? message, CancellationToken ct = default)
    {
        if (action == "commit-selected")
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 20000) throw new ArgumentException("请填写提交说明");
            var changes = await Status(ct);
            if (paths.Length == 0 || paths.Length > 10000 || paths.Any(p => !changes.Any(c => c.Path == p))) throw new ArgumentException("请选择当前变更文件");
            if (changes.Any(c => c.Status.Contains('U') || c.Status is "AA" or "DD")) throw new InvalidOperationException("仓库存在未解决冲突，请先在 Git 工具处理。");
            var selected = paths.Distinct().SelectMany(p => changes.Where(c => c.Path == p).SelectMany(c => c.OriginalPath == null || !c.Status.Contains('R') ? new[] { c.Path } : new[] { c.Path, c.OriginalPath })).Distinct().ToArray();
            if (changes.Any(c => paths.Contains(c.Path) && c.Status.Contains('R') && c.OriginalPath != null && changes.Any(other => other.Path == c.OriginalPath) && !paths.Contains(c.OriginalPath)))
                throw new InvalidOperationException("重命名源路径又出现了独立变更，请一起勾选源路径，或使用 Git 工具分别处理。");
            string input = string.Join('\0', selected) + '\0';
            // 已暂存重命名的旧路径已不在索引，不能再次传给 add；commit --only 仍需它以记录删除。
            var indexed = new HashSet<string>((await Git(ct, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
            string addInput = string.Join('\0', selected.Where(p => indexed.Contains(p) || File.Exists(System.IO.Path.Combine(root, p)) || Directory.Exists(System.IO.Path.Combine(root, p)))) + '\0';
            await GitInput(addInput, ct, "--literal-pathspecs", "add", "--pathspec-from-file=-", "--pathspec-file-nul");
            // --only 只提交当前选择，不带走其它工具或旧流程留下的未选择索引内容。
            return await GitInput(input, ct, "--literal-pathspecs", "commit", "--only", "-m", message, "--pathspec-from-file=-", "--pathspec-file-nul");
        }
        if (action is "stage" or "unstage")
        {
            var status = await Status(ct);
            if (paths.Length == 0 || paths.Length > 10000 || paths.Any(p => !status.Any(c => c.Path == p))) throw new ArgumentException("请选择当前变更文件");
            var selected = paths.Distinct().SelectMany(p => status.Where(c => c.Path == p).SelectMany(c => c.OriginalPath == null || !c.Status.Contains('R') ? new[] { c.Path } : new[] { c.Path, c.OriginalPath })).Distinct().ToArray();
            string input = string.Join('\0', selected) + '\0';
            if (action == "stage") return await GitInput(input, ct, "--literal-pathspecs", "add", "--pathspec-from-file=-", "--pathspec-file-nul");
            var head = await ProcessRunner.RunAsync("git", new[] { "rev-parse", "--verify", "HEAD" }, root, ct: ct);
            return head.ExitCode == 0
                ? await GitInput(input, ct, "--literal-pathspecs", "reset", "--quiet", "HEAD", "--pathspec-from-file=-", "--pathspec-file-nul")
                : await GitInput(input, ct, "--literal-pathspecs", "rm", "--cached", "-f", "--pathspec-from-file=-", "--pathspec-file-nul");
        }
        if (action == "commit")
        {
            if (string.IsNullOrWhiteSpace(message) || message.Length > 20000) throw new ArgumentException("请填写提交说明");
            if (!(await Status(ct)).Any(c => c.Status != "??" && c.Status[0] != ' ')) throw new InvalidOperationException("暂存区没有文件");
            return await Git(ct, "commit", "-m", message);
        }
        if (action == "push")
        {
            await RequireGithub(ct);
            var target = await PushTarget(ct);
            return await Git(ct, "-c", "push.followTags=false", "push", "--no-follow-tags", "--", target.Remote, "HEAD:" + target.Ref);
        }
        if (action == "pull")
        {
            await RequireGithub(ct);
            if ((await Status(ct)).Length != 0) throw new InvalidOperationException("工作区或暂存区有改动，请先提交或使用其它 Git 工具处理；Karolina 不自动 Stash。");
            var target = await PushTarget(ct);
            return await Git(ct, "pull", "--ff-only", "--no-rebase", "--no-autostash", "--", target.Remote, target.Ref);
        }
        if (action == "recover-lock") return await RecoverIndexLock(ct);
        if (action == "connect-github") return await ConnectGithub(ct);
        throw new ArgumentException("不支持的 Git 操作");
    }
    public async Task<(string Remote, string Ref)> PushTarget(CancellationToken ct = default)
    {
        string branch = (await Branch(ct)).Trim();
        if (branch.Length == 0) throw new InvalidOperationException("游离 HEAD 不能直接推送");
        await Git(ct, "rev-parse", "--verify", "HEAD");
        string remote = (await Git(ct, "config", "--get", $"branch.{branch}.remote")).Trim();
        string target = (await Git(ct, "config", "--get", $"branch.{branch}.merge")).Trim();
        if (remote.Length == 0 || remote == "." || remote.StartsWith('-') || !target.StartsWith("refs/heads/") || target.Contains('\n')) throw new InvalidOperationException("当前分支没有有效的远端上游，请先在 Git 工具中配置上游");
        return (remote, target);
    }
    public async Task<Change[]> Status(CancellationToken ct = default)
    {
        var raw = await Git(ct, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var parts = raw.Split('\0'); var changes = new List<Change>();
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length < 4) continue;
            string status = parts[i][..2], path = parts[i][3..]; string? original = null;
            if (status.Contains('R') || status.Contains('C')) { if (++i >= parts.Length) throw new InvalidDataException("Incomplete rename"); original = parts[i]; }
            changes.Add(new(status, path, original));
        }
        return changes.ToArray();
    }
    public async Task<ReviewChange[]> ReviewStatus(CancellationToken ct = default)
    {
        // numstat 不读取 HEAD，因此初始仓库同样有效；一次读取每个区域，避开逐文件启动 Git。
        async Task<Dictionary<string, bool>> Stats(bool index)
        {
            var args = new List<string> { "diff", "--numstat", "-z", "--no-ext-diff", "--no-textconv", "--no-renames" };
            if (index) args.Add("--cached");
            var result = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (string item in (await Git(ct, args.ToArray())).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = item.Split('\t', 3);
                if (parts.Length != 3) throw new InvalidDataException("Git 差异统计不完整");
                result[parts[2]] = parts[0] != "-" && parts[1] != "-" && (parts[0] != "0" || parts[1] != "0");
            }
            return result;
        }
        var status = await Status(ct); var work = await Stats(false); var staged = await Stats(true); var changes = new List<ReviewChange>();
        foreach (var c in status)
        {
            bool workText = work.GetValueOrDefault(c.Path), indexText = staged.GetValueOrDefault(c.Path);
            bool workOther = work.ContainsKey(c.Path) && !workText, indexOther = staged.ContainsKey(c.Path) && !indexText;
            if (c.OriginalPath != null) { indexText |= staged.GetValueOrDefault(c.OriginalPath); indexOther |= !indexText; workOther |= c.Status[1] == 'R'; }
            if (c.Status == "??")
            {
                string diff = await Diff(c, ct);
                workText = diff.StartsWith("Untracked file (not a Git patch):\n") && diff.Length > "Untracked file (not a Git patch):\n".Length;
                workOther = !workText;
            }
            if (workText || indexText || workOther || indexOther) changes.Add(new(c.Status, c.Path, c.OriginalPath, workText, indexText, workOther, indexOther));
        }
        return changes.ToArray();
    }
    public async Task<string> Diff(Change change, CancellationToken ct = default)
    {
        if (change.Status == "??")
        {
            var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(root, change.Path));
            if (!path.StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\','/') + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe Git path");
            for (FileSystemInfo? p = new FileInfo(path); p != null && !p.FullName.Equals(System.IO.Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase); p = p is DirectoryInfo d ? d.Parent : ((FileInfo)p).Directory)
                if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked file cannot be previewed");
            if (new FileInfo(path).Length > 1_000_000) return "Untracked file exceeds 1 MB; open it in your editor.";
            var bytes = await File.ReadAllBytesAsync(path, ct);
            return bytes.Contains((byte)0) ? "Untracked binary file." : "Untracked file (not a Git patch):\n" + Encoding.UTF8.GetString(bytes);
        }
        var args = new List<string> { "diff", "--no-ext-diff", "--no-textconv", "HEAD", "--", change.Path };
        if (change.OriginalPath != null) args.Add(change.OriginalPath);
        return await Git(ct, args.ToArray());
    }
}
