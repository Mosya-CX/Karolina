using System.Text.Json;
using System.Text.RegularExpressions;

namespace Karolina.Core;

public sealed class ExtensionTool
{
    public string Id { get; set; }="";
    public string Title { get; set; }="";
    public string Description { get; set; }="";
    public string Kind { get; set; }="manual";
    public string GuideId { get; set; }="";
    public string? ToolName { get; set; }
    public string? Executable { get; set; }
    public string[] Arguments { get; set; }=[];
    public JsonElement DefaultArguments { get; set; }=JsonSerializer.SerializeToElement(new {});
    public bool ReadOnly { get; set; }
    public bool UnityBuild { get; set; }
    public string? MenuPath { get; set; }
    public string LaunchMode { get; set; } = "wait";
    public int TimeoutSeconds { get; set; }=60;
    public ExtensionTool[] Operations { get; set; }=[];
}
public sealed record ToolDescriptor(ExtensionTool Definition,string Hash,string? GroupId=null,string? OperationId=null);
public sealed class ExtensionToolCatalog(ProjectContext project, DocumentLibrary documents)
{
    private string DirectoryPath => documents.SafePath("Docs/tools/registry");
    public ToolDescriptor[] List() => Directory.Exists(DirectoryPath)?Directory.EnumerateFiles(DirectoryPath,"*.tool.json").Order(StringComparer.Ordinal).Select(Read).ToArray():[];
    private ToolDescriptor Read(string path)
    {
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("工具声明不能是链接");
        byte[] bytes=File.ReadAllBytes(path);var tool=JsonSerializer.Deserialize<ExtensionTool>(bytes,DocumentLibrary.Json)??throw new InvalidDataException("工具声明损坏");Validate(tool);
        if(Path.GetFileName(path)!=tool.Id+".tool.json")throw new InvalidDataException("工具编号与声明文件不一致");
        if(!string.IsNullOrWhiteSpace(tool.GuideId))documents.Find(tool.GuideId);
        return new(tool,RequirementStore.Hash(bytes));
    }
    public ToolDescriptor Find(string id) => List().SingleOrDefault(t=>t.Definition.Id==id)??throw new KeyNotFoundException("拓展工具不存在");
    public ToolDescriptor Resolve(string id, string? operationId)
    {
        var parent=Find(id);
        if(parent.Definition.Kind!="group") { if(operationId!=null && operationId!=id)throw new ArgumentException("工具没有此调用函数");return parent; }
        var operation=parent.Definition.Operations.SingleOrDefault(o=>o.Id==operationId)??throw new ArgumentException("请选择此工具内的调用函数");
        if(string.IsNullOrWhiteSpace(operation.GuideId))operation.GuideId=parent.Definition.GuideId;
        return new(operation,parent.Hash,parent.Definition.Id,operation.Id);
    }
    public ToolDescriptor Register(ExtensionTool tool,string manual)
    {
        Validate(tool);if(string.IsNullOrWhiteSpace(manual)||manual.Length>100000)throw new ArgumentException("注册工具需要配套使用说明");
        using var guard=WorkspaceLock.Acquire(project.Root);
        string guideId="TOOL-GUIDE-"+tool.Id; if(documents.List().Any(d=>d.Id==guideId)||List().Any(t=>t.Definition.Id==tool.Id))throw new ArgumentException("工具编号已存在；可编辑现有声明文件");
        string guidePath=$"Docs/tools/guides/{guideId}_usage.md";
        Directory.CreateDirectory(DirectoryPath);Directory.CreateDirectory(Path.GetDirectoryName(documents.SafePath(guidePath))!);
        tool.GuideId=guideId;
        string metadata=guidePath[..^3]+".meta.json",declaration="Docs/tools/registry/"+tool.Id+".tool.json",catalogPath=documents.SafePath("Docs/catalog.json");
        var writes=new Dictionary<string,string> {
            [guidePath]="# "+tool.Title+"使用说明\n\n"+manual.Trim()+"\n",
            [metadata]=JsonSerializer.Serialize(new{id=guideId,title=tool.Title+"使用说明",type="resource",domain="拓展工具",status="当前资料",path=guidePath,version=1,tags=new[]{"拓展工具"}},DocumentLibrary.Json),
            [declaration]=JsonSerializer.Serialize(tool,DocumentLibrary.Json)
        };
        foreach(string relative in writes.Keys)if(File.Exists(documents.SafePath(relative))||File.Exists(documents.SafePath(relative+".register.tmp")))throw new IOException("工具文件已经存在，保留原文件："+relative);
        byte[] oldCatalog=File.ReadAllBytes(catalogPath);
        var catalog=System.Text.Json.Nodes.JsonNode.Parse(oldCatalog)!.AsObject();
        if(catalog["schemaVersion"]?.GetValue<int>()!=2||catalog["entries"] is not System.Text.Json.Nodes.JsonArray entries)throw new InvalidDataException("资料索引不是版本2");
        if(entries.Any(e=>e?["id"]?.GetValue<string>()==guideId))throw new InvalidDataException("指南编号已在索引内");
        entries.Add(JsonSerializer.SerializeToNode(new{id=guideId,path=guidePath,metadata},DocumentLibrary.Json));
        string catalogText=catalog.ToJsonString(DocumentLibrary.Json),catalogHash=RequirementStore.Hash(System.Text.Encoding.UTF8.GetBytes(catalogText));
        string catalogTemp=documents.SafePath("Docs/catalog.json.register.tmp");var owned=new Dictionary<string,string>();bool catalogWritten=false;
        void WriteNew(string path,string text){using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);owned.Add(path,RequirementStore.Hash(System.Text.Encoding.UTF8.GetBytes(text)));using var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false));writer.Write(text);}
        try
        {
            foreach(var (relative,text) in writes)
            {string target=documents.SafePath(relative),temp=documents.SafePath(relative+".register.tmp");WriteNew(temp,text);File.Move(temp,target,false);string hash=owned[temp];owned.Remove(temp);owned.Add(target,hash);}
            WriteNew(catalogTemp,catalogText);
            if(!oldCatalog.AsSpan().SequenceEqual(File.ReadAllBytes(catalogPath)))throw new IOException("资料索引被外部修改，请重新注册");
            File.Move(catalogTemp,catalogPath,true);owned.Remove(catalogTemp);catalogWritten=true;
            documents.List();return Find(tool.Id);
        }
        catch(Exception failure)
        {
            var conflicts=new List<string>();
            string expectedCatalogHash=catalogWritten?catalogHash:RequirementStore.Hash(oldCatalog);
            if(!File.Exists(catalogPath)||RequirementStore.Hash(File.ReadAllBytes(documents.SafePath("Docs/catalog.json")))!=expectedCatalogHash)conflicts.Add("Docs/catalog.json");
            foreach(var (file,hash) in owned)
            {
                string safe=documents.SafePath(Path.GetRelativePath(project.Root,file));
                if(!File.Exists(safe)||RequirementStore.Hash(File.ReadAllBytes(safe))!=hash)conflicts.Add(Path.GetRelativePath(project.Root,safe));
            }
            // catalog、指南和声明相互关联：任一冲突保留整组最终文件，不能留下断链入口。
            if(conflicts.Count>0)
            {
                foreach(var (file,hash) in owned.Where(e=>e.Key.EndsWith(".tmp",StringComparison.Ordinal)))
                {string safe=documents.SafePath(Path.GetRelativePath(project.Root,file));if(File.Exists(safe)&&RequirementStore.Hash(File.ReadAllBytes(safe))==hash)File.Delete(safe);}
                throw new IOException("注册失败；关联文件组整体保留，需人工核对冲突："+string.Join("、",conflicts),failure);
            }
            if(catalogWritten)
            {
                string rollback=documents.SafePath("Docs/catalog.json.rollback-"+Guid.NewGuid().ToString("N")+".tmp");
                using(var stream=new FileStream(rollback,FileMode.CreateNew,FileAccess.Write,FileShare.None))stream.Write(oldCatalog);
                File.Move(rollback,catalogPath,true);
            }
            foreach(string file in owned.Keys)File.Delete(documents.SafePath(Path.GetRelativePath(project.Root,file)));
            throw;
        }
    }
    private static void Validate(ExtensionTool tool)
    {
        if(!Regex.IsMatch(tool.Id,"^[a-z][a-z0-9-]{0,69}$")||string.IsNullOrWhiteSpace(tool.Title)||tool.Title.Length>100||tool.TimeoutSeconds is <1 or >3600)throw new ArgumentException("工具需有效英文编号、标题与1–3600秒超时");
        if(tool.Kind is not ("manual" or "unity-mcp" or "external" or "group"))throw new ArgumentException("工具类型应为manual、unity-mcp、external或group");
        if(tool.DefaultArguments.ValueKind!=JsonValueKind.Object)throw new ArgumentException("默认参数必须为JSON对象");
        if(tool.Kind=="group")
        {
            if(tool.Operations.Length is <1 or >64 || tool.Operations.Select(o=>o.Id).Distinct(StringComparer.Ordinal).Count()!=tool.Operations.Length)throw new ArgumentException("工具集合需要1–64个不同编号的调用函数");
            if(tool.ToolName!=null||tool.Executable!=null||tool.MenuPath!=null||tool.UnityBuild)throw new ArgumentException("集合只组织函数，执行入口必须声明在具体函数中");
            foreach(var operation in tool.Operations) { if(operation.Kind=="group"||operation.Operations.Length!=0)throw new ArgumentException("调用函数不能嵌套集合");Validate(operation); }
            return;
        }
        if(tool.Operations.Length!=0)throw new ArgumentException("多函数工具需使用group类型");
        if(tool.Kind=="external"&&(string.IsNullOrWhiteSpace(tool.Executable)||tool.Arguments.Length>100))throw new ArgumentException("外部工具需可执行程序与参数列表");
        if(tool.Kind=="unity-mcp"&&string.IsNullOrWhiteSpace(tool.ToolName))throw new ArgumentException("Unity工具需明确MCP名称");
        if(tool.UnityBuild&&tool.Kind!="unity-mcp")throw new ArgumentException("Unity构建标记仅支持Unity MCP工具");
        if(tool.UnityBuild&&tool.ReadOnly)throw new ArgumentException("Unity构建不是只读操作");
        if (tool.LaunchMode is not ("wait" or "gui") || tool.LaunchMode == "gui" && tool.Kind != "external") throw new ArgumentException("GUI启动仅适用于外部程序");
        if (tool.MenuPath != null && (tool.Kind != "unity-mcp" || tool.ToolName != "script-execute" || !tool.MenuPath.StartsWith("FrameSyncMoba/Build Local NGO/", StringComparison.Ordinal) || tool.MenuPath.Length > 200)) throw new ArgumentException("菜单调用需对应项目构建菜单与script-execute");
        if (tool.MenuPath != null)
        {
            string command = tool.MenuPath["FrameSyncMoba/Build Local NGO/".Length..];
            bool build = command is "Build Server" or "Build Client" or "Build Both" or "Build Server Linux (UOS)" or "Build Client Windows (UOS)" or "Build Client + Server (UOS, Once)";
            if (!build && command != "Build Release Client (Optional CDN Package)...") throw new ArgumentException("未核对的菜单不能使用固定构建入口；请注册普通带任务的Unity工具");
            if (tool.UnityBuild != build) throw new ArgumentException("构建菜单必须启用构建暂停标记；参数窗口只打开窗口");
        }
    }
}
public interface IExtensionToolRunner
{
    string Kind { get; }
    Task<string> Run(ExtensionTool tool,JsonElement arguments,CancellationToken ct);
}
public sealed class ExternalToolRunner(ProjectContext project) : IExtensionToolRunner
{
    public string Kind=>"external";
    public static string[] ExpandArguments(ExtensionTool tool,JsonElement arguments)=>tool.Arguments.Select(arg=>Regex.Replace(arg,@"\{([A-Za-z][A-Za-z0-9_]*)\}",m=>arguments.TryGetProperty(m.Groups[1].Value,out var value)?value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText():throw new ArgumentException("缺少参数："+m.Groups[1].Value))).ToArray();
    public async Task<string> Run(ExtensionTool tool,JsonElement arguments,CancellationToken ct)
    {
        string executable=tool.Executable!;
        if(executable.Contains('/')||executable.Contains('\\')) executable=Path.GetFullPath(Path.Combine(project.Root,executable));
        string[] args=ExpandArguments(tool,arguments);
        if (tool.LaunchMode == "gui")
        {
            if (!Path.IsPathRooted(executable) || !File.Exists(executable)) throw new FileNotFoundException("GUI程序尚未生成，请按配套说明准备：" + executable);
            ct.ThrowIfCancellationRequested();
            var start = new System.Diagnostics.ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("GUI进程未启动");
            return $"已启动GUI，PID={process.Id}。此回执只证明进程创建，后续操作和关闭由用户控制。";
        }
        var result=await ProcessRunner.RunAsync(executable,args,project.Root,ct:ct,ownDescendants:true);
        if(result.ExitCode!=0)throw new InvalidOperationException($"外部工具退出码 {result.ExitCode}\n{result.Stderr}\n{result.Stdout}");
        return result.Stdout+(string.IsNullOrWhiteSpace(result.Stderr)?"":"\n标准错误：\n"+result.Stderr);
    }
}
public interface IUnityToolClient
{
    bool Connected { get; }
    void ValidateTool(string name);
    Task<JsonElement> Call(string name,object arguments,CancellationToken ct=default);
    Task VerifyProject(string root,CancellationToken ct=default);
}
public sealed class UnityToolRunner(ProjectContext project,Func<IUnityToolClient?> connection,Action<ExtensionTool>? beforeCall=null) : IExtensionToolRunner
{
    public string Kind=>"unity-mcp";
    public async Task<string> Run(ExtensionTool tool,JsonElement arguments,CancellationToken ct)
    {
        var client=connection(); if(client==null||!client.Connected)throw new InvalidOperationException("请在AI对话页连接并核对Unity");
        client.ValidateTool(tool.ToolName!);
        await client.VerifyProject(project.Root,ct);
        ct.ThrowIfCancellationRequested();beforeCall?.Invoke(tool);
        object actualArguments = tool.MenuPath == null ? arguments : new {
            csharpCode = "public static class KarolinaMenuInvocation { public static void Run() { if (!UnityEditor.EditorApplication.ExecuteMenuItem(" + JsonSerializer.Serialize(tool.MenuPath) + ")) throw new System.InvalidOperationException(\"Unity菜单不存在或未执行\"); } }",
            className = "KarolinaMenuInvocation", methodName = "Run", isMethodBody = false
        };
        var response = await client.Call(tool.ToolName!,actualArguments,ct);
        if (response.TryGetProperty("isError", out var error) && error.ValueKind == JsonValueKind.True) throw new InvalidOperationException("Unity工具返回错误：" + response.GetRawText());
        return response.GetRawText();
    }
}
public sealed class ExtensionToolRun
{
    public string Id { get; set; }=Guid.NewGuid().ToString("N");
    public string ToolId { get; set; }="";
    public string? OperationId { get; set; }
    public string State { get; set; }="执行";
    public string Output { get; set; }="";
    public string? ReviewId { get; set; }
    public DateTimeOffset Started { get; set; }=DateTimeOffset.Now;
    public DateTimeOffset? Ended { get; set; }
}
public sealed class ExtensionToolService(ProjectContext project,ExtensionToolCatalog catalog,IEnumerable<IExtensionToolRunner> runners)
{
    private readonly Dictionary<string,IExtensionToolRunner> adapters=runners.ToDictionary(r=>r.Kind,StringComparer.Ordinal);
    public ExtensionToolRun[] ListRuns()
    {
        string folder=project.StatePath("tool-runs");
        return Directory.Exists(folder)?Directory.EnumerateFiles(folder,"*.json").Select(p=>JsonSerializer.Deserialize<ExtensionToolRun>(File.ReadAllText(project.StatePath("tool-runs/"+Path.GetFileName(p))),DocumentLibrary.Json)??throw new InvalidDataException("工具运行记录损坏")).OrderByDescending(r=>r.Started).Take(30).ToArray():[];
    }
    public void Save(ExtensionToolRun run)
    {
        if(!Regex.IsMatch(run.Id,"^[a-f0-9]{32}$"))throw new ArgumentException("工具运行编号无效");
        string folder=project.StatePath("tool-runs");Directory.CreateDirectory(folder);
        string path=project.StatePath("tool-runs/"+run.Id+".json"),temp=project.StatePath("tool-runs/"+run.Id+".json.tmp");File.WriteAllText(temp,JsonSerializer.Serialize(run,DocumentLibrary.Json));File.Move(temp,path,true);
    }
    public void ValidateExecution(ToolDescriptor descriptor,JsonElement arguments)
    {
        var fresh=catalog.Resolve(descriptor.GroupId??descriptor.Definition.Id,descriptor.OperationId);if(fresh.Hash!=descriptor.Hash)throw new InvalidOperationException("工具声明已变，请重新载入");
        if(arguments.ValueKind!=JsonValueKind.Object||arguments.GetRawText().Length>100000)throw new ArgumentException("工具参数必须为不超过十万字的JSON对象");
        if(!adapters.ContainsKey(fresh.Definition.Kind))throw new InvalidOperationException("此项只有说明，尚未配置执行入口");
        if(fresh.Definition.Kind=="external")ExternalToolRunner.ExpandArguments(fresh.Definition,arguments);
        if (fresh.Definition.MenuPath != null && arguments.EnumerateObject().Any()) throw new ArgumentException("固定构建菜单不接收脚本或覆盖参数，请在Unity参数窗口配置发布内容");
    }
    public async Task Execute(ExtensionToolRun run,ToolDescriptor descriptor,JsonElement arguments,CancellationToken ct)
    {
        try
        {
            ValidateExecution(descriptor,arguments);Save(run);
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(descriptor.Definition.TimeoutSeconds));
            run.Output=await adapters[descriptor.Definition.Kind].Run(descriptor.Definition,arguments,deadline.Token);run.State=descriptor.Definition.LaunchMode == "gui" ? "已启动" : descriptor.Definition.UnityBuild ? "请求已返回，待确认构建结果" : "完成";
        }
        catch(Exception e){run.Output=e.Message;run.State=e is OperationCanceledException?"已取消或超时":"失败";}
        finally{run.Ended=DateTimeOffset.Now;Save(run);}
    }
}
