using System.Net;
using System.Text;
using System.Text.Json;
using Karolina.Core;

internal static partial class Program
{
    private static int passed, failed;
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--import-interrupted") return await ImportInterrupted(args[1], args[2]);
        if (args.Contains("--task-reviews")) return await TaskReviewChecks();
        if (args.Contains("--fixture-function")) {Console.WriteLine(args[^1]);return 0;}
        if (args.Length==3&&args[0]=="--export-review-context") {using var store=new TaskReviewStore(args[1]);Console.WriteLine(store.ExplanationContext(store.Get(args[2])));return 0;}
        if (args.Length==3&&args[0]=="--import-review-narratives") return await ImportReviewNarratives(args[1],args[2]);
        if (args.Contains("--function-rework")) return await FunctionReworkChecks();
        if (args.Contains("--function-polish")) return await FunctionPolishChecks();
        if (args.Contains("--appearance")) return await AppearanceChecks();
        if (args.Contains("--panel-layout")) return await PanelLayoutChecks();
        if (args.Contains("--graph-settings")) return await GraphSettingsChecks();
        if (args.Length == 2 && args[0] == "--graph-project") return await GraphProjectCheck(args[1]);
        if (Environment.GetEnvironmentVariable("KAROLINA_APPSERVER_FIXTURE") == "1") { await AppServerFixture(); return 0; }
        if (Environment.GetEnvironmentVariable("KAROLINA_FIXTURE_MODE") is { } fixtureMode)
        {
            _ = await Console.In.ReadToEndAsync();
            Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fixture-thread\"}");
            if (fixtureMode == "complete") Console.WriteLine("{\"type\":\"turn.completed\"}");
            return 0;
        }
        if (args.Contains("--fixture-codex")) { Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fixture-thread\"}"); Console.WriteLine("{\"type\":\"turn.completed\"}"); return 0; }
        if (args.Contains("--fixture-incomplete")) { Console.WriteLine("{\"type\":\"thread.started\",\"thread_id\":\"fixture-thread\"}"); return 0; }
        if (args.Contains("--fixture-child")) { Console.WriteLine(Environment.ProcessId); await Task.Delay(TimeSpan.FromMinutes(2)); return 0; }
        if (args.Contains("--live")) { Console.Error.WriteLine("旧 CLI 执行验收入口已退役，请运行 maintenance/verify_workbench.py 验证 app-server 前端。"); return 2; }
        string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("../../../..");
        await Case("中文功能库正文与独立元数据一致", () => { var library = new DocumentLibrary(root); var list = library.List(); Check(list.Length > 0,"文档目录不能为空"); Check(list.Any(d=>d.Type=="rule"),"必须至少有一份规则案"); Check(library.ReferenceContext([]).Contains("未指定"),"空参考资料需要明确表达未指定"); var requirement=list.First(d=>d.Type=="requirement"); Check(library.ReferenceContext([requirement.Id]).Contains(requirement.Title),"参考上下文需要显示所选需求标题"); foreach(var d in list) Check(library.Read(d.Id).StartsWith("# "+d.Title),"正文一级标题与元数据标题不一致："+d.Id); return Task.CompletedTask; });
        await Case("中文案创建、并发编辑与路径保护", () => { using var f=new Fixture(); var l=new DocumentLibrary(f.Root); var d=l.Create("requirement","示例功能","示例模块"); Check(d.Status=="激活"); Check(File.ReadAllText(Path.Combine(f.Root,"Docs/catalog.json")).Contains(d.Id)); string hash=l.Hash(d.Id); l.Save(d.Id,"# 示例功能\n\n新的具体内容",hash); Check(l.Metadata(d.Id).GetProperty("version").GetInt32()==2); Throws(()=>l.Save(d.Id,"旧编辑",hash)); Throws(()=>l.Create("plan","../非法","模块")); Throws(()=>l.SafePath("../outside")); Throws(()=>l.Create("requirement","示例功能","示例模块")); return Task.CompletedTask; });
        await Case("类别生命周期、需求专属演进与关闭保护", () => {
            using var f=new Fixture();var l=new DocumentLibrary(f.Root);var r=l.Create("requirement","具体功能","模块");var rule=l.Create("rule","工程规则","模块");Check(rule.Status==null);Check(!l.Metadata(rule.Id).TryGetProperty("status",out _));
            l.Save(r.Id,"# 具体功能\n\n格式调整",l.Hash(r.Id));Check(!l.Metadata(r.Id).TryGetProperty("history",out _));
            l.Save(r.Id,"# 具体功能\n\n变更目标",l.Hash(r.Id),"增加目标边界\n\n原因：新增多人场景。\n\n兼容：旧数据保持可读。");Check(l.Read(r.Id).Contains("## 需求演进") && l.Read(r.Id).Contains("兼容：旧数据保持可读。"));Check(!l.Metadata(r.Id).TryGetProperty("history",out _));Check(Path.GetFileName(r.Path).StartsWith(r.Id+"_") && !Path.GetFileName(r.Path).Contains("具体"));
            var plan=l.Create("plan","执行功能","模块",[r.Id]);Check(l.Metadata(plan.Id).GetProperty("requirementRefs")[0].GetProperty("title").GetString()==r.Title);Check(l.Read(plan.Id).Contains("[具体功能]"));var planMetaPath=l.SafePath(l.Find(plan.Id).MetadataPath);l.UpdateIndex(plan.Id,RequirementStore.Hash(File.ReadAllBytes(planMetaPath)),[],[new PlannedChange("Assets/Example.prefab","add","测试事前预估")],[]);
            int referencedVersion=l.Metadata(plan.Id).GetProperty("requirementRefs")[0].GetProperty("version").GetInt32();l.Save(r.Id,"# 具体功能\n\n执行期新需求",l.Hash(r.Id),"执行期变更");
            string Mh()=>RequirementStore.Hash(File.ReadAllBytes(l.SafePath(l.Find(plan.Id).MetadataPath)));
            l.UpdateDetails(plan.Id,Mh(),"执行",[r.Id],null);Check(l.Metadata(plan.Id).GetProperty("requirementRefs")[0].GetProperty("version").GetInt32()==referencedVersion);Check(JsonDocument.Parse(File.ReadAllText(Path.Combine(f.Root,"Docs/catalog.json"))).RootElement.GetProperty("activePlan").GetString()==plan.Id);
            Throws(()=>l.UpdateDetails(plan.Id,Mh(),"关闭",null,"跳过测试"));l.UpdateDetails(plan.Id,Mh(),"测试",null,null);l.UpdateDetails(plan.Id,Mh(),"校正",null,null);l.UpdateDetails(plan.Id,Mh(),"执行",null,null);l.UpdateDetails(plan.Id,Mh(),"测试",null,null);l.UpdateDetails(plan.Id,Mh(),"验收",null,null);Throws(()=>l.UpdateDetails(plan.Id,Mh(),"关闭",null,null));l.UpdateDetails(plan.Id,Mh(),"校正",null,null);l.UpdateDetails(plan.Id,Mh(),"测试",null,null);l.UpdateDetails(plan.Id,Mh(),"验收",null,null);l.UpdateDetails(plan.Id,Mh(),"关闭",null,"人工验收通过，回执见本机记录");
            Throws(()=>l.Save(plan.Id,"# 覆盖",l.Hash(plan.Id)));Throws(()=>l.UpdateDetails(plan.Id,Mh(),"执行",null,null));Check(!l.Metadata(plan.Id).TryGetProperty("history",out _));return Task.CompletedTask;
        });
        await Case("索引通知延迟不能绕过外部关闭保护", () => {
            using var f=new Fixture();using var l=new DocumentLibrary(f.Root);var plan=l.Create("plan","即将外部关闭","模块");l.List();
            var watcher=(FileSystemWatcher)typeof(DocumentLibrary).GetField("watcher",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(l)!;watcher.EnableRaisingEvents=false;
            string meta=l.SafePath(plan.MetadataPath);var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(meta))!;node["status"]="关闭";File.WriteAllText(meta,node.ToJsonString());
            Check(l.Find(plan.Id).Status=="准备");Throws(()=>l.Save(plan.Id,"# 修改关闭计划",l.Hash(plan.Id)));Throws(()=>l.UpdateDetails(plan.Id,RequirementStore.Hash(File.ReadAllBytes(meta)),"执行",null,null));return Task.CompletedTask;
        });
        await Case("计划自然编码排序与外部元数据更新", () => {
            using var f=new Fixture();var l=new DocumentLibrary(f.Root);var a=l.Create("plan","后执行","Z");var b=l.Create("plan","先执行","A");
            void Code(LibraryDocument d,string code){var p=l.SafePath(d.MetadataPath);var n=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(p))!;n["code"]=code;File.WriteAllText(p,n.ToJsonString());}
            Code(a,"10");Code(b,"2");Check(l.List()[0].Id==b.Id);Code(a,"1");Check(l.List()[0].Id==a.Id);return Task.CompletedTask;
        });
        await Case("Git 批量超长路径标准输入与无文本差异过滤", async () => {
            using var f=new Fixture();await Git(f.Root,"init");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");await Git(f.Root,"config","core.autocrlf","true");var g=new GitService(f.Root);
            string[] names=Enumerable.Range(0,600).Select(i=>$"中文 [{i}] "+new string('x',60)+".txt").ToArray();foreach(var n in names)File.WriteAllText(Path.Combine(f.Root,n),"one\n");
            await g.Action("stage",names,null);Check((await g.Status()).Length==600);await g.Action("commit",[],"temporary fixture baseline");
            File.WriteAllText(Path.Combine(f.Root,names[0]),"one\r\n");File.AppendAllText(Path.Combine(f.Root,names[1]),"two\r\n");File.WriteAllBytes(Path.Combine(f.Root,"binary.bin"),[0,1,2]);File.WriteAllBytes(Path.Combine(f.Root,"empty.txt"),[]);
            var review=await g.ReviewStatus();Check(!review.Any(c=>c.Path==names[0]));Check(review.Single(c=>c.Path==names[1]).WorktreeText);Check(review.Single(c=>c.Path=="binary.bin").WorktreeOther);Check(review.Single(c=>c.Path=="empty.txt").WorktreeOther);
            await g.Action("stage",[names[1]],null);Check((await g.ReviewStatus()).Single(c=>c.Path==names[1]).IndexText);await g.Action("unstage",[names[1]],null);
        });
        await Case("Git 分区差异、暂存后再编辑、取消与初始无 HEAD", async () => { using var f=new Fixture(); await Git(f.Root,"init"); var g=new GitService(f.Root); File.WriteAllText(Path.Combine(f.Root,"中文 name.txt"),"first\n"); var c=(await g.Status()).Single(c=>c.Path=="中文 name.txt"); Check((await g.AreaDiff(c,"worktree")).Contains("+first")); await g.Action("stage",[c.Path],null); c=(await g.Status()).Single(c=>c.Path=="中文 name.txt"); Check((await g.AreaDiff(c,"index")).Contains("+first")); File.AppendAllText(Path.Combine(f.Root,c.Path),"second\n"); await g.Action("unstage",[c.Path],null); Check((await g.Status()).Single(c=>c.Path=="中文 name.txt").Status=="??"); Check(File.ReadAllText(Path.Combine(f.Root,c.Path)).Contains("second")); await ThrowsAsync(()=>g.Action("stage",["../bad"],null)); });
        await Case("Git 所选提交保留其它暂存，采用当前正文且支持初始 HEAD", async () => {
            using var f=new Fixture(); await Git(f.Root,"init");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");var g=new GitService(f.Root);
            File.WriteAllText(Path.Combine(f.Root,"selected.txt"),"first\n");File.WriteAllText(Path.Combine(f.Root,"other.txt"),"other\n");await g.Action("stage",["selected.txt","other.txt"],null);
            File.AppendAllText(Path.Combine(f.Root,"selected.txt"),"latest\n");await g.Action("commit-selected",["selected.txt"],"first selected");
            var head=await ProcessRunner.RunAsync("git",["show","HEAD:selected.txt"],f.Root);Check(head.Stdout.Contains("latest"));Check((await g.Status()).Single(c=>c.Path=="other.txt").Status=="A ");
            File.WriteAllText(Path.Combine(f.Root,"new.txt"),"new\n");File.AppendAllText(Path.Combine(f.Root,"selected.txt"),"next\n");Check((await g.WorkingChanges()).Count(c=>c.Path=="selected.txt")==1);
            await g.Action("commit-selected",["selected.txt","new.txt"],"next selected");Check((await g.Status()).Single(c=>c.Path=="other.txt").Status=="A ");
            var diff=await g.CommitDiff((await g.History())[0].Hash);Check(diff.Contains("next")&&diff.Contains("new.txt")&&!diff.Contains("other.txt"));
            File.AppendAllText(Path.Combine(f.Root,"selected.txt"),"hidden\n");await g.Action("stage",["selected.txt"],null);File.WriteAllText(Path.Combine(f.Root,"selected.txt"),head.Stdout+"next\n");Check(!(await g.WorkingChanges()).Any(c=>c.Path=="selected.txt"));
        });
        await Case("Git 所选重命名和删除包含必要源路径", async () => {
            using var f=new Fixture();await Git(f.Root,"init");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");var g=new GitService(f.Root);
            File.WriteAllText(Path.Combine(f.Root,"source.txt"),"shared source\n");File.WriteAllText(Path.Combine(f.Root,"delete.txt"),"delete\n");await g.Action("commit-selected",["source.txt","delete.txt"],"baseline");
            await Git(f.Root,"mv","source.txt","renamed.txt");File.Delete(Path.Combine(f.Root,"delete.txt"));await g.Action("commit-selected",["renamed.txt","delete.txt"],"rename delete");Check((await g.Status()).All(c=>c.Path.StartsWith("Docs/")));Check(!File.Exists(Path.Combine(f.Root,"source.txt")));
        });
        await Case("Git 复制目标不带走未选源文件", async () => {
            using var f=new Fixture();await Git(f.Root,"init");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");await Git(f.Root,"config","status.renames","copies");var g=new GitService(f.Root);
            string content=string.Join('\n',Enumerable.Range(0,100).Select(i=>"source line "+i))+"\n";File.WriteAllText(Path.Combine(f.Root,"source.txt"),content);await g.Action("commit-selected",["source.txt"],"baseline");
            File.WriteAllText(Path.Combine(f.Root,"copy.txt"),content);File.AppendAllText(Path.Combine(f.Root,"source.txt"),"unselected source\n");await g.Action("stage",["source.txt","copy.txt"],null);
            Check((await g.Status()).Single(c=>c.Path=="copy.txt").Status.Contains('C'),"Fixture must exercise an actual C copy record");Check(!(await g.AreaDiff((await g.Status()).Single(c=>c.Path=="copy.txt"),"combined")).Contains("unselected source"));await g.Action("commit-selected",["copy.txt"],"only copy");
            var source=await ProcessRunner.RunAsync("git",["show","HEAD:source.txt"],f.Root);Check(source.Stdout==content);Check((await g.Status()).Single(c=>c.Path=="source.txt").Status=="M ");
        });
        await Case("拒绝含路径的恶意元数据编号", () => { using var f=new Fixture(); var l=new DocumentLibrary(f.Root);var d=l.Create("rule","有效标题","测试");var path=Path.Combine(f.Root,d.MetadataPath);var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;node["id"]="../escape";File.WriteAllText(path,node.ToJsonString());Throws(()=>l.List());return Task.CompletedTask; });
        await Case("Git 明确当前分支推送，不受 matching 和额外 refspec 影响", async () => { using var f=new Fixture();await Git(f.Root,"init","-b","main");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");string file="only.txt";File.WriteAllText(Path.Combine(f.Root,file),"one");var g=new GitService(f.Root);await g.Action("stage",[file],null);await g.Action("commit",[],"one");string remote=Path.Combine(f.Root,"remote.git");await Git(f.Root,"init","--bare",remote);await Git(f.Root,"remote","add","origin",remote);await Git(f.Root,"push","-u","origin","main");await Git(f.Root,"branch","extra");await Git(f.Root,"push","origin","extra");File.AppendAllText(Path.Combine(f.Root,file),"two");await g.Action("stage",[file],null);await g.Action("commit",[],"two");await Git(f.Root,"branch","-f","extra","HEAD");await Git(f.Root,"config","push.default","matching");await Git(f.Root,"config","remote.origin.push","refs/heads/extra:refs/heads/extra");Check((await g.PushTarget()).Ref=="refs/heads/main");await Git(f.Root,"remote","set-url","origin","https://github.com/karolina-fixture/local.git");await Git(f.Root,"config","url."+remote.Replace("\\","/")+".insteadOf","https://github.com/karolina-fixture/local.git");await g.Action("push",[],null);Check((await g.History()).Length==2);Check((await g.CommitDiff((await g.History())[0].Hash)).Contains("two"));bool dirtyRefused=false;try{await g.Action("pull",[],null);}catch(InvalidOperationException){dirtyRefused=true;}Check(dirtyRefused);File.WriteAllText(Path.Combine(f.Root,".git/info/exclude"),"Docs/\nremote.git/\n");await g.Action("pull",[],null);var refs=await ProcessRunner.RunAsync("git",["ls-remote","origin","refs/heads/main","refs/heads/extra"],f.Root);var heads=refs.Stdout.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(l=>l.Split('\t')[0]).ToArray();Check(heads.Length==2 && heads[0]!=heads[1]); });
        await Case("Git 残留索引锁诊断、拒绝非空锁和恢复后实际提交", async () => {
            using var f=new Fixture();await Git(f.Root,"init");await Git(f.Root,"config","user.name","Fixture");await Git(f.Root,"config","user.email","fixture@local.invalid");var g=new GitService(f.Root,()=>false);string file="中文 [空格].txt";File.WriteAllText(Path.Combine(f.Root,file),"one");string locked=Path.Combine(f.Root,".git","index.lock");File.WriteAllText(locked,"");File.SetLastWriteTimeUtc(locked,DateTime.UtcNow.AddMinutes(-5));bool failed=false;try{await g.Action("stage",[file],null);}catch(IOException e){failed=e.Message.Contains("index.lock");}Check(failed && (await g.IndexLock()).Exists);await RecoverFixtureLock(g, locked);Check(!File.Exists(locked));await g.Action("stage",[file],null);await g.Action("commit",[],"真实提交");Check((await g.History())[0].Title=="真实提交");File.WriteAllText(locked,"");File.SetLastWriteTimeUtc(locked,DateTime.UtcNow.AddMinutes(-5));var runningGit=new GitService(f.Root,()=>true);bool runningRefused=false;try{await runningGit.RecoverIndexLock();}catch(InvalidOperationException){runningRefused=true;}Check(runningRefused,"模拟的同工程Git进程应阻止锁恢复");File.WriteAllText(locked,"nonempty");File.SetLastWriteTimeUtc(locked,DateTime.UtcNow.AddMinutes(-5));bool refused=false;try{await g.RecoverIndexLock();}catch(InvalidOperationException){refused=true;}Check(refused && File.ReadAllText(locked)=="nonempty");
        });
        await Case("scoped amendment retains base; Proposed has no authority; shuffled order deterministic", () =>
        {
            using var fixture = new Fixture(); Requirement baseReq = R("REQ-TEST-001"), patch = R("REQ-TEST-002"), proposal = R("REQ-TEST-003"); patch.Amendments = [new(baseReq.Id, "Only attack cap")]; proposal.Status = "Proposed";
            fixture.Save([patch, proposal, baseReq]); var a = new RequirementStore(fixture.Root).Resolve("test"); Check(a.ReadSet.Length == 2); Check(a.Overrides.Length == 1); fixture.Save([baseReq, patch, proposal]); var b = new RequirementStore(fixture.Root).Resolve("test"); Check(a.ReadSet.Select(r => r.Id).SequenceEqual(b.ReadSet.Select(r => r.Id))); return Task.CompletedTask;
        });
        await Case("full replacement excludes entire older chain", () => { using var f = new Fixture(); Requirement a = R("REQ-TEST-001"), b = R("REQ-TEST-002"), c = R("REQ-TEST-003"); b.Supersedes = [a.Id]; c.Supersedes = [b.Id]; f.Save([a,b,c]); Check(new RequirementStore(f.Root).Resolve("test").ReadSet.Single().Id == c.Id); return Task.CompletedTask; });
        await Case("duplicate ID rejected", () => InvalidCatalog(r => r.Add(r[0])));
        await Case("missing reference rejected", () => InvalidCatalog(r => r[0].Related = ["REQ-NO-001"]));
        await Case("invalid status rejected", () => InvalidCatalog(r => r[0].Status = "Done"));
        await Case("path escape rejected", () => InvalidCatalog(r => r[0].Path = "../escape.md"));
        await Case("combined override cycle rejected", () => InvalidCatalog(r => { r[0].Supersedes = [r[1].Id]; r[1].Amendments = [new(r[0].Id, "cap")]; }));
        await Case("missing scope rejected", () => InvalidCatalog(r => r[1].Amendments = [new(r[0].Id, "")]));
        await Case("Active amendment cannot adopt a Proposed contract", () => InvalidCatalog(r => { r[0].Status = "Proposed"; r[1].Amendments = [new(r[0].Id,"cap")]; }));
        await Case("partial test tree total requires actual individual terminal evidence", () =>
        {
            using var d = JsonDocument.Parse("{\"structuredContent\":{\"result\":{\"Summary\":{\"Status\":\"Passed\",\"TotalTests\":1100,\"PassedTests\":1,\"FailedTests\":0,\"SkippedTests\":0},\"Results\":[{\"Name\":\"fixture.test\",\"Status\":\"Passed\"}]}}}"); Check(McpClient.TestVerdict(d.RootElement).Contains("1/1 executed")); return Task.CompletedTask;
        });
        await Case("raw Unity test receipts are readable and failed results never pass", () =>
        {
            using var raw = JsonDocument.Parse("{\"Summary\":{\"Status\":\"Passed\",\"TotalTests\":1,\"PassedTests\":1,\"FailedTests\":0,\"SkippedTests\":0},\"Results\":[{\"Name\":\"fixture.pass\",\"Status\":\"Passed\"}]}");
            Check(McpClient.TestVerdict(raw.RootElement).Contains("1/1 executed"));
            using var failed = JsonDocument.Parse("{\"structuredContent\":{\"result\":{\"Summary\":{\"Status\":\"Failed\",\"TotalTests\":1,\"PassedTests\":0,\"FailedTests\":1,\"SkippedTests\":0},\"Results\":[{\"Name\":\"fixture.fail\",\"Status\":\"Failed\"}]}}}");
            Throws(() => McpClient.TestVerdict(failed.RootElement));
            return Task.CompletedTask;
        });
        await Case("proposal and Draft plan creation, duplicate/path protection", () =>
        {
            using var f = new Fixture(); f.Save([R("REQ-TEST-001")]); var s = new RequirementStore(f.Root); var p = s.CreateProposal("REQ-TEST-002", "test", "test", "cap=5"); Check(p.Status == "Proposed"); Check(new RequirementStore(f.Root).Resolve("test").ReadSet.Length == 1); var plan = s.CreatePlan("PLAN-001", p.Id, "verify cap"); Check(File.ReadAllText(s.SafePath(plan)).Contains("Status: Draft")); Throws(() => s.CreateProposal("../escape", "x", "x", "x")); Throws(() => s.Find("REQ-UNKNOWN")); return Task.CompletedTask;
        });
        await Case("stale catalog writer cannot overwrite another instance", () =>
        {
            using var f = new Fixture(); f.Save([R("REQ-TEST-001")]); var first = new RequirementStore(f.Root); var stale = new RequirementStore(f.Root);
            first.CreateProposal("REQ-TEST-002","first","test","first"); Throws(() => stale.CreateProposal("REQ-TEST-003","stale","test","stale")); Check(!File.Exists(Path.Combine(f.Root,"Docs/Requirements/REQ-TEST-003.md"))); Check(new RequirementStore(f.Root).Find("REQ-TEST-002").Title == "first"); return Task.CompletedTask;
        });
        await Case("process preserves exact NUL stdout and stdin arguments", async () =>
        {
            using var f = new Fixture(); var p = await ProcessRunner.RunAsync("python", ["-X", "utf8", "-c", "import sys; print(sys.argv[1],flush=True); sys.stdout.buffer.write((sys.stdin.read()+chr(0)).encode('utf8'))", "中文 name & $(bad)"], f.Root, "line1\nline2"); Check(p.ExitCode == 0, p.Stderr); Check(p.Stdout.Contains("中文 name & $(bad)")); Check(p.Stdout.EndsWith("line1\nline2\0"));
        });
        await Case("cancellation kills descendant process", async () =>
        {
            using var f = new Fixture(); using var c = new CancellationTokenSource(); int childPid = 0;
            var task = ProcessRunner.RunAsync("python", ["-u", "-c", "import subprocess,sys,time; p=subprocess.Popen([sys.executable,'-c','import time;time.sleep(120)']); print(p.pid,flush=True); time.sleep(120)"], f.Root, onOutput: l => { childPid = int.Parse(l); c.CancelAfter(200); }, ct: c.Token);
            try { await task; throw new Exception("Cancel did not throw"); } catch (OperationCanceledException) { }
            Check(childPid != 0); try { Check(System.Diagnostics.Process.GetProcessById(childPid).HasExited); } catch (ArgumentException) { }
        });
        await Case("bounded process capture rejects oversized display rather than truncating silently", async () => { using var f = new Fixture(); await ThrowsAsync(() => ProcessRunner.RunAsync("python",["-c","import sys; sys.stdout.write('x'*4000001)"],f.Root)); });
        await Case("Git unicode/space/rename/staged/untracked and no repo writes", async () =>
        {
            using var f = new Fixture(); await Git(f.Root, "init"); await Git(f.Root, "config", "user.name", "fixture"); await Git(f.Root, "config", "user.email", "fixture@example.invalid");
            File.WriteAllText(Path.Combine(f.Root, "中文 name.txt"), "first\n"); await Git(f.Root, "add", "--", "中文 name.txt"); await Git(f.Root, "commit", "-m", "fixture");
            await Git(f.Root, "mv", "--", "中文 name.txt", "renamed 文.txt"); File.AppendAllText(Path.Combine(f.Root, "renamed 文.txt"), "second\n"); File.WriteAllText(Path.Combine(f.Root,"new 文.txt"), "untracked");
            File.WriteAllText(Path.Combine(f.Root,".git/hooks/watch"),"#!/bin/sh\nprintf triggered > hook-called\n"); await Git(f.Root,"config","core.fsmonitor",".git/hooks/watch");
            var indexBefore = RequirementStore.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git/index"))); var g = new GitService(f.Root); var s = await g.Status(); Check(s.Length == 2); var rename = s.Single(x => x.OriginalPath != null); Check(rename.OriginalPath == "中文 name.txt"); Check((await g.Diff(rename)).Contains("second")); Check((await g.Diff(s.Single(x => x.Status == "??"))).Contains("untracked")); Check(indexBefore == RequirementStore.Hash(File.ReadAllBytes(Path.Combine(f.Root,".git/index")))); Check(!File.Exists(Path.Combine(f.Root,"hook-called")));
        });
        await Case("MCP JSON negotiation/session/curated tool call", async () => { using var m = new McpClient("http://localhost:1234/mcp", new Handler()); await m.Connect(); Check(m.Connected); var r = await m.Call("editor-application-get-state", new { }); Check(!McpClient.Payload(r).GetProperty("IsCompiling").GetBoolean()); });
        await Case("MCP SSE skips unrelated events and matches multiline response", async () => { using var m = new McpClient("http://localhost:1234/mcp", new Handler { Sse = true }); await m.Connect(); Check(McpClient.Payload(await m.Call("editor-application-get-state", new { })).GetProperty("IsCompiling").ValueKind == JsonValueKind.False); });
        await Case("MCP tool isError rejected", async () => { using var m = new McpClient("http://localhost:1234/mcp", new Handler { Error = "tool" }); await m.Connect(); await ThrowsAsync(() => m.Call("editor-application-get-state", new { })); });
        await Case("MCP JSON-RPC error rejected", async () => { using var m = new McpClient("http://localhost:1234/mcp", new Handler { Error = "rpc" }); await m.Connect(); await ThrowsAsync(() => m.Call("editor-application-get-state", new { })); });
        await Case("MCP ID mismatch/closed stream rejected", async () => { using var m = new McpClient("http://localhost:1234/mcp", new Handler { Sse = true, Error = "id" }); await m.Connect(); await ThrowsAsync(() => m.Call("editor-application-get-state", new { })); });
        await Case("MCP remote endpoint forbidden; empty/processing/failed tests not passed", () => { Throws(() => new McpClient("http://example.com/mcp")); using var d = JsonDocument.Parse("{\"structuredContent\":{\"result\":{\"Summary\":{\"Status\":\"Passed\",\"TotalTests\":0,\"PassedTests\":0,\"FailedTests\":0,\"SkippedTests\":0}}}}"); Throws(() => McpClient.TestVerdict(d.RootElement)); using var pending = JsonDocument.Parse("{\"content\":[{\"type\":\"text\",\"text\":\"Processing\"}]}"); Throws(() => McpClient.TestVerdict(pending.RootElement)); return Task.CompletedTask; });
        await Case("Codex success requires terminal event; saves actual session and baseline", async () => await CodexFixture(true));
        await Case("Codex exit zero with no terminal event is failed", async () => await CodexFixture(false));
        await Case("cross-instance workspace write lock", () => { using var f = new Fixture(); using (var locked = WorkspaceLock.Acquire(f.Root)) Throws(() => WorkspaceLock.Acquire(f.Root)); using var available = WorkspaceLock.Acquire(f.Root); return Task.CompletedTask; });
        await Case("plan link uses complete metadata tokens", async () =>
        {
            using var f = new Fixture(); f.Save([R("REQ-TEST-001")]); Directory.CreateDirectory(Path.Combine(f.Root,"Docs/Plans")); File.WriteAllText(Path.Combine(f.Root,"Docs/Plans/PLAN-001.md"), "Status: Active\nRequirements: REQ-TEST-0010\n\n## Purpose\nREQ-TEST-001\n");
            await ThrowsAsync(() => new CodexRunner(new EvidenceStore(Path.Combine(f.Root,"evidence"))).Run("not-started",new RequirementStore(f.Root),"REQ-TEST-001","Docs/Plans/PLAN-001.md","fixture","read-only",null));
        });
        await Case("SSE read timeout covers response body", async () =>
        {
            using var m = new McpClient("http://localhost:1234/mcp",new Handler { Hang = true }) { RequestTimeout = TimeSpan.FromMilliseconds(150) }; await m.Connect(); await ThrowsAsync(() => m.Call("editor-application-get-state",new { }));
        });
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        if ((args.Contains("--live") || args.Contains("--unity-only")) && failed == 0) await Live(root, args.Length > 2 ? args[2] : "codex", !args.Contains("--unity-only"));
        if (args.Contains("--live-write") && failed == 0) await Case("LIVE Codex workspace-write modifies only a temporary fixture", async () =>
        {
            using var f = new Fixture(); f.Save([R("REQ-TEST-001")]); Directory.CreateDirectory(Path.Combine(f.Root,"Docs/Plans")); File.WriteAllText(Path.Combine(f.Root,"Docs/Plans/PLAN-001.md"),"Plan ID: PLAN-001\nStatus: Active\nRequirements: REQ-TEST-001\n"); File.WriteAllText(Path.Combine(f.Root,"AGENTS.md"),"Temporary Karolina verification repository. User authorized creation of marker.txt only. No tests/packages/commits.\n"); await Git(f.Root,"init");
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var e = new EvidenceStore(); var result = await new CodexRunner(e).Run(args[2],new RequirementStore(f.Root),"REQ-TEST-001","Docs/Plans/PLAN-001.md","Create marker.txt in this temporary repository with exactly KAROLINA-WRITE-OK in UTF-8, without a newline. Do not change any other files or run git writes. The requirement and plan metadata explicitly authorize this verification.","workspace-write",null,ct:timeout.Token);
            Check(result.State=="Completed — awaiting review",result.Error); Check(File.ReadAllText(Path.Combine(f.Root,"marker.txt"))=="KAROLINA-WRITE-OK"); Console.WriteLine("Write evidence: "+e.DirectoryFor(result.Id));
        });
        Console.WriteLine($"EXIT RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }
    static async Task AppServerFixture()
    {
        int threadSequence=0,turnSequence=0,responded=0; string currentThread="", currentTurn="";
        void Output(object value) { Console.WriteLine(JsonSerializer.Serialize(value)); Console.Out.Flush(); }
        void Completed(string thread,string turn) => Output(new { method="turn/completed", @params=new { threadId=thread,turn=new { id=turn,status="completed",error=(object?)null } } });
        while(await Console.In.ReadLineAsync() is {} line)
        {
            using var document=JsonDocument.Parse(line);var message=document.RootElement;
            if(!message.TryGetProperty("method",out var m)) {if(++responded==2)Completed(currentThread,currentTurn);continue;}
            string method=m.GetString()!;if(!message.TryGetProperty("id",out var id))continue;
            var p=message.GetProperty("params");object result=new {};
            switch(method)
            {
                case "account/read":result=new {account=new {type="chatgpt",planType="fixture"}};break;
                case "model/list":result=new {data=new[]{new { id="fixture",model="fixture",displayName="测试模型",hidden=false,isDefault=true,defaultReasoningEffort="low",supportedReasoningEfforts=new[]{new{reasoningEffort="low",description="测试"}}}},nextCursor=(string?)null};break;
                case "thread/start":currentThread="fixture-"+Environment.ProcessId+"-"+ ++threadSequence;result=new {thread=new {id=currentThread}};break;
                case "thread/resume":currentThread=p.GetProperty("threadId").GetString()!;result=new {thread=new {id=currentThread}};break;
                case "thread/read":result=new {thread=new {id=currentThread,turns=Array.Empty<object>()}};break;
                case "turn/start":
                    if(Environment.GetEnvironmentVariable("KAROLINA_HANG_TURN_START")=="1") continue;
                    currentTurn="turn-"+ ++turnSequence;result=new {turn=new{id=currentTurn}};
                    if(turnSequence>1)Completed(currentThread,"turn-"+(turnSequence-1));
                    Output(new {method="turn/started",@params=new {threadId=currentThread,turn=new{id=currentTurn}}});
                    if(p.GetProperty("input")[0].GetProperty("text").GetString()!.Contains("POLISH_WRITE"))
                    {
                        string path=Path.GetFullPath("Assets/agent-fixture.cs"); File.WriteAllText(path,"class Fixture { int HP; }\n");
                        Output(new {method="item/completed",@params=new {threadId=currentThread,turnId=currentTurn,item=new {id="patch-fixture",type="fileChange",status="completed",changes=new[]{new{path,diff="@@ -1 +1 @@\n-class Fixture {}\n+class Fixture { int HP; }\n",kind=new{type="update"}}}}}});
                        Output(new {method="item/completed",@params=new {threadId=currentThread,turnId=currentTurn,item=new {id="summary-fixture",type="agentMessage",text=(p.GetProperty("input")[0].GetProperty("text").GetString()!.Contains("SUMMARY_ONLY")?"":"临时夹具为单位增加HP字段；仅协议验证，无Unity行为验收。\n")+"```karolina-review\n"+JsonSerializer.Serialize(new{files=new[]{new{path="Assets/agent-fixture.cs",summary="为单位增加HP字段",whatChanged="为Fixture类新增整型HP字段",why="协议夹具验证",verification="只验证文件回执，不证明Unity行为"}}},DocumentLibrary.Json)+"\n```"}}});
                        Completed(currentThread,currentTurn);
                    }
                    if(p.GetProperty("input")[0].GetProperty("text").GetString() is string prompt && prompt.Contains("```karolina-snapshots"))
                    {
                        var block=System.Text.RegularExpressions.Regex.Match(prompt,@"```karolina-snapshots\s*\r?\n([\s\S]*?)```");
                        using var snapshots=JsonDocument.Parse(block.Groups[1].Value);
                        var explanations=snapshots.RootElement.EnumerateArray().Select(f=>new {path=f.GetProperty("path").GetString(),fingerprint=f.GetProperty("fingerprint").GetString(),summary="配置营地对象的激活状态",whatChanged="将营地对象从停用调整为启用；仅临时fixture冻结差异分析",why="临时测试需要对象启用",verification="未运行Unity，仅协议验证"}).ToArray();
                        Output(new{method="item/completed",@params=new{threadId=currentThread,turnId=currentTurn,item=new{id="narrative-fixture",type="agentMessage",text="冻结资料的说明，未更改作者判断。\n```karolina-review\n"+JsonSerializer.Serialize(new{files=explanations},DocumentLibrary.Json)+"\n```"}}});
                        Completed(currentThread,currentTurn);
                    }
                    if(p.GetProperty("input")[0].GetProperty("text").GetString()!.Contains("APPROVAL"))
                        for(int n=1;n<=2;n++)Output(new {id=900+n,method="item/commandExecution/requestApproval",@params=new {threadId=currentThread,turnId=currentTurn,itemId="approval-"+n,command="fixture command "+n}});
                    if(p.GetProperty("input")[0].GetProperty("text").GetString()!.Contains("MALFORMED"))
                        _=Task.Run(async()=>{await Task.Delay(100);Console.WriteLine("invalid JSON");Console.Out.Flush();for(int n=0;n<1000;n++){File.WriteAllText("fixture-heartbeat.txt",n.ToString());await Task.Delay(15);}});
                    break;
                case "turn/interrupt":result=new {};Output(new {method="turn/completed",@params=new {threadId=currentThread,turn=new{id=currentTurn,status="interrupted",error=(object?)null}}});break;
            }
            Output(new {id=id.Clone(),result});
        }
    }
    static async Task CodexFixture(bool complete)
    {
        using var f = new Fixture(); f.Save([R("REQ-TEST-001")]); Directory.CreateDirectory(Path.Combine(f.Root,"Docs/Plans")); File.WriteAllText(Path.Combine(f.Root,"Docs/Plans/PLAN-001.md"), "Plan ID: PLAN-001\nStatus: Active\nRequirements: REQ-TEST-001\n"); await Git(f.Root,"init");
        // The executable fixture implements the actual JSONL protocol, rather than bypassing the provider.
        string shim = Path.Combine(f.Root, "fixture.exe"); string self = Environment.ProcessPath!;
        // Python is an installed executable; a copied python exe needs its stdlib location, so use a .cmd-free tiny .NET apphost fixture mode via an environment variable.
        var fixtureExe = self;
        Environment.SetEnvironmentVariable("KAROLINA_FIXTURE_MODE", complete ? "complete" : "incomplete");
        try
        {
            var e = new EvidenceStore(Path.Combine(f.Root,"evidence")); var runner = new CodexRunner(e);
            var result = await runner.Run(fixtureExe, new RequirementStore(f.Root), "REQ-TEST-001", "Docs/Plans/PLAN-001.md", "fixture", "read-only", null);
            Check(result.State == (complete ? "Completed — awaiting review" : "Failed")); Check(result.ThreadId == "fixture-thread"); Check(File.Exists(Path.Combine(e.DirectoryFor(result.Id),"events.jsonl")));
        }
        finally { Environment.SetEnvironmentVariable("KAROLINA_FIXTURE_MODE", null); }
    }
    static async Task Live(string root, string executable, bool withCodex)
    {
        var evidence = new EvidenceStore(); var store = withCodex ? new RequirementStore(root) : null; RunRecord? run = null;
        if (withCodex) await Case("LIVE Codex linked requirement/plan read-only execution", async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            run = await new CodexRunner(evidence).Run(executable, store!, "REQ-KAR-002", "Docs/Plans/PLAN-0166_karolina_mvp.md", "只读检查：阅读 REQ-KAR-002 和关联 PLAN-0166，只返回 5 个 MVP 验收要点及明确排除的最终版功能。不要调用 Unity 工具、不要运行测试、不要编辑或删除任何文件。", "read-only", null, l => { if (l.Contains("turn.completed") || l.Contains("turn.failed")) Console.WriteLine(l); }, deadline.Token);
            Console.WriteLine($"Run {run.Id}, thread {run.ThreadId}, state {run.State}, evidence {evidence.DirectoryFor(run.Id)}"); Check(run.State == "Completed — awaiting review", run.Error);
        });
        else { var p = Directory.GetDirectories(evidence.Root).Where(p => File.Exists(Path.Combine(p,"run.json"))).OrderByDescending(p => Directory.GetLastWriteTimeUtc(p)).First(); run = JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(Path.Combine(p,"run.json")),RequirementStore.Json); }
        using var mcp = new McpClient(McpClient.Discover(root));
        await Case("LIVE Unity MCP connect + project identity + Editor", async () => { await mcp.Connect(); await mcp.VerifyProject(root); var r = await mcp.Call("editor-application-get-state", new { }); if (run != null) evidence.Validation(run.Id,"editor-application-get-state",r.GetRawText()); var p = McpClient.Payload(r); Check(!p.GetProperty("IsCompiling").GetBoolean() && !p.GetProperty("IsUpdating").GetBoolean()); Console.WriteLine("Unity tools: " + mcp.Tools.Length); });
        await Case("LIVE Unity MCP refresh + compiler state + Console", async () =>
        {
            var refresh = await mcp.Call("assets-refresh", new { options = "ForceSynchronousImport" }); if (run != null) evidence.Validation(run.Id,"assets-refresh",refresh.GetRawText());
            var state = McpClient.Payload(await mcp.Call("editor-application-get-state", new { })); Check(!state.GetProperty("IsCompiling").GetBoolean());
            var logs = await mcp.Call("console-get-logs", new { logTypeFilter = "Error", maxEntries = 100, includeStackTrace = false }); if (run != null) evidence.Validation(run.Id,"console-get-logs",logs.GetRawText()); var p = McpClient.Payload(logs); Check(p.ValueKind == JsonValueKind.Array); Check(!p.EnumerateArray().Any(l => (l.TryGetProperty("Message",out var msg) ? msg.GetString() : "")?.Contains("error CS",StringComparison.Ordinal) == true)); Console.WriteLine("Console errors captured: " + p.GetArrayLength() + " (historical transport errors retained)");
        });
        await Case("LIVE Unity focused EditMode", async () =>
        {
            var r = await mcp.Call("tests-run", new { testMode = "EditMode", testAssembly = "FrameSyncMoba.FrameSync.Tests", testClass = "CommandTargetTickResolverAdaptiveTests", includePassingTests = true, includeMessages = true, includeStacktrace = false }); if (run != null) evidence.Validation(run.Id,"tests-run",r.GetRawText()); Console.WriteLine(McpClient.TestVerdict(r));
        });
        Console.WriteLine($"FINAL RESULT: {passed} passed, {failed} failed");
    }
    static Requirement R(string id) => new() { Id = id, Title = id, Domain = "test", Domains = ["test"], Status = "Active", Path = "Docs/Requirements/" + id + ".md" };
    static Task InvalidCatalog(Action<List<Requirement>> edit) { using var f = new Fixture(); var r = new List<Requirement> { R("REQ-TEST-001"), R("REQ-TEST-002") }; edit(r); f.Save(r); Throws(() => new RequirementStore(f.Root)); return Task.CompletedTask; }
    static void Check(bool condition, string? message = null) { if (!condition) throw new Exception(message ?? "Assertion failed"); }
    static void Throws(Action action) { try { action(); } catch (Exception) { return; } throw new Exception("Expected failure"); }
    static async Task ThrowsAsync(Func<Task> action) { try { await action(); } catch (Exception) { return; } throw new Exception("Expected failure"); }
    static async Task Case(string name, Func<Task> action) { try { await action(); passed++; Console.WriteLine("PASS " + name); } catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); } }
    static async Task RecoverFixtureLock(GitService git, string path)
    {
        // 系统里其它 Git 客户端的只读轮询也可能触发保守拒绝；只重试本夹具明确创建的旧空锁。
        for (int attempt = 0; attempt < 100; attempt++)
        {
            try { await git.RecoverIndexLock(); return; }
            catch (InvalidOperationException) when (File.Exists(path) && new FileInfo(path).Length == 0 && File.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddMinutes(-1)) { await Task.Delay(100); }
        }
        await git.RecoverIndexLock();
    }
    static async Task Git(string root, params string[] args) { var r = await ProcessRunner.RunAsync("git",args,root); Check(r.ExitCode == 0,r.Stderr); }
    sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "karolina-fixture-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(Path.Combine(Root,"Docs/Requirements"));
        public void Save(IEnumerable<Requirement> entries)
        {
            var list = entries.ToList(); foreach (var r in list) if (!r.Path.StartsWith("..")) File.WriteAllText(Path.Combine(Root,r.Path), $"# {r.Id}\nRequirement ID: {r.Id}\nStatus: {r.Status}\n");
            File.WriteAllText(Path.Combine(Root,"Docs/Requirements/catalog.json"),JsonSerializer.Serialize(new Catalog { SchemaVersion = 1, Entries = list },RequirementStore.Json));
        }
        public void Dispose()
        {
            var resolved = Path.GetFullPath(Root); if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("karolina-fixture-")) throw new InvalidOperationException("Unsafe cleanup");
            foreach (var file in Directory.GetFiles(resolved,"*",SearchOption.AllDirectories)) File.SetAttributes(file,FileAttributes.Normal);
            Directory.Delete(resolved,true);
        }
    }
    sealed class Handler : HttpMessageHandler
    {
        public bool Sse, Hang; public string? Error; private bool initialized;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var d = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = d.RootElement; string method = body.GetProperty("method").GetString()!;
            if (method == "notifications/initialized") { initialized = true; return new(HttpStatusCode.Accepted); }
            if (method != "initialize") { Check(initialized); Check(request.Headers.GetValues("Mcp-Session-Id").Single() == "fixture-session"); Check(request.Headers.GetValues("MCP-Protocol-Version").Single() == "2025-06-18"); }
            long id = body.GetProperty("id").GetInt64(); string result = method switch { "initialize" => "{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{}}", "tools/list" => "{\"tools\":[{\"name\":\"editor-application-get-state\",\"inputSchema\":{\"type\":\"object\"}}]}", _ => Error == "tool" ? "{\"isError\":true,\"content\":[]}" : "{\"structuredContent\":{\"result\":{\"IsCompiling\":false}}}" };
            bool call = method == "tools/call";
            string json = call && Error == "rpc" ? $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":-1,\"message\":\"failed\"}}}}" : $"{{\"jsonrpc\":\"2.0\",\"id\":{(call && Error == "id" ? id+1 : id)},\"result\":{result}}}";
            var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.Add("Mcp-Session-Id","fixture-session");
            if (Hang && call) { response.Content = new StreamContent(new HungStream()); response.Content.Headers.ContentType = new("text/event-stream"); }
            else if (Sse && call) { int split = json.IndexOf(",\"result\"") + 1; response.Content = new StringContent("data: {\"jsonrpc\":\"2.0\",\"method\":\"notification\"}\n\ndata: " + json[..split] + "\ndata: " + json[split..] + "\n\n",Encoding.UTF8,"text/event-stream"); }
            else response.Content = new StringContent(json,Encoding.UTF8,"application/json"); return response;
        }
    }
    sealed class HungStream : Stream
    {
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] b,int o,int c) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default) { await Task.Delay(Timeout.Infinite,ct); return 0; }
        public override void Flush() { } public override long Seek(long o,SeekOrigin origin) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b,int o,int c) => throw new NotSupportedException();
    }
}
