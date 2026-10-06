using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private static async Task<int> GraphSettingsChecks()
    {
        using var fixture = new Fixture();
        string assets = Path.Combine(fixture.Root, "Assets");
        string scripts = Path.Combine(assets, "Scripts");
        string prefabs = Path.Combine(assets, "Prefabs");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(prefabs);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "ProjectSettings"));

        await Case("测试回执只依据真实逐项终态判断", () =>
        {
            using var raw = JsonDocument.Parse("{\"Summary\":{\"Status\":\"Passed\",\"TotalTests\":1,\"PassedTests\":1,\"FailedTests\":0,\"SkippedTests\":0},\"Results\":[{\"Name\":\"fixture.pass\",\"Status\":\"Passed\"}]}");
            Check(McpClient.TestVerdict(raw.RootElement).Contains("1/1 executed"));
            using var failed = JsonDocument.Parse("{\"structuredContent\":{\"result\":{\"Summary\":{\"Status\":\"Failed\",\"TotalTests\":1,\"PassedTests\":0,\"FailedTests\":1,\"SkippedTests\":0},\"Results\":[{\"Name\":\"fixture.fail\",\"Status\":\"Failed\"}]}}}");
            Throws(() => McpClient.TestVerdict(failed.RootElement));
            return Task.CompletedTask;
        });

        const string baseGuid = "11111111111111111111111111111111";
        const string wolfGuid = "22222222222222222222222222222222";
        const string prefabGuid = "33333333333333333333333333333333";
        const string binaryGuid = "44444444444444444444444444444444";
        const string missingGuid = "55555555555555555555555555555555";
        string basePath = Path.Combine(scripts, "BaseController.cs");
        string wolfPath = Path.Combine(scripts, "WolfController.cs");
        string blocksPath = Path.Combine(scripts, "MultipleNamespaces.cs");
        string recordPath = Path.Combine(scripts, "Position.cs");
        string prefabPath = Path.Combine(prefabs, "Wolf.prefab");
        File.WriteAllText(basePath, "namespace Demo; public class BaseController { }");
        File.WriteAllText(basePath + ".meta", $"fileFormatVersion: 2\nguid: {baseGuid}\n");
        File.WriteAllText(wolfPath, "namespace Demo; public class WolfController : BaseController { string fake = \"PhantomClass\"; } // GhostCommentClass\n");
        File.WriteAllText(wolfPath + ".meta", $"fileFormatVersion: 2\nguid: {wolfGuid}\n");
        File.WriteAllText(blocksPath, "namespace First { public class FirstType { } } namespace Second { public class SecondType { } }");
        File.WriteAllText(recordPath, "namespace Geometry; public record struct Position(int X, int Y);");
        File.WriteAllText(prefabPath, $"--- !u!114 &1\nMonoBehaviour:\n  m_Script: {{fileID: 11500000, guid: {wolfGuid}, type: 3}}\n");
        File.WriteAllText(prefabPath + ".meta", $"fileFormatVersion: 2\nguid: {prefabGuid}\n");
        File.WriteAllBytes(Path.Combine(prefabs, "Binary.prefab"), [0, 255, 1]);
        File.WriteAllText(Path.Combine(prefabs, "Binary.prefab.meta"), $"fileFormatVersion: 2\nguid: {binaryGuid}\n");
        string missingPrefab = Path.Combine(prefabs, "MissingReference.prefab");
        File.WriteAllText(missingPrefab, $"--- !u!114 &2\nMonoBehaviour:\n  m_Script: {{fileID: 11500000, guid: {missingGuid}, type: 3}}\n");

        var project = new ProjectContext(fixture.Root);
        using var graph = new ProjectGraphService(project);
        await Case("图谱索引只读工程、建立C#类型和Unity GUID关系", async () =>
        {
            Check(!graph.Status().Indexed);
            graph.StartIndex();
            ProjectGraphStatus status = await WaitForIndex(graph);
            Check(status.Indexed && status.Error is null);
            Check(status.SkippedFiles >= 1);
            Check(status.UnresolvedGuids == 1);
            var search = graph.Search("Wolf", null);
            Check(search.Nodes.Any(node => node.Kind == "代码文件" && node.Path == "Assets/Scripts/WolfController.cs"));
            var wolfType = search.Nodes.Single(node => node.Kind == "C# 类型");
            Check(wolfType.Title == "Demo.WolfController");
            var typeDetail = graph.Node(wolfType.Id);
            Check(typeDetail.Incoming.Any(edge => edge.Relation == "声明"));
            var baseType = graph.Search("Demo.BaseController", "C# 类型").Nodes.Single();
            Check(graph.Node(baseType.Id).Incoming.Any(edge => edge.Relation == "类型引用"));
            Check(graph.Search("First.FirstType", "C# 类型").Nodes.Length == 1);
            Check(graph.Search("Second.SecondType", "C# 类型").Nodes.Length == 1);
            Check(graph.Search("Geometry.Position", "C# 类型").Nodes.Length == 1);
            var prefab = graph.Search("Wolf.prefab", "Unity资源").Nodes.Single();
            string wolfFileId = search.Nodes.Single(node => node.Kind == "代码文件" && node.Path == "Assets/Scripts/WolfController.cs").Id;
            Check(graph.Node(prefab.Id).Outgoing.Any(edge => edge.Relation == "Unity GUID 引用" && edge.To == wolfFileId));
            Check(!graph.Search("PhantomClass GhostCommentClass", null).Nodes.Any(node => node.Kind == "C# 类型"));
            string context = graph.ReferenceContext([wolfType.Id]);
            Check(context.Contains("Demo.WolfController") && !context.Contains("PhantomClass"));
            Check(File.Exists(project.StatePath("project-graph.json")));
            Check(File.ReadAllText(wolfPath).Contains("PhantomClass"));
        });

        await GraphQueryChecks(graph, project, wolfPath);

        await Case("设置按工程原子保存、验证范围并保留损坏文件", () =>
        {
            var store = new WorkbenchSettingsStore(project);
            Check(store.Read().MaxVerificationRepairRounds == 1);
            var settings = new WorkbenchSettings
            {
                AutoVerification = false,
                MaxVerificationRepairRounds = 0,
                ModelProfiles = new(StringComparer.Ordinal)
                {
                    ["execute-task"] = new("small-model", "low")
                }
            };
            store.Save(settings);
            Check(store.Read().ModelProfiles["execute-task"].Model == "small-model");
            string settingsPath = project.StatePath("settings.json");
            string saved = File.ReadAllText(settingsPath);
            Check(!saved.Contains("apiKey", StringComparison.OrdinalIgnoreCase) && !saved.Contains("token", StringComparison.OrdinalIgnoreCase));
            Throws(() => WorkbenchSettingsStore.Validate(new WorkbenchSettings { MaxVerificationRepairRounds = 4 }));
            Throws(() => WorkbenchSettingsStore.Validate(new WorkbenchSettings { ModelProfiles = new() { ["unknown-mode"] = new("m", "low") } }));
            File.WriteAllText(settingsPath, "{invalid");
            Throws(() => store.Read());
            Check(File.ReadAllText(settingsPath) == "{invalid");
            Throws(() => store.Save(settings));
            var recovery = store.ResetCorruptWithBackup();
            Check(recovery.BackupFile is not null);
            Check(File.ReadAllText(Path.Combine(project.StateDirectory, recovery.BackupFile!)) == "{invalid");
            Check(store.Read().MaxVerificationRepairRounds == 1);
            return Task.CompletedTask;
        });

        Console.WriteLine($"Graph/settings checks: {passed} passed, {failed} failed; isolated fixture only.");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> GraphProjectCheck(string root)
    {
        var project = new ProjectContext(root, requireUnity: true);
        using var graph = new ProjectGraphService(project);
        Console.WriteLine($"Starting read-only index: {project.Root}");
        graph.StartIndex();
        var status = await WaitForIndex(graph, 12_000, true);
        Console.WriteLine(JsonSerializer.Serialize(status, DocumentLibrary.Json));
        return status.Indexed && status.Error is null ? 0 : 1;
    }

    private static async Task<ProjectGraphStatus> WaitForIndex(ProjectGraphService graph, int maxAttempts = 500, bool reportProgress = false)
    {
        for (int attempt = 0; attempt < maxAttempts; attempt++)
        {
            var status = graph.Status();
            if (!status.Indexing) return status;
            if (reportProgress && attempt % 50 == 0) Console.WriteLine($"{status.Stage} · 已扫描 {status.FilesScanned:N0} 个文件，跳过 {status.SkippedFiles:N0}");
            await Task.Delay(20);
        }
        throw new TimeoutException($"工程图谱索引未在{maxAttempts * 20 / 1000}秒内结束");
    }
}
