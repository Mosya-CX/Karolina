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
    private async Task<IResult> UnityAction(UnityInput input)
    {
        if(BuildPending)throw new InvalidOperationException("Unity构建尚未确认结束，暂停所有Unity操作");
        if(toolCancellation!=null)throw new InvalidOperationException("拓展工具正在执行，请等待结束");
        if (!await unityGate.WaitAsync(0)) throw new InvalidOperationException("Unity 操作正在执行");
        try
        {
            if(BuildPending)throw new InvalidOperationException("Unity构建尚未确认结束，暂停所有Unity操作");
            if(toolCancellation!=null)throw new InvalidOperationException("拓展工具正在执行，请等待结束");
            if (input.Action == "connect")
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(app!.Lifetime.ApplicationStopping);
                deadline.CancelAfter(TimeSpan.FromSeconds(12));
                unity?.Dispose(); unity = new McpClient(McpClient.Discover(root));
                await unity.Connect(deadline.Token); await unity.VerifyProject(root, deadline.Token);
                unityState = "已连接"; unityError = ""; return Results.Json(new { state = unityState, tools = unity.Tools.Length });
            }
            if (unity == null || !unity.Connected || unityState != "已连接") throw new InvalidOperationException("请先连接并核对 Unity 工程");
            using var guard = WorkspaceLock.Acquire(root);
            string tool; object arguments;
            switch (input.Action)
            {
                case "state": tool = "editor-application-get-state"; arguments = new { }; break;
                case "console": tool = "console-get-logs"; arguments = new { logTypeFilter = "Error", maxEntries = 100, includeStackTrace = false }; break;
                case "compile": tool = "assets-refresh"; arguments = new { options = "ForceSynchronousImport" }; break;
                case "tests":
                    if (string.IsNullOrWhiteSpace(input.Assembly) || string.IsNullOrWhiteSpace(input.Class) || input.Mode is not ("EditMode" or "PlayMode")) throw new ArgumentException("请填写测试程序集、类和模式");
                    tool = "tests-run"; arguments = new { testMode = input.Mode, testAssembly = input.Assembly, testClass = input.Class, includePassingTests = true, includeMessages = true, includeStacktrace = false }; break;
                default: throw new ArgumentException("不支持的 Unity 操作");
            }
            var response = await unity.Call(tool, arguments, app!.Lifetime.ApplicationStopping); var id = Guid.NewGuid().ToString("N"); evidence.Validation(id, tool, response.GetRawText());
            string? verdict = input.Action == "tests" ? McpClient.TestVerdict(response) : null;
            unityError = "";
            unityLastResult = input.Action switch { "tests" => "测试 " + System.Text.RegularExpressions.Regex.Match(verdict!, @"\d+/\d+").Value + " 通过", "compile" => "已刷新，待核查 Console", "console" => "Console " + McpClient.Payload(response).GetArrayLength() + " 条错误", _ => "Editor 状态已读取" };
            return Results.Json(new { tool, response, verdict, evidenceId = id });
        }
        catch (Exception e) { unityError = e.Message; unityLastResult = input.Action == "tests" ? "测试未通过或尚未取得终态" : "操作失败"; if (input.Action == "connect") unityState = "连接失败"; throw; }
        finally { unityGate.Release(); }
    }
}
