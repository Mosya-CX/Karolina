using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Karolina.Core;

public sealed record Amendment(string Target, string Scope);
public sealed class Requirement
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Domain { get; set; } = "";
    public string[] Domains { get; set; } = [];
    public string Kind { get; set; } = "Proposal";
    public string Status { get; set; } = "Proposed";
    public int Priority { get; set; }
    public string Path { get; set; } = "";
    public string[] Supersedes { get; set; } = [];
    public string[] Related { get; set; } = [];
    public Amendment[] Amendments { get; set; } = [];
    public JsonElement[] Sources { get; set; } = [];
    public string? LegacyId { get; set; }
    public override string ToString() => $"{Id} [{Status}] {Title}";
}
public sealed class Catalog
{
    public int SchemaVersion { get; set; }
    public List<Requirement> Entries { get; set; } = [];
    public Dictionary<string, string> LegacyAliases { get; set; } = [];
    public JsonElement[] Domains { get; set; } = [];
    public JsonElement[] KnownIssues { get; set; } = [];
}
public sealed record Projection(Requirement[] ReadSet, Requirement[] History, string[] Overrides, JsonElement[] KnownIssues);

public sealed class RequirementStore
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, WriteIndented = true };
    public string Root { get; }
    public Catalog Catalog { get; private set; }
    private string catalogFingerprint;
    public RequirementStore(string root)
    {
        Root = System.IO.Path.GetFullPath(root);
        var bytes = File.ReadAllBytes(SafePath("Docs/Requirements/catalog.json"));
        catalogFingerprint = Hash(bytes);
        Catalog = JsonSerializer.Deserialize<Catalog>(bytes, Json) ?? throw new InvalidDataException("Empty catalog");
        Validate();
    }
    public string SafePath(string relative, string? within = null)
    {
        if (System.IO.Path.IsPathRooted(relative)) throw new InvalidDataException("Absolute path forbidden: " + relative);
        string target = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, relative));
        string boundary = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, within ?? ""));
        if (!target.StartsWith(boundary.TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path escapes workspace: " + relative);
        for (var p = new FileInfo(target) as FileSystemInfo; p is not null && !p.FullName.Equals(Root, StringComparison.OrdinalIgnoreCase); p = p is DirectoryInfo d ? d.Parent : ((FileInfo)p).Directory)
            if (p.Exists && (p.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Linked paths forbidden: " + relative);
        return target;
    }
    public void Validate()
    {
        if (Catalog.SchemaVersion != 1) throw new InvalidDataException("Unsupported catalog schema");
        var map = new Dictionary<string, Requirement>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Catalog.Entries)
        {
            if (!Regex.IsMatch(r.Id, "^REQ-[A-Z0-9-]+$") || !map.TryAdd(r.Id, r)) throw new InvalidDataException("Invalid/duplicate ID: " + r.Id);
            if (!new[] { "Active", "Proposed", "Historical", "Superseded" }.Contains(r.Status)) throw new InvalidDataException("Invalid status: " + r.Id);
            var path = SafePath(r.Path, "Docs/Requirements");
            if (!paths.Add(path)) throw new InvalidDataException("Duplicate document path");
            var text = File.ReadAllText(path).Replace("\r\n", "\n");
            string header = new Regex(@"(?m)^(?:## |<!-- BEGIN MIGRATED CONTRACT -->)").Split(text,2)[0];
            var declaredId = Regex.Matches(header,@"(?m)^Requirement ID: ([^\n]+)$"); var declaredStatus = Regex.Matches(header,@"(?m)^Status: ([^\n]+)$");
            if (declaredId.Count != 1 || declaredStatus.Count != 1 || declaredId[0].Groups[1].Value != r.Id || declaredStatus[0].Groups[1].Value != r.Status) throw new InvalidDataException("Document metadata mismatch: " + r.Id);
            foreach (var source in r.Sources)
            {
                var arc = SafePath(source.GetProperty("archived").GetString()!, "Docs/Archive");
                if (!Hash(File.ReadAllBytes(arc)).Equals(source.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Archive hash mismatch: " + r.Id);
                const string begin = "<!-- BEGIN MIGRATED CONTRACT -->\n", end = "\n<!-- END MIGRATED CONTRACT -->";
                int a = text.IndexOf(begin, StringComparison.Ordinal), b = text.LastIndexOf(end, StringComparison.Ordinal);
                if (a < 0 || b < a) throw new InvalidDataException("Missing migrated body: " + r.Id);
                var payload = text[(a + begin.Length)..b];
                if (!Hash(System.Text.Encoding.UTF8.GetBytes(payload)).Equals(source.GetProperty("payloadSha256").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Migrated body changed: " + r.Id);
                if (Regex.Matches(payload, "^#{1,6} ", RegexOptions.Multiline).Count != source.GetProperty("headings").GetInt32()) throw new InvalidDataException("Section inventory mismatch");
            }
        }
        foreach (var r in Catalog.Entries)
        {
            foreach (var id in r.Related.Concat(r.Supersedes).Concat(r.Amendments.Select(a => a.Target)))
                if (!map.ContainsKey(id) || id == r.Id) throw new InvalidDataException("Missing/self reference: " + r.Id + " -> " + id);
            if (r.Status == "Active" && r.Supersedes.Concat(r.Amendments.Select(a => a.Target)).Any(id => map[id].Status == "Proposed")) throw new InvalidDataException("Active override targets an unaccepted proposal");
            if (r.Amendments.Any(a => string.IsNullOrWhiteSpace(a.Scope))) throw new InvalidDataException("Amendment has no scope");
            if (r.Supersedes.Distinct().Count() != r.Supersedes.Length || r.Amendments.Select(a => a.Target).Distinct().Count() != r.Amendments.Length) throw new InvalidDataException("Duplicate override");
        }
        var visiting = new HashSet<string>(); var visited = new HashSet<string>();
        void Walk(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new InvalidDataException("Override cycle: " + id);
            foreach (var target in map[id].Supersedes.Concat(map[id].Amendments.Select(a => a.Target))) Walk(target);
            visiting.Remove(id); visited.Add(id);
        }
        foreach (var id in map.Keys) Walk(id);
        foreach (var alias in Catalog.LegacyAliases)
            if (!map.ContainsKey(alias.Value)) throw new InvalidDataException("Invalid legacy alias");
    }
    public static string Hash(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    public Requirement Find(string id) => Catalog.Entries.SingleOrDefault(r => r.Id == id || Catalog.LegacyAliases.GetValueOrDefault(id) == r.Id) ?? throw new KeyNotFoundException("Unknown requirement: " + id);
    public bool IsEffective(Requirement r) => Resolve(r.Domain).ReadSet.Any(x => x.Id == r.Id);
    public Projection Resolve(string domain)
    {
        var selected = Catalog.Entries.Where(r => r.Domain == domain || r.Domains.Contains(domain)).ToArray();
        if (selected.Length == 0) throw new KeyNotFoundException("Unknown domain: " + domain);
        // Full replacements hide the whole target; amendments keep every target's remaining clauses.
        var replaced = new HashSet<string>();
        void Replace(string id) { if (replaced.Add(id)) foreach (var old in Find(id).Supersedes) Replace(old); }
        foreach (var r in Catalog.Entries.Where(r => r.Status == "Active")) foreach (var id in r.Supersedes) Replace(id);
        var current = selected.Where(r => r.Status == "Active" && !replaced.Contains(r.Id)).OrderBy(r => r.Priority).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        return new(current, selected.Except(current).ToArray(), current.SelectMany(r => r.Amendments.Select(a => $"{r.Id} -> {a.Target}: {a.Scope}")).ToArray(),
            Catalog.KnownIssues.Where(i => i.GetProperty("requirements").EnumerateArray().Any(x => current.Any(r => r.Id == x.GetString()))).ToArray());
    }
    public string Read(Requirement r) => File.ReadAllText(SafePath(r.Path, "Docs/Requirements"));
    public Requirement CreateProposal(string id, string title, string domain, string body)
    {
        using var writeLock = WorkspaceLock.Acquire(Root);
        EnsureCatalogUnchanged();
        if (!Regex.IsMatch(id, "^REQ-[A-Z0-9-]+$") || Catalog.Entries.Any(r => r.Id == id)) throw new ArgumentException("Use a unique REQ-... ID");
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(body)) throw new ArgumentException("Title, domain and body are required");
        var r = new Requirement { Id = id, Title = title, Domain = domain, Domains = [domain], Path = $"Docs/Requirements/{id}.md" };
        string path = SafePath(r.Path, "Docs/Requirements");
        using (var stream = new FileStream(path, FileMode.CreateNew)) using (var writer = new StreamWriter(stream))
            writer.Write($"# {id} — {title}\n\nRequirement ID: {id}\nStatus: Proposed\nCreated: {DateTimeOffset.Now:yyyy-MM-dd}\n\n{body}\n");
        Catalog.Entries.Add(r);
        try { Validate(); SaveCatalog(); } catch { Catalog.Entries.Remove(r); File.Delete(path); throw; }
        return r;
    }
    private void SaveCatalog()
    {
        var path = SafePath("Docs/Requirements/catalog.json"); var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { File.WriteAllText(temp, JsonSerializer.Serialize(Catalog, Json) + "\n"); EnsureCatalogUnchanged(); File.Move(temp, path, true); catalogFingerprint = Hash(File.ReadAllBytes(path)); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private void EnsureCatalogUnchanged()
    {
        if (Hash(File.ReadAllBytes(SafePath("Docs/Requirements/catalog.json"))) != catalogFingerprint) throw new InvalidOperationException("Requirement catalog changed externally; refresh before creating documents");
    }
    public void AssertCurrent() { EnsureCatalogUnchanged(); Validate(); }
    public string CreatePlan(string id, string reqId, string purpose)
    {
        using var writeLock = WorkspaceLock.Acquire(Root);
        EnsureCatalogUnchanged();
        var req = Find(reqId);
        if (!Regex.IsMatch(id, "^PLAN-[A-Z0-9-]+$") || string.IsNullOrWhiteSpace(purpose)) throw new ArgumentException("Plan ID/purpose invalid");
        string relative = $"Docs/Plans/{id}.md", path = SafePath(relative, "Docs/Plans");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew); using var writer = new StreamWriter(stream);
        writer.Write($"# {id}\n\nPlan ID: {id}\nStatus: Draft\nRequirements: {req.Id}\nCreated: {DateTimeOffset.Now:yyyy-MM-dd}\nRisk: Unclassified\n\n## Purpose\n\n{purpose}\n\n## Progress\n\n- [ ] Resolve authority and repository facts.\n- [ ] Define scope, interfaces and requirement-to-test mapping.\n- [ ] Implement and verify.\n- [ ] Review diff and update evidence.\n\n## Validation and recovery\n\nSpecify focused checks and recovery before activation. Draft does not authorize implementation.\n");
        return relative;
    }
}
