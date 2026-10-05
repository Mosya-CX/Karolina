using System.Text.Json;

namespace Karolina.Core;

/// <summary>
/// Codex app-server 协议端口。调用者拥有传入会话的生命周期；实现不得发布借用 JsonDocument 的元素。
/// 保留现有 RPC 方法/消息语义，尚不是跨 Harness 的统一领域协议。
/// </summary>
public interface ICodexSession : IDisposable
{
    bool Connected { get; }
    string? Error { get; }
    string Executable { get; }
    event Action<JsonElement>? Notification;
    event Action<JsonElement>? ServerRequest;
    Task Connect(string root, CancellationToken ct = default);
    Task<JsonElement> Request(string method, object args, CancellationToken ct = default);
    Task Respond(JsonElement id, object result);
    Task Reject(JsonElement id, string message);
}
