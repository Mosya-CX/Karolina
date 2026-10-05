# Karolina 仓库工作约定

本仓库是独立的 Windows 桌面程序。它可以连接任意用户指定的 Unity 工程；运行中的 Unity 工程是测试目标，不是本仓库的一部分。Karolina 的产品需求、计划、规则与事实文档保存在当前连接工程的 `Docs` 目录，不复制进本源码仓库。

## 文档入口

- `<Unity工程>/Docs/requirements`：Karolina 及该工程已接受的目标、技术选择、边界与工程取证。
- `<Unity工程>/Docs/plans`：具体实施、数据流、测试设计和进度；当前执行计划由项目文档目录 `catalog.json` 的 `activePlan` 指向。
- `<Unity工程>/Docs/rules`：项目工程规则与当前架构事实。
- 需求案、计划案与事实的元数据保存在相邻 `.meta.json` 文件；源码仓库不包含产品 `Docs` 目录。

只加载当前任务涉及的文档。附件是需求材料；只有用户明确批准的部分才是执行指令。

## 工程边界

1. `Karolina.Core` 不依赖 WinForms、WebView 或 Unity；Desktop 组合 Core 服务，浏览器 UI 通过本机受令牌保护的 API 访问。
2. 正常启动直接运行 `artifacts/current/Karolina.Desktop.exe`；目标 Unity 工程从本机 `%LOCALAPPDATA%/Karolina/last-project.txt` 读取。除用户明确要求的任务外，索引与连接检查保持只读；不手改 Unity YAML，不自动保存场景，不擅自运行正式构建。
3. 设置不保存 API 密钥或访问令牌。日志和图谱属于用户所选工程的 `.karolina/state`，不要提交到此仓库。
4. 只为明确需求增加依赖；保留独立程序集边界和可替换的 Codex 会话接口。
5. UI 和代码注释使用简体中文；路径、程序集与元数据文件名使用英文。
6. 不提交成功占位、吞异常或跳过的测试。用户要求不使用截图验收；行为验收通过可执行检查、API、原生输入或真实工具回执，不通过截图。
7. 运行 `dotnet build Karolina.sln -c Release` 与相关的定向测试。构建成功不代表用户人工验收通过。

## 启动与测试

- 直接双击 `artifacts/current/Karolina.Desktop.exe` 启动。Windows 桌面快捷方式应直接指向此 EXE，不通过命令解释器；目标工程由本机 `last-project.txt` 记忆。
- `Start-Karolina.ps1` 仅用于开发构建或显式启动工程，不作为用户日常入口。产品文档始终读取连接工程的 `Docs` 目录。
- 解决方案：`Karolina.sln`。
- 前端控制器注册回归：`node --test Karolina.Tests/FrontendRegistrationChecks.test.mjs`；界面修改还需实际加载页面检查启动错误、主题、输入及连接状态，JavaScript 语法检查不能替代。
- 定向测试：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --graph-settings`、`--task-reviews`。
- 完整隔离套件（需传入含 `Docs` 的 Unity 测试工程）：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- <Unity工程根目录>`。
- 真实工程静态图谱测试：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --graph-project <Unity根目录>`。此入口只读扫描 Assets、Packages 和 ProjectSettings，并在测试工程 `.karolina/state` 写入索引快照。
