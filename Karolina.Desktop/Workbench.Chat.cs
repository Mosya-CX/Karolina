using System.Text.Json;
using Karolina.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Karolina.Desktop;

public sealed partial class Workbench
{
    private async Task<IResult> Stop(string? explanationId = null, string? expectedRunId = null)
    {
        JsonElement? recoveredTerminal = null;
        await gate.WaitAsync();
        try
        {
            if (explanationId != null && (explainingTask?.Id != explanationId || run?.Id != expectedRunId)) throw new InvalidOperationException("此说明轮次已结束或被替换，未停止其它任务");
            if (!busy) return Results.Json(new { status = run?.State ?? "already-ended" });
            if (activeThread == null || activeTurn == null) throw new InvalidOperationException("正在等待轮次身份，暂时无法中断；工作区锁仍保留");
            string thread = activeThread, turn = activeTurn;
            try { await codex.Request("turn/interrupt", new { threadId = thread, turnId = turn }, app!.Lifetime.ApplicationStopping); return Results.Json(new { status = "requested" }); }
            catch (CodexRpcException)
            {
                var history = await codex.Request("thread/read", new { threadId = thread, includeTurns = true }, app!.Lifetime.ApplicationStopping);
                var terminal = history.GetProperty("thread").GetProperty("turns").EnumerateArray().FirstOrDefault(t => t.GetProperty("id").GetString() == turn);
                if (terminal.ValueKind != JsonValueKind.Undefined && terminal.GetProperty("status").GetString() is "completed" or "interrupted" or "failed")
                {
                    if(terminal.TryGetProperty("items",out var items) && items.ValueKind==JsonValueKind.Array)
                    {
                        var message=items.EnumerateArray().LastOrDefault(i=>i.TryGetProperty("type",out var type)&&type.GetString()=="agentMessage");
                        if(message.ValueKind!=JsonValueKind.Undefined && message.TryGetProperty("text",out var text))finalSummary=text.GetString();
                    }
                    recoveredTerminal=JsonSerializer.SerializeToElement(new {method="turn/completed",@params=new {threadId=thread,turn=terminal}},DocumentLibrary.Json);
                    return Results.Json(new { status = terminal.GetProperty("status").GetString() });
                }
                throw;
            }
        }
        finally { gate.Release(); if(recoveredTerminal is {} terminal)await Finish(terminal,"turn/completed"); }
    }
    private async Task<IResult> Send(ChatInput input)
    {
        await gate.WaitAsync();
        bool ownsRun = false, turnSubmitted = false;
        try
        {
            if (busy) throw new InvalidOperationException("当前轮次尚未结束");
            if(toolCancellation!=null)throw new InvalidOperationException("拓展工具正在执行");
            if (!codex.Connected) throw new InvalidOperationException("请先连接 Codex");
            if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 100000) throw new ArgumentException("指令不能为空且不能超过十万字");
            if (!models.Any(m => m.GetProperty("model").GetString() == input.Model && m.GetProperty("supportedReasoningEfforts").EnumerateArray().Any(e => e.GetProperty("reasoningEffort").GetString() == input.Effort))) throw new ArgumentException("请选择服务支持的模型和思考档位");
            if (input.Access is not ("read-only" or "workspace-write" or "danger-full-access")) throw new ArgumentException("无效访问权限");
            if (BuildPending) throw new InvalidOperationException("Unity构建尚未确认结束，暂停Harness轮次以避免重复Unity调用；请先登记构建结束");
            string context = $"当前关联工程根目录：{root}。文档只保存在此工程Docs中。参考路径以该工程根目录为基准，外部资源以所注明仓库为基准；资料不授予额外写入权限。\n"+WorkflowModes.Context(input.Mode)+"\n\n"+library.ReferenceContext(input.Documents ?? [])+library.ReferenceResources(input.Documents ?? [], input.ResourceSelections)+projectGraph.ReferenceContext(input.GraphNodeIds ?? [])+ProjectGraphTools.AgentInstructions+AgentActivity.FeedbackInstructions;
            WorkbenchSettings? executionSettings = input.Mode == "execute-task" && input.Access != "read-only" ? settingsStore.Read() : null;
            (string Id,Dictionary<string,string> Fingerprints)? explanationTarget=null;
            if(input.Mode=="explain-review")
            {
                if(input.Access!="read-only"||input.ExplanationReviewId==null||input.ExplanationRevision==null)throw new ArgumentException("文件说明必须指定冻结任务版本，并使用只读模式");
                var task=reviews.Get(input.ExplanationReviewId);if(task.Revision!=input.ExplanationRevision)throw new InvalidOperationException("任务已更新，请读取最新版本后补说明");
                context+="\n"+reviews.ExplanationContext(task)+"\n"+ReviewNarratives.Instructions;
                if(task.State is not ("执行" or "待审批"))throw new InvalidOperationException("只有执行或待审批任务可以补充说明");
                var files=task.Files.Where(f=>f.Origin!="human"&&!f.Path.EndsWith(".meta",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(files.Length==0)throw new InvalidOperationException("没有需要补充说明的文件");
                explanationTarget=(task.Id,files.ToDictionary(f=>f.Path,f=>f.Fingerprint));
            }
            if(input.Mode!="execute-task"&&input.Access=="danger-full-access")throw new ArgumentException("讨论需求/制定计划请选择只读或文档写入，不能使用完全访问");
            if (input.ThreadId != null) { lock (chats) if (!chats.Any(c => c.Id == input.ThreadId)) throw new ArgumentException("会话不属于当前工程"); }
            taskLock = WorkspaceLock.Acquire(root);
            ownsRun = true;
            lock (runProgressGate) run = null;
            activeThread = null; activeTurn = null;
            activeReview = null; finalSummary = null;
            explainingTask=explanationTarget;
            if(input.Access != "read-only" && input.Mode == "execute-task")
            {
                if(input.TaskId != null)
                {
                    var task = reviews.Get(input.TaskId); ReviewPlan(task.PlanId);
                    if(task.State is not ("执行" or "待再执行")) throw new InvalidOperationException("先退回任务再执行，审批中的版本不能继续改写");
                    activeReview = task.Id;
                    if(task.State == "待再执行") context += "\n\n人工审批返回执行：\n" + reviews.FeedbackPrompt(task);
                }
                else if(!string.IsNullOrWhiteSpace(input.PlanId))
                {
                    var plan=ReviewPlan(input.PlanId);
                    var open=reviews.List().SingleOrDefault(t=>t.State!="已通过");
                    if(open != null && open.PlanId != plan.Id) throw new InvalidOperationException("已有其它计划的未结束任务");
                    if(open?.State is "待审批" or "待再执行") throw new InvalidOperationException("请从审批页面返回执行原任务");
                    activeReview=(open ?? await reviews.Start(plan.Id,plan.Title,app!.Lifetime.ApplicationStopping)).Id;
                }
                else throw new ArgumentException("写入工程前请选择执行计划，以便记录任务开始基线；普通讨论可使用只读权限");
            }
            lock (runProgressGate)
            {
                run = new RunRecord { Project = root, Prompt = input.Text, Model = input.Model, Sandbox = input.Access, Requirement = string.Join(',', input.Documents ?? []), State = "running", Mode = input.Mode };
                evidence.Save(run);
            }
            if(explanationTarget is {} target) BeginExplanationProgress(target.Id,target.Fingerprints.Count,input.Model,input.Effort);
            if(activeReview != null)
            {
                var task=reviews.Get(activeReview); lock (runProgressGate) { run!.Plan=task.PlanId; run.ReviewId=task.Id; }
                context+=$"\n\n执行任务计划：{task.PlanId}（{task.Title}）。对照方式：{task.BaselineKind}。{task.BaselineDescription} 结束后需人工审批。请在最后简洁总结做了什么、机器/Agent 验证事实及限制，不宣布人工验收通过。\n"+ReviewNarratives.Instructions;
                if (executionSettings?.AutoVerification == true)
                    context += $"\n\n## 本任务机器验证与有限修复策略\n先读取执行计划中的验证要求及其指定场景/测试类，定位本任务适用的机器检查。实现后运行相应工具，读取工具真实终态与输出；若失败，按具体失败定位并修复后重跑。最多进行 {executionSettings.MaxVerificationRepairRounds} 轮“因验证失败而修复并复测”，不能隐瞒失败、伪造输出或超过此数；到上限仍失败就停下并说明。把每次测试/Unity MCP/命令的真实结果、是否执行及无法执行的原因列在最终摘要与逐文件说明中。Unity 未连接、计划无明确测试、当前 Harness 没有相应工具时必须说明未运行；不能把静态阅读、刷新资源或空回执说成测试通过。不得自动发起 Unity 正式构建；机器结果不替代用户人工审批。";
                else context += "\n\n当前设置关闭了自动验证指引；仍应如实说明本轮实际执行过的机器检查、失败与限制，不得宣称未运行的验证通过。";
            }
            lock (chats) journal = new StreamWriter(Path.Combine(evidence.DirectoryFor(run.Id), "events.jsonl")) { AutoFlush = true };
            busy = true;
            activeTurn = null;
            string workDirectory=WorkflowModes.WorkingDirectory(input.Mode,root);
            object policy = input.Access switch { "workspace-write" => new { type = "workspaceWrite", writableRoots = new[] { workDirectory }, networkAccess = false }, "danger-full-access" => (object)new { type = "dangerFullAccess" }, _ => new { type = "readOnly", networkAccess = false } };
            var thread = await codex.Request(input.ThreadId == null ? "thread/start" : "thread/resume", input.ThreadId == null ? (object)new { cwd = workDirectory, model = input.Model, sandbox = input.Access, approvalPolicy = "on-request", config = GraphSessionConfig() } : new { threadId = input.ThreadId, cwd = workDirectory, model = input.Model, sandbox = input.Access, approvalPolicy = "on-request", config = GraphSessionConfig() }, app!.Lifetime.ApplicationStopping);
            activeThread = thread.GetProperty("thread").GetProperty("id").GetString()!; lock (runProgressGate) run!.ThreadId = activeThread;
            if(activeReview != null) { var task=reviews.Get(activeReview); if(task.State=="待再执行")reviews.Resume(task.Id,task.Revision); reviews.RecordRun(task.Id,run.Id,activeThread,"running"); }
            lock (chats)
            {
                var latest = File.Exists(chatsPath) ? JsonSerializer.Deserialize<List<ChatSummary>>(File.ReadAllText(chatsPath), DocumentLibrary.Json) ?? [] : [];
                foreach (var saved in latest) if (!chats.Any(c => c.Id == saved.Id)) chats.Add(saved);
                int index = chats.FindIndex(c => c.Id == activeThread); var item = new ChatSummary(activeThread, index < 0 ? input.Text[..Math.Min(input.Text.Length, 32)] : chats[index].Title, DateTimeOffset.Now);
                if (index < 0) chats.Insert(0, item); else { chats.RemoveAt(index); chats.Insert(0, item); }
                File.WriteAllText(project.StatePath("chats.json.tmp"), JsonSerializer.Serialize(chats, DocumentLibrary.Json)); File.Move(project.StatePath("chats.json.tmp"), project.StatePath("chats.json"), true);
            }
            SaveRun(run);
            UpdateExplanationProgress("等待回执", "指令已准备，正在等待Codex轮次回执");
            turnSubmitted = true;
            var response = await codex.Request("turn/start", new { threadId = activeThread, model = input.Model, effort = input.Effort, summary = "auto", approvalPolicy = "on-request", sandboxPolicy = policy, input = new[] { new { type = "text", text = context + "\n\n用户指令：\n" + input.Text, text_elements = Array.Empty<object>() } } }, app!.Lifetime.ApplicationStopping);
            activeTurn = response.GetProperty("turn").GetProperty("id").GetString();
            UpdateExplanationProgress("分析", "Codex已接收指令，正在分析冻结文件资料");
            return Results.Json(new { threadId = activeThread, turnId = activeTurn, runId = run.Id });
        }
        catch (Exception e)
        {
            if (!ownsRun) throw;
            if (turnSubmitted && e is not CodexRpcException && codex.Connected)
            {
                // 超时不证明服务未受理。保持锁，等待真实 turn 终态，或关闭程序终止进程树。
                UpdateExplanationProgress("回执未知", "启动回执未知，保留锁并等待真实终态；不要重复发送", error:e.Message, persist:true);
                string? unknownRunId = null;
                lock (runProgressGate) { if (run != null) { run.State = "unknown"; run.Error = "启动回执未知，保持工作区锁；等待终态或停止当前轮次。" + e.Message; evidence.Save(run); unknownRunId = run.Id; } }
                if(activeReview != null && unknownRunId != null)reviews.RecordRun(activeReview,unknownRunId,activeThread,"unknown");
                throw;
            }
            UpdateExplanationProgress("失败", "未能启动说明生成", active:false, error:e.Message, persist:true);
            explainingTask=null;
            busy = false; taskLock?.Dispose(); taskLock = null;
            string? failedRunId = null;
            lock (runProgressGate) { if (run != null) { run.State = "failed"; run.Error = e.Message; evidence.Save(run); failedRunId = run.Id; } }
            if(activeReview != null && failedRunId != null) reviews.RecordRun(activeReview,failedRunId,activeThread,"failed");
            lock (chats) { journal?.Dispose(); journal = null; }
            throw;
        }
        finally { gate.Release(); }
    }
}
