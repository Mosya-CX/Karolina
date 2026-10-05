# Karolina

Karolina 是一个独立的 Windows 工程工作台，使用 .NET 8、WinForms/WebView2、本机 Codex app-server 和可选的 Unity MCP。仓库包含应用源码、前端资源、测试和界面设计资料。Karolina 的产品需求、计划、规则和项目事实保存在当前连接 Unity 工程的 `Docs` 目录，不保存在源码仓库中。

## 启动

安装 .NET 8 Desktop Runtime、Windows WebView2 Runtime，并先在 Codex 中完成登录。构建 Release 后，直接双击仓库内的 `artifacts/current/Karolina.Desktop.exe` 即可启动；本机桌面 `Karolina.lnk` 也直接指向这个 EXE。程序从 `%LOCALAPPDATA%/Karolina/last-project.txt` 读取目标 Unity 工程，当前值为 FrameSyncMoba 工程路径。

程序仅绑定本机随机回环端口，并使用单次会话令牌保护 API。工程级设置、任务审批、运行记录与图谱快照写在所选 Unity 工程的 `.karolina/state` 下。

## 功能范围

- **AI 对话**：连接 Codex、选择模型/思考挡位/访问权限，创建与继续对话；提供讨论需求、制定计划、执行任务三种工作模式。
- **需求、计划与规则文档**：浏览和编辑需求案、计划案与规则案；管理标签、需求演进、计划预估与实际资源关联。
- **任务变更审批**：按一次计划冻结 Unity 工程变更，说明 Agent 修改了什么；支持文件差异、单文件通过/驳回、注释、整体通过或退回再执行。
- **工程图谱**：只读索引 C# 声明、可识别的继承/引用，以及 Unity `.meta` GUID 资源关系；用于搜索和对话上下文。它是受限的静态启发式索引，不等同 Roslyn 语义分析或运行时依赖图。
- **验证与修复指引**：任务可以要求 Agent 按计划运行检查，并依据设置最多修复 0–3 轮。此设置写入工作指引，不能强制任意第三方 Harness 遵守；没有结构化真实回执时，界面会显示未确认。
- **统一设置与外观**：按工作模式保存默认模型/挡位及有限修复策略，配置图谱与主题。模型失效会显示提示；设置文件损坏时必须先备份再恢复。
- **拓展工具**：在注册表声明说明、Unity MCP 或外部进程工具，并从工具页调用。

Jev、自动生成/切换对话、ArchitectureGraph、多 Harness MCP 替代、自动化语义文档治理等仍属于后续规划；当前应用不会声称具备这些能力。

## 开发与检查

```powershell
dotnet build Karolina.sln -c Release
dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --graph-settings
dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --task-reviews
dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- <Unity工程根目录>
```

连接工程的 `Docs/README.md` 是产品需求、计划和规则的目录。`THEMES.md` 说明主题包和界面组件扩展；`AGENTS.md` 说明源码仓库工作约定。`Design` 保留本机美术源文件；其中 PSD/PNG 原稿由 `.gitignore` 排除，不会随 GitHub 仓库上传。程序实际使用的资源位于 `Karolina.Desktop/Web` 与 `Karolina.Desktop/Resources`。
