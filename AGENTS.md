# Karolina 仓库工作约定

本仓库是独立的 Windows 桌面程序。它可以连接任意用户指定的 Unity 工程；运行中的 Unity 工程是测试目标，不是本仓库的一部分。

## 文档入口

- `Docs/requirements`：Karolina 已接受目标、技术选择、边界与工程取证。
- `Docs/plans`：具体实施、数据流、测试设计和进度；当前执行计划由 `Docs/catalog.json` 的 `activePlan` 指向。
- `Docs/rules`：Karolina 工程规则与当前架构事实。
- 需求案、计划案与事实的元数据保存在相邻 `.meta.json` 文件；不要把 Unity 工程的文档目录混入本仓库。

只加载当前任务涉及的文档。附件是需求材料；只有用户明确批准的部分才是执行指令。

## 工程边界

1. `Karolina.Core` 不依赖 WinForms、WebView 或 Unity；Desktop 组合 Core 服务，浏览器 UI 通过本机受令牌保护的 API 访问。
2. Unity 工程通过启动参数选择。除用户明确要求的任务外，索引与连接检查保持只读；不手改 Unity YAML，不自动保存场景，不擅自运行正式构建。
3. 设置不保存 API 密钥或访问令牌。日志和图谱属于用户所选工程的 `.karolina/state`，不要提交到此仓库。
4. 只为明确需求增加依赖；保留独立程序集边界和可替换的 Codex 会话接口。
5. UI 和代码注释使用简体中文；路径、程序集与元数据文件名使用英文。
6. 不提交成功占位、吞异常或跳过的测试。用户要求不使用截图验收；行为验收通过可执行检查、API、原生输入或真实工具回执，不通过截图。
7. 运行 `dotnet build Karolina.sln -c Release` 与相关的定向测试。构建成功不代表用户人工验收通过。

## 启动与测试

- `Start-Karolina.ps1` 首次启动会要求输入 Unity 工程根目录；也可运行 `./Start-Karolina.ps1 -ProjectPath <Unity根目录>`。
- 解决方案：`Karolina.sln`。
- 定向测试：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --graph-settings`、`--task-reviews`。
- 完整隔离套件（显式传入本仓库作为文档工程）：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- <Karolina仓库根目录>`。
- 真实工程静态图谱测试：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -c Release -- --graph-project <Unity根目录>`。此入口只读扫描 Assets、Packages 和 ProjectSettings，并在测试工程 `.karolina/state` 写入索引快照。
