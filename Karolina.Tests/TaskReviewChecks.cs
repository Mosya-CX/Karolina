using System.Text;
using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private sealed record ImportManifest(string PlanId, string Title, string Description, string Summary, RetrospectiveFile[] Files, string? ReplaceId = null);
    private static async Task<int> ImportInterrupted(string root, string manifestPath)
    {
        var manifest = JsonSerializer.Deserialize<ImportManifest>(File.ReadAllText(manifestPath), DocumentLibrary.Json) ?? throw new InvalidDataException("导入清单损坏");
        using var library = new DocumentLibrary(root);
        var plan = library.Find(manifest.PlanId);
        if (plan.Type != "plan" || plan.Status == "关闭") throw new InvalidOperationException("导入需关联未关闭计划");
        using var guard = WorkspaceLock.Acquire(root);
        var task = await new TaskReviewStore(root).ImportInterrupted(plan.Id, manifest.Title, manifest.Description, manifest.Summary, manifest.Files, replaceId: manifest.ReplaceId);
        Console.WriteLine(JsonSerializer.Serialize(new { task.Id, task.PlanId, task.State, files = task.Files.Count }, DocumentLibrary.Json));
        return 0;
    }
    private static async Task<int> TaskReviewChecks()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(Path.Combine(f.Root, "Assets"));
        string a = Path.Combine(f.Root, "Assets/a.cs"), b = Path.Combine(f.Root, "Assets/camp.prefab"), unrelated = Path.Combine(f.Root, "Assets/unrelated.txt");
        File.WriteAllText(a, "new wolf\n"); File.WriteAllText(b, "camp\n"); File.WriteAllText(unrelated, "already existed\n");
        using var store = new TaskReviewStore(f.Root);
        TaskReview task = null!;
        await Case("事后整理显示来源，只收录有证据候选且保护既有无关内容", async () => {
            task = await store.ImportInterrupted("PLAN-WOLF-165", "三狼重做中断", "历史参考，不是真实开始基线", "仅整理当前部分内容，尚未完成", [new("Assets/a.cs", Encoding.UTF8.GetBytes("old wolf\n"), RequirementStore.Hash(File.ReadAllBytes(a)), "共享源码候选"), new("Assets/camp.prefab", null, RequirementStore.Hash(File.ReadAllBytes(b)), "专用资源")]);
            Check(task.State == "待审批" && task.Files.Count == 2 && task.BaselineKind.Contains("事后")); Check(task.Files.All(x => x.Decision == "待审批")); Check(!task.Files.Any(x => x.Path.Contains("unrelated")));
        });
        await Case("代码冻结前后差异可读取", () => { string diff = JsonSerializer.Serialize(store.Diff(task.Id,"Assets/a.cs"),DocumentLibrary.Json); Check(diff.Contains("deleted") && diff.Contains("added") && diff.Contains("old wolf") && diff.Contains("new wolf")); return Task.CompletedTask; });
        await Case("事后补充清点保留同一条目和最初基线", async () => {
            string id=task.Id; int revision=task.Revision;
            task=await store.ImportInterrupted(task.PlanId,task.Title,task.BaselineDescription,task.Summary,[new("Assets/a.cs",Encoding.UTF8.GetBytes("old wolf\n"),RequirementStore.Hash(File.ReadAllBytes(a)),"共享"),new("Assets/camp.prefab",null,RequirementStore.Hash(File.ReadAllBytes(b)),"资源")],replaceId:id);
            Check(task.Id==id && task.Revision==revision+1 && task.Baseline["Assets/unrelated.txt"].Hash==RequirementStore.Hash(File.ReadAllBytes(unrelated)));
        });
        await Case("拒绝重复启动与无理由文件驳回", async () => { await ThrowsAsync(() => store.Start("other", "other", default)); var file=task.Files[0]; await ThrowsAsync(() => store.DecideFile(task.Id, task.Revision,file.Path,file.Fingerprint,false,[],default)); });
        await Case("用户仅提交新摘要也禁止维护重导入覆盖", async () => {
            task=await store.Submit(task.Id,task.Revision,"用户已经保存的新摘要",default);
            await ThrowsAsync(()=>store.ImportInterrupted(task.PlanId,task.Title,"说明","过时摘要",[new("Assets/a.cs",Encoding.UTF8.GetBytes("old wolf\n"),RequirementStore.Hash(File.ReadAllBytes(a)),"共享"),new("Assets/camp.prefab",null,RequirementStore.Hash(File.ReadAllBytes(b)),"资源")],replaceId:task.Id));
            Check(store.Get(task.Id).Summary=="用户已经保存的新摘要");
        });
        foreach(var candidate in task.Files.ToArray()) task=await store.SetOrigin(task.Id,task.Revision,candidate.Path,candidate.Fingerprint,"agent","测试夹具人工明确来源",default);
        await Case("逐文件通过、定位行意见、陈旧修订拒绝", async () => {
            task=await FixtureNarratives(store,task);var file=task.Files.Single(x=>x.Path=="Assets/camp.prefab"); int old=task.Revision; task=await store.DecideFile(task.Id,old,file.Path,file.Fingerprint,true,[],default);
            await ThrowsAsync(()=>store.DecideFile(task.Id,old,file.Path,file.Fingerprint,true,[],default));
            file=task.Files.Single(x=>x.Path=="Assets/a.cs"); task=await store.DecideFile(task.Id,task.Revision,file.Path,file.Fingerprint,false,[new("这里需要修改","after",1)],default); Check(task.Files.Single(x=>x.Path==file.Path).Notes[0].Line==1);
        });
        await Case("已有人工意见不能被维护重导入覆盖", async () => { await ThrowsAsync(()=>store.ImportInterrupted(task.PlanId,task.Title,"说明","摘要",[new("Assets/a.cs",null,RequirementStore.Hash(File.ReadAllBytes(a)),"资源"),new("Assets/camp.prefab",null,RequirementStore.Hash(File.ReadAllBytes(b)),"资源")],replaceId:task.Id)); });
        await Case("有不通过文件不能整体批准，整体退回保留意见", async () => { await ThrowsAsync(()=>store.DecideTask(task.Id,task.Revision,true,"",default)); task=await store.DecideTask(task.Id,task.Revision,false,"修正后再执行",default); Check(task.State=="待再执行" && task.Attempts.Count==1 && store.FeedbackPrompt(task).Contains("这里需要修改")); });
        await Case("返回再执行保留原基线，变化重审、不变结论保留", async () => { task=store.Resume(task.Id,task.Revision); Check(task.Round==2 && task.Baseline["Assets/a.cs"].Hash==RequirementStore.Hash(Encoding.UTF8.GetBytes("old wolf\n"))); File.WriteAllText(a,"fixed wolf\n"); task=await store.Submit(task.Id,task.Revision,"修正模拟内容",default); Check(task.Files.Single(x=>x.Path=="Assets/a.cs").Decision=="待审批"); Check(task.Files.Single(x=>x.Path=="Assets/camp.prefab").Decision=="通过"); });
        await Case("审批时内容变化拒绝，整理后新文件不会漏掉", async () => { File.WriteAllText(a,"changed externally\n"); var file=task.Files.Single(x=>x.Path=="Assets/a.cs"); await ThrowsAsync(()=>store.DecideFile(task.Id,task.Revision,file.Path,file.Fingerprint,true,[],default)); await ThrowsAsync(()=>store.DecideTask(task.Id,task.Revision,true,"",default)); File.WriteAllText(Path.Combine(f.Root,"Assets/new.asset"),"new\n"); task=await store.Submit(task.Id,task.Revision,"再整理",default); Check(task.Files.Count==3); });
        await Case("临时任务整体通过不改工程正文、不产生 Git 操作", async () => { string contents=File.ReadAllText(a); foreach(var candidate in task.Files.Where(f=>f.Origin=="unknown").ToArray()) task=await store.SetOrigin(task.Id,task.Revision,candidate.Path,candidate.Fingerprint,"agent","测试夹具人工明确来源",default); task=await FixtureNarratives(store,task);task=await store.DecideTask(task.Id,task.Revision,true,"临时夹具批准",default); Check(task.State=="已通过" && File.ReadAllText(a)==contents && File.ReadAllText(unrelated)=="already existed\n"); });
        await Case("整理输入内容指纹过期、越界和重复路径可见失败", async () => { await ThrowsAsync(()=>store.ImportInterrupted("p","p","说明","摘要",[new("Assets/a.cs",null,"wrong","原因")])); await ThrowsAsync(()=>store.ImportInterrupted("p","p","说明","摘要",[new("Assets/../Docs/outside",null,null,"原因")])); await ThrowsAsync(()=>store.ImportInterrupted("p","p","说明","摘要",[new("Assets/a.cs",null,null,"原因"),new("Assets/A.cs",null,null,"原因")])); });
        await Case("正常任务仍用开始前实际内容，不把既有修改当任务新增", async () => { task=await store.Start("p","正常开始",default); task=await store.Submit(task.Id,task.Revision,"尚未修改",default); Check(task.Files.Count==0 && task.BaselineKind=="任务开始实测"); });
        Console.WriteLine($"Task review checks: {passed} passed, {failed} failed; isolated fixture only.");
        return failed==0?0:1;
    }
}
