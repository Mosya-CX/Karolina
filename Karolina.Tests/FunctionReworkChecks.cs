using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private sealed record NarrativeManifest(string Id,Dictionary<string,string> Fingerprints,ReviewNarrativeInput[] Files,string Source);
    private static async Task<int> ImportReviewNarratives(string root,string path)
    {
        var manifest=JsonSerializer.Deserialize<NarrativeManifest>(File.ReadAllText(path),DocumentLibrary.Json)??throw new InvalidDataException("说明清单损坏");
        using var guard=WorkspaceLock.Acquire(root);using var store=new TaskReviewStore(root);
        var task=await store.ApplyNarratives(manifest.Id,manifest.Fingerprints,manifest.Files,manifest.Source,default);
        Console.WriteLine(JsonSerializer.Serialize(new{task.Id,task.Revision,task.State,files=manifest.Files.Length},DocumentLibrary.Json));return 0;
    }
    private static Task<TaskReview> FixtureNarratives(TaskReviewStore store,TaskReview task)=>store.ApplyNarratives(task.Id,task.Files.ToDictionary(f=>f.Path,f=>f.Fingerprint),task.Files.Where(f=>f.Origin=="agent").Select(f=>new ReviewNarrativeInput(f.Path,"临时测试操作说明","测试夹具已明确写入该文件","验证审批合同","仅临时文件状态测试")).ToArray(),"临时夹具",default);
    private static async Task<int> FunctionReworkChecks()
    {
        using var fixture=new Fixture();string root=fixture.Root;
        Directory.CreateDirectory(Path.Combine(root,"Assets"));
        string code=Path.Combine(root,"Assets/unit.cs"),asset=Path.Combine(root,"Assets/camp.prefab"),model=Path.Combine(root,"Assets/model.glb");
        File.WriteAllText(code,"class Unit {}\n");File.WriteAllText(asset,"%YAML 1.1\nGameObject:\n  m_Name: Camp\n  m_IsActive: 0\n");
        using var store=new TaskReviewStore(root);TaskReview task=await store.Start("PLAN-TEST","测试计划",default);
        File.WriteAllText(code,"class Unit { int HP; }\n");File.WriteAllText(asset,"%YAML 1.1\nGameObject:\n  m_Name: Camp\n  m_IsActive: 1\n");File.WriteAllBytes(model,[0,1,0,2]);
        task=await store.Submit(task.Id,task.Revision,"配置营地",default);
        await Case("说明协议拒绝空元素、缺数组、重复路径与无具体操作",()=>{
            foreach(string invalid in new[]{"{\"files\":[null]}","{}","{\"files\":null}","[]","{\"files\":[{\"path\":\"a\",\"summary\":\"a\",\"whatChanged\":\"b\"},{\"path\":\"a\",\"summary\":\"a\",\"whatChanged\":\"b\"}]}","{\"files\":[{\"path\":\"a\",\"summary\":\"a\",\"whatChanged\":\"\"}]}"})Throws(()=>ReviewNarratives.Parse("```karolina-review\n"+invalid+"\n```"));return Task.CompletedTask;
        });
        await Case("人工导入模型不参与审批且不能生成Agent操作说明",async()=>{
            var f=task.Files.Single(f=>f.Path=="Assets/model.glb");task=await store.SetOrigin(task.Id,task.Revision,f.Path,f.Fingerprint,"human","用户明确确认手工导入",default);
            await ThrowsAsync(()=>store.ApplyNarratives(task.Id,task.Files.ToDictionary(f=>f.Path,f=>f.Fingerprint),[new(f.Path,"错误认领","错误认领")],"测试",default));Check(store.Get(task.Id).Files.Single(x=>x.Path==f.Path).Origin=="human");
        });
        await Case("冻结差异补充说明独立于作者、单文件和整体决定",async()=>{
            var f=task.Files.Single(f=>f.Path=="Assets/camp.prefab");task=await store.ApplyNarratives(task.Id,new(){[f.Path]=f.Fingerprint},[new(f.Path,"启用营地对象","将营地对象从停用改为启用","用于场景加载","未运行Unity",f.Fingerprint)],"冻结差异分析",default);
            f=task.Files.Single(x=>x.Path==f.Path);Check(f.Origin=="unknown"&&f.Decision=="待审批"&&task.State=="待审批"&&TaskReviewStore.HasExplanation(f));
        });
        await Case("资源差异接口不返回YAML原文，代码仍有逐行差异",()=>{
            string raw=JsonSerializer.Serialize(store.Diff(task.Id,"Assets/camp.prefab"),DocumentLibrary.Json);Check(!raw.Contains("%YAML")&&!raw.Contains("m_IsActive")&&JsonSerializer.SerializeToElement(store.Diff(task.Id,"Assets/camp.prefab"),DocumentLibrary.Json).GetProperty("explanation").GetProperty("summary").GetString()=="启用营地对象");
            string c=JsonSerializer.Serialize(store.Diff(task.Id,"Assets/unit.cs"),DocumentLibrary.Json);Check(c.Contains("added")&&c.Contains("int HP"));return Task.CompletedTask;
        });
        await Case("说明先全量验证，错误路径不能导致部分保存",async()=>{
            var f=task.Files.Single(f=>f.Path=="Assets/unit.cs");int revision=task.Revision;
            await ThrowsAsync(()=>store.ApplyNarratives(task.Id,new(){[f.Path]=f.Fingerprint},[new(f.Path,"本不应保存","本不应保存"),new("Assets/missing.cs","错误","错误")],"测试",default));Check(store.Get(task.Id).Revision==revision&&store.Get(task.Id).Files.Single(x=>x.Path==f.Path).Explanation==null);
        });
        await Case("Agent文件缺少说明不能单独或整体批准，但允许有理由退回",async()=>{
            foreach(var f in task.Files.Where(f=>f.Origin=="unknown").ToArray())task=await store.SetOrigin(task.Id,task.Revision,f.Path,f.Fingerprint,"agent","临时夹具明确写入者",default);
            var codeFile=task.Files.Single(f=>f.Path=="Assets/unit.cs");await ThrowsAsync(()=>store.DecideFile(task.Id,task.Revision,codeFile.Path,codeFile.Fingerprint,true,[],default));await ThrowsAsync(()=>store.DecideTask(task.Id,task.Revision,true,"",default));
            task=await store.DecideFile(task.Id,task.Revision,codeFile.Path,codeFile.Fingerprint,false,[new("请解释改动")],default);Check(task.Files.Single(f=>f.Path==codeFile.Path).Decision=="不通过");
        });
        await Case("说明绑定文件指纹，后台修改失效，旧版本输入拒绝",async()=>{
            var f=task.Files.Single(f=>f.Path=="Assets/camp.prefab");string old=f.Fingerprint;File.AppendAllText(asset,"  m_Layer: 2\n");await store.RefreshChanged(default,true);task=store.Get(task.Id);Check(task.Files.Single(x=>x.Path==f.Path).Explanation==null);
            await ThrowsAsync(()=>store.ApplyNarratives(task.Id,new(){[f.Path]=old},[new(f.Path,"旧说明","旧说明")],"测试",default));
        });
        await Case("说明与人工模型来源重启后持久保存",async()=>{
            task=await FixtureNarratives(store,task);using var restored=new TaskReviewStore(root);var t=restored.Get(task.Id);Check(t.Files.Single(f=>f.Path=="Assets/model.glb").Origin=="human"&&t.Files.Single(f=>f.Path=="Assets/unit.cs").Explanation?.Source=="临时夹具");
        });
        await Case("超大代码说明上下文有界，明确未完整分析",async()=>{
            File.WriteAllText(code,string.Concat(Enumerable.Repeat("large source line\n",20000)));await store.RefreshChanged(default,true);task=store.Get(task.Id);string context=store.ExplanationContext(task);Check(context.Length<85000&&JsonDocument.Parse(System.Text.RegularExpressions.Regex.Match(context,@"```karolina-snapshots\s*\r?\n([\s\S]*?)```").Groups[1].Value).RootElement.EnumerateArray().Any(f=>f.GetProperty("evidence").GetString()!.Contains("未计算完整差异"))&&context.Contains("\"truncated\": true"));
        });
        await Case("一个工具注册多个函数，父声明hash锁定函数，拒绝未知函数与嵌套组",()=>{
            using var library=new DocumentLibrary(root);library.Create("rule","夹具使用说明","fixture");var catalog=new ExtensionToolCatalog(new(root),library);
            var group=catalog.Register(new(){Id="test-group",Title="测试函数集合",Kind="group",Operations=[new(){Id="one",Title="函数一",Kind="external",Executable="fixture.exe",ReadOnly=true},new(){Id="two",Title="函数二",Kind="external",Executable="fixture.exe",ReadOnly=true} ]},"测试工具说明");
            var one=catalog.Resolve(group.Definition.Id,"one");var two=catalog.Resolve(group.Definition.Id,"two");Check(one.GroupId==group.Definition.Id&&one.Hash==two.Hash&&one.Definition.GuideId==group.Definition.GuideId&&one.OperationId!="two");Throws(()=>catalog.Resolve(group.Definition.Id,"unknown"));
            Throws(()=>catalog.Register(new(){Id="invalid",Title="错误",Kind="group",Operations=[new(){Id="nested",Title="错误嵌套",Kind="group"} ]},"错误声明"));return Task.CompletedTask;
        });
        Console.WriteLine($"Function rework checks: {passed} passed, {failed} failed; isolated fixture only.");return failed==0?0:1;
    }
}
