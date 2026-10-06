namespace Karolina.Core;

/// <summary>由已收到的操作事件描述活动，不把命令成功解释为功能验收。</summary>
public static class AgentActivity
{
    public static string PublicSummary(System.Text.Json.JsonElement item)
    {
        if(!item.TryGetProperty("summary",out var summaries)||summaries.ValueKind!=System.Text.Json.JsonValueKind.Array)return "";
        return string.Join('\n',summaries.EnumerateArray().Select(s=>s.ValueKind==System.Text.Json.JsonValueKind.String?s.GetString():s.ValueKind==System.Text.Json.JsonValueKind.Object&&s.TryGetProperty("text",out var text)&&text.ValueKind==System.Text.Json.JsonValueKind.String?text.GetString():null).Where(s=>!string.IsNullOrWhiteSpace(s)));
    }
    public const string FeedbackInstructions = "\n\n面向用户的反馈：用简体中文简洁说明本轮理解的目标、当前在做什么、判断依据摘要、下一步及限制；在查阅资料前先说明理解，长任务关键阶段更新。用户测试既有功能时，先明确对应资料里的功能和验收目标，资料不足时询问，不猜测用户要测什么。不要复述内部CLI、shell命令、协议JSON或原始工具输出，除非用户明确索取。思考只提供可公开的简洁摘要，不输出私有推理链，不把推测写成进度，不编造百分比或测试成功。";
    public static (string Stage,string Message) CommandPurpose(string command)
    {
        if(System.Text.RegularExpressions.Regex.IsMatch(command,@"\b(rg|grep|find|Get-Content|cat|sed|Select-String|Get-ChildItem)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return("查阅资料","正在查找或读取项目资料");
        if(System.Text.RegularExpressions.Regex.IsMatch(command,@"\b(dotnet\s+test|node\s+--test|npm\s+test|pytest)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return("机器检查","正在运行相关检查，等待真实结果");
        if(System.Text.RegularExpressions.Regex.IsMatch(command,@"\b(dotnet\s+build|npm\s+run\s+build|msbuild)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return("编译检查","正在检查编译结果");
        if(System.Text.RegularExpressions.Regex.IsMatch(command,@"\bgit\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase))return("核对变更","正在核对仓库记录或文件变化");
        return("执行操作","正在处理项目资料或执行辅助操作");
    }
    public static string TestVerdict(System.Text.Json.JsonElement item)
    {
        if(!item.TryGetProperty("status",out var status)||status.GetString()!="completed")throw new InvalidDataException("工具未返回成功终态，不能确认测试通过");
        if(!item.TryGetProperty("result",out var result))throw new InvalidDataException("未附结构化测试回执，不能确认测试通过");
        return McpClient.TestVerdict(result);
    }
}
