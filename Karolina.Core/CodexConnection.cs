using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Karolina.Core;

public sealed class CodexRpcException(string message) : IOException(message);

/// <summary>本机 Codex app-server 的 stdio JSON-RPC 连接；不读取或转发凭据文件。</summary>
public sealed class CodexConnection : ICodexSession
{
    private Process? process;
    private Task? readTask;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
    private readonly SemaphoreSlim writer = new(1, 1);
    private long sequence;
    public bool Connected { get; private set; }
    public string? Error { get; private set; }
    public event Action<JsonElement>? Notification;
    public event Action<JsonElement>? ServerRequest;
    public string Executable { get; private set; } = "";
    public static string Discover()
    {
        var configured = Environment.GetEnvironmentVariable("KAROLINA_CODEX_EXE");
        if (!string.IsNullOrWhiteSpace(configured)) return configured;
        var bin = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin");
        return Directory.Exists(bin) ? Directory.GetFiles(bin, "codex.exe", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? "codex" : "codex";
    }
    public async Task Connect(string root, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (Connected) return;
        if (process != null) { Dispose(); if (readTask != null) await readTask; process = null; }
        Error = null;
        Executable = Discover();
        var info = new ProcessStartInfo(Executable) { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, StandardInputEncoding = new UTF8Encoding(false) };
        info.ArgumentList.Add("app-server");
        process = Process.Start(info) ?? throw new IOException("Codex 未能启动");
        using var stopping = ct.Register(Dispose);
        readTask = ReadOutput(process);
        _ = DrainErrors(process);
        try
        {
            await Request("initialize", new { clientInfo = new { name = "karolina", title = "Karolina", version = "0.2.0" }, capabilities = new { experimentalApi = false } }, ct);
            await Write(new { method = "initialized" }, ct);
            if (process.HasExited) throw new IOException("Codex 初始化后连接已关闭");
            Connected = true;
        }
        catch { Dispose(); throw; }
    }
    private async Task DrainErrors(Process current)
    {
        try { while (await current.StandardError.ReadLineAsync() is { } line) if (line.Length < 4096) Error = line; }
        catch (Exception e) when (e is IOException or ObjectDisposedException) { Error = e.Message; }
    }
    private async Task ReadOutput(Process current)
    {
        Exception failure = new IOException("Codex 连接已关闭");
        try
        {
            while (await current.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 8_000_000) throw new InvalidDataException("Codex 单条消息超过容量限制");
                using var json = JsonDocument.Parse(line); var e = json.RootElement.Clone();
                if (e.TryGetProperty("method", out _))
                {
                    if (e.TryGetProperty("id", out _)) ServerRequest?.Invoke(e); else Notification?.Invoke(e);
                }
                else if (e.TryGetProperty("id", out var id) && id.TryGetInt64(out var n) && pending.TryRemove(n, out var completion))
                {
                    if (e.TryGetProperty("error", out var error)) completion.TrySetException(new CodexRpcException(error.GetRawText()));
                    else completion.TrySetResult(e.GetProperty("result").Clone());
                }
            }
        }
        catch (Exception e) { failure = e; }
        finally
        {
            // 读通道失败不代表子进程停止。终止整个工具进程树后才允许工作台释放工作区锁。
            try { if (!current.HasExited) { current.Kill(true); current.WaitForExit(); } } catch (InvalidOperationException) { }
            Connected = false; Error = failure.Message;
            foreach (var pair in pending) if (pending.TryRemove(pair.Key, out var task)) task.TrySetException(failure);
            Notification?.Invoke(JsonSerializer.SerializeToElement(new { method = "karolina/disconnected", @params = new { message = failure.Message } }));
        }
    }
    public async Task<JsonElement> Request(string method, object args, CancellationToken ct = default)
    {
        long id = Interlocked.Increment(ref sequence);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        try { await Write(new { id, method, @params = args }, ct); return await completion.Task.WaitAsync(TimeSpan.FromSeconds(90), ct); }
        finally { pending.TryRemove(id, out _); }
    }
    public Task Respond(JsonElement id, object result) => Write(new { id, result });
    public Task Reject(JsonElement id, string message) => Write(new { id, error = new { code = -32601, message } });
    private async Task Write(object value, CancellationToken ct = default)
    {
        if (process == null || process.HasExited) throw new IOException("Codex 未连接");
        await writer.WaitAsync(ct);
        try { await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(value).AsMemory(), ct); await process.StandardInput.FlushAsync(ct); }
        finally { writer.Release(); }
    }
    public void Dispose()
    {
        Connected = false;
        if (process != null) { try { if (!process.HasExited) { process.Kill(true); process.WaitForExit(); } process.Dispose(); } catch (InvalidOperationException) { } }
    }
}
