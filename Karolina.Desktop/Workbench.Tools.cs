using System.Text.Json;
using Karolina.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    private ExtensionToolCatalog toolCatalog=null!;
    private ExtensionToolService toolService=null!;
    private CancellationTokenSource? toolCancellation;
    private ExtensionToolRun? runningToolRecord;
    private bool BuildPending => File.Exists(project.StatePath("unity-build-pending.json"));
    private void MapToolRoutes()
    {
        var server=app??throw new InvalidOperationException("工作台服务尚未创建");
        toolCatalog=new(project,library);
        toolService=new(project,toolCatalog,[new ExternalToolRunner(project),new UnityToolRunner(project,()=>unity,tool=> {
            if(tool.UnityBuild)File.WriteAllText(project.StatePath("unity-build-pending.json"),JsonSerializer.Serialize(new {tool=tool.Id,run=runningToolRecord?.Id??throw new InvalidOperationException("缺少工具运行记录"),at=DateTimeOffset.Now},DocumentLibrary.Json));
        })]);
        server.MapGet("/tools.js",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web/tools.js"),"text/javascript"));
        server.MapGet("/workspace.js",()=>Results.File(Path.Combine(AppContext.BaseDirectory,"Web/workspace.js"),"text/javascript"));
        server.MapGet("/api/tools",()=>toolCatalog.List());
        server.MapGet("/api/tools/runs",()=>toolService.ListRuns());
        server.MapGet("/api/tools/unity",()=>unity?.Tools??[]);
        server.MapGet("/api/tools/build",()=>new { pending=BuildPending });
        server.MapPost("/api/tools/register",(ToolRegisterInput input)=>toolCatalog.Register(input.Definition,input.Manual));
        server.MapPost("/api/tools/run",RunExtensionTool);
        server.MapPost("/api/tools/stop",()=>{toolCancellation?.Cancel();return new { requested=toolCancellation!=null };});
        server.MapPost("/api/tools/build-finished",()=>{if(busy||toolCancellation!=null)throw new InvalidOperationException("请等待当前调用结束");File.Delete(project.StatePath("unity-build-pending.json"));return new { pending=false };});
    }
    private async Task<IResult> RunExtensionTool(ToolRunInput input)
    {
        if(!await gate.WaitAsync(0))throw new InvalidOperationException("当前工程操作正在处理，请稍后重试；工具不会在后台排队重复执行");
        bool unityHeld=false;
        try
        {
            if(busy||toolCancellation!=null)throw new InvalidOperationException("当前工程还有运行中的操作");
            var descriptor=toolCatalog.Resolve(input.Id,input.OperationId);var definition=descriptor.Definition;
            if(descriptor.Hash!=input.Hash)throw new InvalidOperationException("工具声明已更新，请重新选择工具");
            if(definition.Kind=="manual")throw new ArgumentException("此项只有指南，请先注册执行入口");
            toolService.ValidateExecution(descriptor,input.Arguments);
            if(definition.Kind=="unity-mcp"&&BuildPending)throw new InvalidOperationException("构建请求已经发送；请先确认Unity构建结束，期间禁止继续Unity操作");
            using var guard=WorkspaceLock.Acquire(root);
            var record=new ExtensionToolRun { ToolId=input.Id,OperationId=descriptor.OperationId };
            toolCancellation=CancellationTokenSource.CreateLinkedTokenSource(app!.Lifetime.ApplicationStopping);
            runningToolRecord=record;
            toolService.Save(record);
            try
            {
                if(definition.Kind=="unity-mcp")
                {
                    unityHeld=await unityGate.WaitAsync(0,toolCancellation.Token);
                    if(!unityHeld)throw new InvalidOperationException("Unity操作正在执行，请稍后重试");
                    if(BuildPending)throw new InvalidOperationException("请先确认Unity构建结束");
                    if(unity==null||!unity.Connected||unityState!="已连接")throw new InvalidOperationException("请在AI对话页连接并核对Unity");
                    unity.ValidateTool(definition.ToolName!);
                    using var preflight=CancellationTokenSource.CreateLinkedTokenSource(toolCancellation.Token);preflight.CancelAfter(TimeSpan.FromSeconds(12));
                    await unity.VerifyProject(root,preflight.Token);
                }
                await toolService.Execute(record,descriptor,input.Arguments,toolCancellation.Token);
            }
            catch(Exception e){record.State=e is OperationCanceledException?"已取消或超时":"失败";record.Output=e.Message;record.Ended=DateTimeOffset.Now;toolService.Save(record);}
            return Results.Json(record);
        }
        finally{if(unityHeld)unityGate.Release();runningToolRecord=null;toolCancellation?.Dispose();toolCancellation=null;gate.Release();}
    }
}
