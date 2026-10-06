using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private static async Task<int> ChatFeedbackChecks(string[] args)
    {
        using var fixture = new Fixture();
        Directory.CreateDirectory(Path.Combine(fixture.Root,"Docs/plans/demo"));
        File.WriteAllText(Path.Combine(fixture.Root,"Docs/plans/demo/PLAN-CHAT-001_reference.md"),"# 参考资料测试\n");
        File.WriteAllText(Path.Combine(fixture.Root,"Docs/plans/demo/PLAN-CHAT-001_reference.meta.json"),"""
        {"id":"PLAN-CHAT-001","title":"参考资料测试","type":"plan","status":"验收","domain":"demo","path":"Docs/plans/demo/PLAN-CHAT-001_reference.md",
         "resourceRefs":[{"path":"Docs/plans/demo/PLAN-CHAT-001_reference.md","evidence":"本地资料"},{"repository":"https://github.com/Mosya-CX/Karolina","path":"Karolina.Desktop/Web/settings.js","evidence":"独立源码"}],
         "externalRefs":[{"repository":"https://github.com/Mosya-CX/Karolina","path":"Karolina.Desktop/Web/settings.js","evidence":"相同源码"},{"repository":"E:/Github/Karolina","path":"README.md","evidence":"另一仓库说明"}]}
        """);
        using var library = new DocumentLibrary(fixture.Root);
        await Case("资源反序列化保留所属仓库",()=>{
            var r=JsonSerializer.Deserialize<ResourceReference>("{\"path\":\"README.md\",\"evidence\":\"来源\",\"repository\":\"E:/Github/Karolina\"}",DocumentLibrary.Json)!;
            using var j=JsonDocument.Parse(JsonSerializer.Serialize(r,DocumentLibrary.Json));
            Check(j.RootElement.TryGetProperty("repository",out var repository)&&repository.GetString()=="E:/Github/Karolina");return Task.CompletedTask;
        });
        await Case("默认引用混合本地及外部资料不误判工程越界",()=>{
            string context=library.ReferenceResources(["PLAN-CHAT-001"],null);
            Check(context.Contains("https://github.com/Mosya-CX/Karolina")&&context.Contains("E:/Github/Karolina")&&context.Contains("本地资料"));
            Check(library.Find("PLAN-CHAT-001").ResourceRefs!.Length==3,"外部引用应合并且同仓库同路径去重");
            library.Resources("PLAN-CHAT-001");return Task.CompletedTask;
        });
        await Case("空资源选择仍携带计划标题，未选资料保持为空",()=>{
            Check(library.ReferenceResources(["PLAN-CHAT-001"],new(){["PLAN-CHAT-001"]=[]})=="");
            Check(library.ReferenceContext(["PLAN-CHAT-001"]).Contains("参考资料测试"));Throws(()=>library.ReferenceResources([],new(){["PLAN-CHAT-001"]=[]}));return Task.CompletedTask;
        });
        await Case("本地路径越界保护继续生效",()=>{
            foreach(var path in new[]{"../README.md","C:/Windows/file","Assets/../secret","Assets/file.meta"})Throws(()=>library.ResourcePath(path));return Task.CompletedTask;
        });
        await Case("逐项资源选择保留仓库身份且不接受未选资源",()=>{
            var resources=library.Find("PLAN-CHAT-001").ResourceRefs!;var external=resources.Single(r=>r.Repository=="E:/Github/Karolina");
            string selected=library.ReferenceResources(["PLAN-CHAT-001"],new(){["PLAN-CHAT-001"]=[external.SelectionKey]});
            Check(selected.Contains("README.md")&&!selected.Contains("settings.js"));
            Throws(()=>library.ReferenceResources(["PLAN-CHAT-001"],new(){["PLAN-CHAT-001"]=["not-owned"]}));return Task.CompletedTask;
        });
        await Case("进度描述不包含CLI且不把命令成功当验收",()=>{
            foreach(string command in new[]{"rg --files SECRET_CLI","Get-Content SECRET_CLI","dotnet test SECRET_CLI","dotnet build SECRET_CLI","python SECRET_CLI"}){
                var activity=AgentActivity.CommandPurpose(command);Check(!activity.Message.Contains("SECRET_CLI")&&!activity.Message.Contains("通过"));}
            Check(AgentActivity.CommandPurpose("rg -n test build Docs").Stage=="查阅资料");return Task.CompletedTask;
        });
        await Case("外层工具失败不能被内层通过摘要覆盖",()=>{
            var passedResult=JsonSerializer.SerializeToElement(new{Summary=new{Status="Passed",TotalTests=1,PassedTests=1,FailedTests=0,SkippedTests=0},Results=new[]{new{Name="pass",Status="Passed"}}});
            foreach(string status in new[]{"failed","declined","interrupted","unknown"})Throws(()=>AgentActivity.TestVerdict(JsonSerializer.SerializeToElement(new{status,result=passedResult})));
            Check(AgentActivity.TestVerdict(JsonSerializer.SerializeToElement(new{status="completed",result=passedResult})).Contains("1/1"));return Task.CompletedTask;
        });
        await Case("审批关联兼容不同仓库同名资源",()=>{
            string metadataPath=Path.Combine(fixture.Root,library.Find("PLAN-CHAT-001").MetadataPath);var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(metadataPath))!;
            node["resourceRefs"]!.AsArray().Add(JsonSerializer.SerializeToNode(new ResourceReference("Karolina.Desktop/Web/settings.js","另一仓库",Repository:"E:/OtherRepository"),DocumentLibrary.Json));
            File.WriteAllText(metadataPath,node.ToJsonString());library.List();library.AssociateReview(new TaskReview{PlanId="PLAN-CHAT-001"});
            Check(library.Find("PLAN-CHAT-001").ResourceRefs!.Count(r=>r.Path=="Karolina.Desktop/Web/settings.js")==2);return Task.CompletedTask;
        });
        int at=Array.IndexOf(args,"--reference-project");
        await Case("公开摘要忽略空条目及私有推理",()=>{
            Check(AgentActivity.PublicSummary(JsonSerializer.SerializeToElement(new{summary=new object[]{"公开判断",new{text="下一步"},null!},content=new[]{"PRIVATE_MARKER"}}))=="公开判断\n下一步");
            Check(AgentActivity.PublicSummary(JsonSerializer.SerializeToElement(new{summary=Array.Empty<string>(),content=new[]{"PRIVATE_MARKER"}}))=="");return Task.CompletedTask;
        });
        if(at>=0)await Case("用户实际计划默认关联资料可正常解析",()=>{
            using var actual=new DocumentLibrary(args[at+1]);var plan=actual.Find("PLAN-KAR-011");
            string context=actual.ReferenceResources([plan.Id],null);Check(context.Contains("Karolina.Desktop/Web/settings.js")&&context.Contains("github.com/Mosya-CX/Karolina"));actual.Resources(plan.Id);return Task.CompletedTask;
        });
        Console.WriteLine($"Chat feedback checks: {passed} passed, {failed} failed");return failed==0?0:1;
    }
}
