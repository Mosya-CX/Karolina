using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Karolina.Core;

public sealed class McpClient : IDisposable, IUnityToolClient
{
    private readonly HttpClient http;
    private readonly Uri endpoint;
    private string? session;
    private string protocol = "2025-06-18";
    private long nextId;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromMinutes(6);
    public bool Connected { get; private set; }
    public JsonElement[] Tools { get; private set; } = [];
    public McpClient(string url, HttpMessageHandler? handler = null)
    {
        endpoint = new Uri(url);
        if (!endpoint.IsLoopback || endpoint.Scheme != "http" || endpoint.UserInfo.Length != 0) throw new ArgumentException("MVP supports local HTTP Unity MCP endpoints only");
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(6) };
    }
    public static string Discover(string root)
    {
        var path = Path.Combine(root, ".mcp.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.GetProperty("mcpServers").TryGetProperty("ai-game-developer", out var server)) throw new InvalidDataException("Unity MCP config missing ai-game-developer");
        return server.GetProperty("url").GetString() ?? throw new InvalidDataException("MCP URL missing");
    }
    public async Task Connect(CancellationToken ct = default)
    {
        Connected = false; session = null; Tools = [];
        var init = await Request("initialize", new { protocolVersion = protocol, capabilities = new { }, clientInfo = new { name = "Karolina", version = "0.1.0" } }, ct);
        protocol = init.GetProperty("protocolVersion").GetString()!;
        if (protocol is not ("2025-06-18" or "2025-03-26" or "2024-11-05")) throw new InvalidDataException("Unsupported negotiated MCP protocol: " + protocol);
        await Send(new { jsonrpc = "2.0", method = "notifications/initialized" }, null, ct);
        var tools = new List<JsonElement>(); string? cursor = null;
        do
        {
            var list = await Request("tools/list", cursor == null ? new { } : (object)new { cursor }, ct);
            tools.AddRange(list.GetProperty("tools").EnumerateArray().Select(t => t.Clone()));
            cursor = list.TryGetProperty("nextCursor", out var c) ? c.GetString() : null;
            if (tools.Count > 10_000) throw new InvalidDataException("Tool list too large");
        } while (cursor != null);
        Tools = tools.ToArray(); Connected = true;
    }
    public string Tool(string name) => Tools.FirstOrDefault(t => t.GetProperty("name").GetString() == name || t.GetProperty("name").GetString()?.EndsWith("_" + name.Replace('-', '_'), StringComparison.Ordinal) == true) is var match && match.ValueKind != JsonValueKind.Undefined
        ? match.GetProperty("name").GetString()! : throw new KeyNotFoundException("MCP tool unavailable: " + name);
    public void ValidateTool(string name)=>Tool(name);
    public async Task<JsonElement> Call(string name, object arguments, CancellationToken ct = default)
    {
        if (!Connected) throw new InvalidOperationException("Connect Unity MCP first");
        var result = await Request("tools/call", new { name = Tool(name), arguments }, ct);
        if (result.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True) throw new InvalidDataException("MCP tool failed: " + result.GetRawText());
        return result;
    }
    public async Task VerifyProject(string root, CancellationToken ct = default)
    {
        var result = await Call("script-execute", new { className = "KarolinaProjectIdentity", methodName = "Read", csharpCode = "public static class KarolinaProjectIdentity { public static string Read() { return UnityEngine.Application.dataPath; } }" }, ct);
        var payload = Payload(result);
        if (!payload.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String) throw new InvalidDataException("Unity project identity unavailable");
        string expected = Path.GetFullPath(Path.Combine(root, "Assets")), actual = Path.GetFullPath(value.GetString()!);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"Unity project mismatch: selected {expected}, MCP {actual}");
    }
    private async Task<JsonElement> Request(string method, object args, CancellationToken ct)
    {
        long id = Interlocked.Increment(ref nextId);
        return await Send(new { jsonrpc = "2.0", id, method, @params = args }, id, ct);
    }
    private async Task<JsonElement> Send(object body, long? id, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(RequestTimeout);
        ct = deadline.Token;
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (session != null) request.Headers.Add("Mcp-Session-Id", session);
        request.Headers.Add("MCP-Protocol-Version", protocol);
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"MCP HTTP {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        if (response.Headers.TryGetValues("Mcp-Session-Id", out var values)) session = values.Single();
        if (id == null) return default;
        JsonElement? Match(string json)
        {
            using var doc = JsonDocument.Parse(json); var item = doc.RootElement;
            if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var rid) || rid.ToString() != id.Value.ToString()) return null;
            if (item.TryGetProperty("error", out var error)) throw new InvalidDataException("MCP JSON-RPC error: " + error.GetRawText());
            if (!item.TryGetProperty("result", out var result)) throw new InvalidDataException("MCP result missing");
            return result.Clone();
        }
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
        {
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct)); var data = new StringBuilder();
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0)
                {
                    if (data.Length > 0) { var found = Match(data.ToString()); if (found.HasValue) return found.Value; data.Clear(); }
                }
                else if (line.StartsWith("data:", StringComparison.Ordinal)) { if (data.Length > 0) data.Append('\n'); data.Append(line[5..].TrimStart(' ')); }
                if (data.Length > 16_000_000) throw new InvalidDataException("MCP event too large");
            }
            throw new EndOfStreamException("MCP stream ended before matching response");
        }
        if (response.Content.Headers.ContentType?.MediaType != "application/json") throw new InvalidDataException("Unsupported MCP content type");
        return Match(await response.Content.ReadAsStringAsync(ct)) ?? throw new InvalidDataException("MCP response ID mismatch");
    }
    public static JsonElement Payload(JsonElement toolResult)
    {
        if (toolResult.ValueKind == JsonValueKind.Object && toolResult.TryGetProperty("Summary", out _)) return toolResult.Clone();
        if (toolResult.TryGetProperty("structuredContent", out var structured)) return structured.TryGetProperty("result", out var nested) ? nested : structured;
        if (toolResult.TryGetProperty("content", out var content))
            foreach (var block in content.EnumerateArray())
                if (block.TryGetProperty("text", out var text))
                    try { using var json = JsonDocument.Parse(text.GetString()!); return json.RootElement.TryGetProperty("result", out var result) ? result.Clone() : json.RootElement.Clone(); } catch (JsonException) { }
        throw new InvalidDataException("No interpretable Unity result; verification is pending");
    }
    public static string TestVerdict(JsonElement result)
    {
        var payload = Payload(result);
        if (!payload.TryGetProperty("Summary", out var summary) || !summary.TryGetProperty("Status", out var status)) throw new InvalidDataException("No terminal test Summary; verification is pending");
        int total = summary.GetProperty("TotalTests").GetInt32(), passed = summary.GetProperty("PassedTests").GetInt32(), failed = summary.GetProperty("FailedTests").GetInt32(), skipped = summary.GetProperty("SkippedTests").GetInt32();
        // Unity MCP 0.84.3 reports the entire discovered tree as TotalTests for filtered runs.
        // Require actual per-case terminal evidence; do not infer executed count from that field.
        if (status.GetString() != "Passed" || total < passed || passed <= 0 || failed != 0 || skipped != 0 || !payload.TryGetProperty("Results", out var results)) throw new InvalidDataException($"Tests not fully verified: {summary.GetRawText()}");
        var cases = results.EnumerateArray().ToArray();
        if (cases.Length != passed || cases.Any(t => t.GetProperty("Status").GetString() != "Passed") || cases.Select(t => t.GetProperty("Name").GetString()).Distinct().Count() != passed)
            throw new InvalidDataException("Missing, duplicate or failed individual test evidence; request includePassingTests=true");
        return $"Passed {passed}/{passed} executed cases (server discovered tree: {total})";
    }
    public void Dispose() => http.Dispose();
}
