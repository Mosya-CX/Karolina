using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private static async Task<int> FunctionPolishChecks()
    {
        using var fixture = new Fixture(); string root = fixture.Root;
        Directory.CreateDirectory(Path.Combine(root,"Assets")); Directory.CreateDirectory(Path.Combine(root,"ProjectSettings"));
        using var library = new DocumentLibrary(root); using var reviews = new TaskReviewStore(root);
        var req = library.Create("requirement","资料参考","测试",englishTitle:"reference");
        var plan = library.Create("plan","资料实施","测试",[req.Id],englishTitle:"reference-plan");
        var rule = library.Create("rule","工程规则","测试",englishTitle:"test-rule");
        string MetadataHash(string id) => JsonSerializer.SerializeToElement(library.MetadataSnapshot(id),DocumentLibrary.Json).GetProperty("hash").GetString()!;
        void Index(string id, string[] tags, PlannedChange[]? changes = null, ResourceReference[]? resources = null) => library.UpdateIndex(id,MetadataHash(id),tags,changes,resources);
        string code = Path.Combine(root,"Assets/unit.cs"), human = Path.Combine(root,"Assets/supplied.anim"), meta = code+".meta";
        File.WriteAllText(code,"class Unit {}\n"); File.WriteAllText(human,"before animation\n"); File.WriteAllText(meta,"guid: test\n");
        await Case("所有文档标签去空去重、最多10个且规则可更新",()=>{
            Index(rule.Id,["规则","  测试 ","测试",""]); Check(library.Metadata(rule.Id).GetProperty("tags").GetArrayLength()==2);
            Throws(()=>Index(req.Id,Enumerable.Range(0,11).Select(i=>"tag"+i).ToArray())); return Task.CompletedTask;
        });
        await Case("计划进入执行前要求预估，预估与真实资源分离",()=>{
            Throws(()=>library.UpdateDetails(plan.Id,MetadataHash(plan.Id),"执行",null,null));
            Index(plan.Id,["测试"],[new("Assets/unit.cs","modify","修改单位逻辑")],[new("Assets/unit.cs","源码精确文件")]);
            library.UpdateDetails(plan.Id,MetadataHash(plan.Id),"执行",null,null);
            Check(library.Find(plan.Id).Status=="执行" && library.Find(plan.Id).ResourceRefs!.Single().Hash!=null); return Task.CompletedTask;
        });
        await Case("资源参考支持默认全选/空选，拒绝越界与非计划资源",()=>{
            Check(library.ReferenceResources([plan.Id],null).Contains("Assets/unit.cs")); Check(library.ReferenceResources([plan.Id],new(){[plan.Id]=[]})=="");
            Throws(()=>library.ReferenceResources([plan.Id],new(){[plan.Id]=["Assets/other.cs"]}));Throws(()=>library.ReferenceResources([],new(){[plan.Id]=[]}));
            Throws(()=>library.ResourcePath("Assets/../Docs/a.md"));Throws(()=>library.ResourcePath("C:/secrets"));Throws(()=>Index(plan.Id,[],resources:[new("Assets/missing.asset","无证据") ])); return Task.CompletedTask;
        });
        await Case("关闭计划允许索引补充，正文/生命周期/事前预估仍冻结",()=>{
            foreach (string status in new[]{"测试","验收","关闭"}) library.UpdateDetails(plan.Id,MetadataHash(plan.Id),status,null,status=="关闭"?"临时夹具人工结论":null);
            Index(plan.Id,["已核对"]);Check(library.Find(plan.Id).Status=="关闭");
            Throws(()=>Index(plan.Id,[],[new("Assets/unit.cs","modify","不许重启") ]));Throws(()=>library.Save(plan.Id,"# 修改",library.Hash(plan.Id)));return Task.CompletedTask;
        });
        await Case("历史冻结资源后来被删，补其他关联不丢原证据",()=>{
            var original=library.Find(plan.Id).ResourceRefs!.Single();File.Delete(code);
            Index(plan.Id,["已核对"],resources:[original,new("Assets/supplied.anim","临时工程补充资源")]);
            Check(library.Find(plan.Id).ResourceRefs!.Any(r=>r.Path==original.Path&&r.Hash==original.Hash));
            File.WriteAllText(code,"class Unit {}\n");return Task.CompletedTask;
        });
        await Case("来源补丁完整核对尾部空行、换行和无末尾换行标记",()=>{
            const string patch="@@ -1 +1 @@\n-class Unit {}\n+class Unit { int HP; }\n";
            Check(ReviewProvenance.PatchMatches("class Unit {}\n","class Unit { int HP; }\n",patch));
            Check(!ReviewProvenance.PatchMatches("class Unit {}\n","class Unit { int HP; }\n\n",patch));
            Check(!ReviewProvenance.PatchMatches("class Unit {}\n","class Unit { int HP; }\r\n",patch));
            Check(ReviewProvenance.PatchMatches("a","b","@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+b\n\\ No newline at end of file\n"));
            Check(ReviewProvenance.PatchMatches("a\nb\n","a\nx\nb\n","@@ -1,0 +2 @@\n+x\n"));return Task.CompletedTask;
        });
        await Case("Unity字段投影仅作内部冻结说明证据，二进制不伪造行为",()=>{
            var f=new ReviewFile{Path="Assets/camp.prefab",Kind="修改"};
            string before="%YAML 1.1\n--- !u!1 &1\nGameObject:\n  m_Name: Camp\n  m_IsActive: 0\n", after=before.Replace("m_IsActive: 0","m_IsActive: 1");
            var summary=ReviewSummary.Describe(f,before,after);Check(summary.Kind=="unity" && summary.Changes.Any(c=>c.Field.Contains("m_IsActive")&&c.Before=="0"&&c.After=="1"));
            var binary=ReviewSummary.Describe(new ReviewFile{Before=new("hash",99,null),Path="Assets/a.png",Kind="删除"},null,null);Check(binary.Kind=="binary"&&binary.Limitation.Contains("无法推断"));return Task.CompletedTask;
        });
        TaskReview task=null!;
        await Case("实时快照默认来源未知，meta永久排除且基线保留",async()=>{
            task=await reviews.Start("fixture-plan","实时审批",default);
            File.WriteAllText(code,"class Unit { int HP; }\n");File.WriteAllText(human,"new provided animation\n");File.WriteAllText(meta,"guid: updated\n");
            await reviews.RefreshChanged(default,true);task=reviews.Get(task.Id);Check(task.Files.Count==2&&task.Files.All(f=>f.Origin=="unknown")&&task.Baseline.ContainsKey("Assets/unit.cs.meta"));
            task=await reviews.Submit(task.Id,task.Revision,"真实静态变更",default);await ThrowsAsync(()=>reviews.DecideTask(task.Id,task.Revision,true,"",default));
        });
        await Case("真实文件补丁认领Agent，人工动画排除，拒绝陈旧归属",async()=>{
            await reviews.RecordAgentPatch(task.Id,"Assets/unit.cs","@@ -1 +1 @@\n-class Unit {}\n+class Unit { int HP; }\n","write-1",default);task=reviews.Get(task.Id);
            Check(task.Files.Single(f=>f.Path=="Assets/unit.cs").Origin=="agent");
            var asset=task.Files.Single(f=>f.Path=="Assets/supplied.anim");task=await reviews.SetOrigin(task.Id,task.Revision,asset.Path,asset.Fingerprint,"human","用户提供的动画素材",default);
            await ThrowsAsync(()=>reviews.DecideFile(task.Id,task.Revision,asset.Path,asset.Fingerprint,true,[],default));
            task=await FixtureNarratives(reviews,task);var file=task.Files.Single(f=>f.Path=="Assets/unit.cs");task=await reviews.DecideFile(task.Id,task.Revision,file.Path,file.Fingerprint,true,[],default);Check(file.Path=="Assets/unit.cs");
        });
        await Case("后台更新保留摘要/未变意见，变化失效且后续Agent补丁可再次证明",async()=>{
            task=reviews.SaveSummary(task.Id,task.Revision,"人工编辑的摘要");
            File.WriteAllText(code,"class Unit { int HP; int MP; }\n");await reviews.RefreshChanged(default,true);task=reviews.Get(task.Id);
            Check(task.Summary=="人工编辑的摘要"&&task.Files.Single(f=>f.Path=="Assets/unit.cs").Origin=="unknown");
            await reviews.RecordAgentPatch(task.Id,"Assets/unit.cs","@@ -1 +1 @@\n-class Unit { int HP; }\n+class Unit { int HP; int MP; }\n","write-2",default);task=reviews.Get(task.Id);
            var file=task.Files.Single(f=>f.Path=="Assets/unit.cs");Check(file.Origin=="agent"&&file.Decision=="待审批");
            task=await reviews.DecideFile(task.Id,task.Revision,file.Path,file.Fingerprint,false,[new("需要校正")],default);
            File.WriteAllText(meta,"guid: ignored-again\n");await reviews.RefreshChanged(default,true);var fresh=reviews.Get(task.Id);Check(fresh.Revision==task.Revision&&fresh.Files.Single(f=>f.Path==file.Path).Notes.Single().Text=="需要校正");
        });
        await Case("退回保留意见，重执行已变文件重审，来源人工确认后整体通过",async()=>{
            task=await reviews.DecideTask(task.Id,task.Revision,false,"再执行",default);task=reviews.Resume(task.Id,task.Revision);
            File.WriteAllText(code,"class Unit { int HP; int MP; int Speed; }\n");task=await reviews.Submit(task.Id,task.Revision,"修正",default);
            var file=task.Files.Single(f=>f.Path=="Assets/unit.cs");task=await reviews.SetOrigin(task.Id,task.Revision,file.Path,file.Fingerprint,"agent","临时夹具明确认领",default);
            task=await FixtureNarratives(reviews,task);task=await reviews.DecideTask(task.Id,task.Revision,true,"夹具批准",default);Check(task.State=="已通过"&&task.Files.Single(f=>f.Origin=="human").Decision=="待审批");
            using var reload=new TaskReviewStore(root);Check(reload.Get(task.Id).Attempts.Count==1&&File.ReadAllText(human)=="new provided animation\n");
        });
        await Case("固定构建菜单不能取消暂停标记或通过虚假菜单绕过",()=>{
            var catalog=new ExtensionToolCatalog(new(root),library);
            Throws(()=>catalog.Register(new(){Id="unsafe-build",Title="错误声明",Kind="unity-mcp",ToolName="script-execute",MenuPath="FrameSyncMoba/Build Local NGO/Build Both",UnityBuild=false},"错误例子"));
            Throws(()=>catalog.Register(new(){Id="unknown-menu",Title="错误菜单",Kind="unity-mcp",ToolName="script-execute",MenuPath="FrameSyncMoba/Build Local NGO/Unknown",UnityBuild=true},"错误例子"));return Task.CompletedTask;
        });
        await Case("GUI启动返回PID且不等待退出，测试只启动自有临时进程",async()=>{
            var runner=new ExternalToolRunner(new(root));
            var result=await runner.Run(new(){Executable=Environment.ProcessPath!,LaunchMode="gui",Arguments=["--fixture-child"]},JsonSerializer.SerializeToElement(new{}),default);
            var match=System.Text.RegularExpressions.Regex.Match(result,"PID=([0-9]+)");Check(match.Success);
            using var child=System.Diagnostics.Process.GetProcessById(int.Parse(match.Groups[1].Value));
            try {Check(!child.HasExited&&result.Contains("只证明进程创建"));}finally{if(!child.HasExited){child.Kill();await child.WaitForExitAsync();}}
        });
        Console.WriteLine($"Function polish checks: {passed} passed, {failed} failed; isolated fixture only.");return failed==0?0:1;
    }
}
