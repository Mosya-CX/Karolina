# 当前版本与最小 Git 工作流实施

## 参考需求

- [当前版本与最小 Git 工作流](../../requirements/karolina/REQ-KAR-005_current-version-git-workflow.md)：目标实现、技术方案、边界情况与附录。
- [文档生命周期与工作台交互](../../requirements/karolina/REQ-KAR-004_document-workbench-interaction.md)：目标实现、技术方案、边界情况与附录。

## 实施细节

DocumentLibrary 管理侧车元数据、编号、文件名与正文 hash；语义保存将多行 Markdown 演进附加到正文，计划状态按六阶段图判定。准备可进入执行；执行进入测试；测试进入校正/验收；校正回执行/测试；验收进入校正/关闭。关闭需人工结论并冻结正文/状态。取消结果为关闭计划的 outcome，不另建长期演进。

GitService 调用 GCM 账号列表/浏览器登录，不读取或显示 token。状态合并真实 Git diff 与锁诊断；选择路径通过 UTF8 无 BOM、NUL 输入传给 Git，避免 Windows 命令长度。Commit 内部用 UTF8 NUL 路径输入与 --only，只提交当前勾选文件的最新正文，保护未选既有索引；复制只包含目标，已暂存重命名的旧路径不再传给 add。Push 显式 refspec，Pull 干净且仅快进。History 取最近 100 条提交，提交 diff 禁用外部 diff/textconv。

Workbench API 保留会话令牌和 loopback 限制，Git 写操作持工程锁。Program 的 STA WinForms 容器拥有原生标题栏与 NotifyIcon 托盘，WebView2 Dock Fill 控件渲染客户区；原生最小化进任务栏，标题栏 SC_CLOSE/X 隐藏到托盘，双击/菜单恢复；托盘完全退出才停止 HTTP 并回收拥有的子进程。客户端启动自动连接 Codex/尝试 Unity，失败空闲时每约 15 秒重试；Unity 连接核对工程且有 12 秒截止与退出取消。Git 页约 2 秒轮询，读写子进程均跟随服务退出取消；勾选/说明/历史 diff 在刷新和失败时保留。启动脚本输出到唯一 current 目录，已有窗口必须先完整退出才更新二进制。

一次性迁移保留原始恢复包，按编号与英文功能名移动正文/侧车，同步目录、正文相对链接和当前入口；来源的旧路径属于 provenance，不伪装成仍有效的正文。两份旧验收报告并入对应计划，复核汇总已由各计划 source/evidence/assessment 保存，整合说明归入维护规则。

## 六阶段执行

- [x] 准备：读取当前规则、进程、凭据和索引锁，确认 Git 报错与版本累积。
- [x] 执行：修改文档、Git、窗口与启动路径。
- [x] 测试：机器构建、临时 Git/HTTP/DOM，以及独立只读 Agent 审查。
- [x] 校正：根据本轮人工反馈改自动连接、自动刷新、所选提交、左侧历史和原生托盘，修复独立审查发现的问题并重新测试。
- [ ] 验收：用户实际使用最新窗口，确认审查、Commit/Pull/Push 操作与文档体验。
- [ ] 关闭：人工结论登记后冻结；后续变更用新计划。

## 测试设计

纯 .NET 临时目录夹具验证状态跳转、关闭禁止重启、需求正文演进、引用快照、英文文件名与外部更新。Git 临时仓库验证中文/空格/长路径、真实索引选择、Commit/hash/history/diff、零字节过期锁恢复、非空锁拒绝；重写 GitHub HTTPS 测试 URL 到本机 bare 仓库验证 Push/Pull，无生产远端写入。

本机服务 HTTP 与 Edge DOM 验证 GitHub 本机账号状态、锁提示、提交历史和按钮；窗口以进程/窗口句柄核验最小化与完全退出，不用截图。原生 X 应隐藏但保持服务/任务；真实托盘菜单退出及 API 完全退出应结束所属进程。退出活动 Agent 时进程树结束后释放工程锁。

## 上一轮结果与限制（本轮校正前）

上一轮执行与机器/Agent 测试曾完成并进入人工验收；用户反馈未通过，以下是校正前的证据，不作为本轮新交互的通过声明。

- Release 构建：0 错误、0 警告。
- 核心检查：38/38；真实临时 Git 索引、Commit、明确 Push、快进 Pull、history/diff、长路径和索引锁恢复均覆盖。GitHub 测试 URL 重写到本机 bare 远端，没有向 GitHub 写入测试内容。
- HTTP/DOM 回归：43/43。最新正文读取中位数 14.27 ms，最大 32.60 ms，仅是本机测量。
- 本轮专项：15/15；需求多行正文演进、英文文件名、提交历史 diff、挂起 turn/start 完全退出/写锁释放、真实原生最小化及关闭。窗口关闭约 3–4 秒结束服务；没有截图。
- 文档取证：273 个正文/元数据/catalog 条目，123 需求、132 计划；所有 Docs 物理目录/文件 ASCII，真实本地链接和引用路径零问题。
- 原工程保护：2953 个既有 Unity 文件哈希未变。只核验 Codex 模型/账号真实连接，Unity 处于未开启/连接失败，不宣称 Unity 行为已验收。
- 工程 index.lock 复现为旧零字节残留且没有 Git 进程，安全移到本机恢复位置；索引前后 SHA 相同。残留产生者无可靠记录，未断言是某个工具制造；以后遇到残留通过可见诊断恢复，不吞掉错误。
- GitHub 已识别本机 GCM 账号，远端写权限仍在用户实际 Push 时验证。没有替用户暂存、Commit、Push 或 Pull 本工程。
- 独立只读 Agent 两轮审查闭合，无剩余 P1/P2；审查者不宣称独立重跑机器检查。

机器回执在本机 `Karolina/revision-inventory/current-acceptance.json`、`current-additions.json`、`current-documents.json`。旧轮报告已归入所属计划，不新增当前目录中的独立验收报告。

## 人工验收重点

1. 通过唯一启动入口打开当前程序，确认 Codex/Unity 自动尝试连接。标题栏最小化进任务栏，X 收起到托盘且任务继续；双击托盘恢复，从托盘完全退出后无所属任务残留。
2. 需求演进在正文可读可编辑；英文路径不影响中文标题/模块，信息与关联仍正确。计划的测试、校正和人工验收分别明确。
3. Git 页查看实际 GitHub 账号/上游、文本 diff 和 Commit 历史，勾选五个文件、填说明、直接 Commit，未选既有暂存内容保留，Git 外部改动自动显示；真实生产 Commit/Push/Pull 由用户主动操作，服务器权限失败应清晰可见。

如人工验收发现问题，进入校正后重新执行/测试；确认满意后填写人工结论并关闭。本轮未获得人工通过，因此保持“验收”。


## 旧程序输出处理

自动安全审查拒绝递归删除旧输出，仅返回 blocked by policy。已采用可恢复的归档移动，将 lifecycle、verified 和旧 Desktop/bin 移出工程到本机 Karolina/recovery/retired-build-outputs；工程应用输出仅剩 artifacts/current。启动脚本也改为归档旧输出，不生成 GUID 版本目录。旧归档不是前端可切换版本，也不在工作区程序目录出现。用户若希望永久删除，可在文件管理器删除该本机归档目录。


## 本轮人工反馈校正

人工反馈指出自动连接缺失、Git 刷新和提交选择错误、历史未放左侧及窗口关闭语义不符，本计划从验收进入校正，不另建重复计划。

- [x] 自动连接 Codex / Unity，失败有真实状态与空闲重试。
- [x] Git 自动刷新保留勾选/差异，单一工作文件列表。
- [x] 勾选直接部分 Commit，含已暂存、新增/删除/重命名，未勾选索引保留。
- [x] 左侧提交历史可直接审查。
- [x] WinForms 原生容器、X 收起托盘、恢复、标题栏最小化及托盘退出。
- [x] 临时 Git/HTTP/DOM/原生窗口验证和独立只读审查；无截图。

失败 Commit 不吞错误，刷新不能清空选择。原生容器通过标准 WebView2 控件处理 DPI/布局，完全退出终止仅属于本程序的 WebView2 和 Codex；辅助渲染进程的可恢复异常只报告，不终止 Agent。旧阶段测试数字属于前一轮，不冒充本轮新增行为通过。


## 本轮校正结果与证据

Release 0 错误、0 警告；核心 41/41（新增所选提交/真实 C 复制/重命名删除/无 HEAD/未选索引），HTTP/DOM 回归 43/43；自动连接与 Git 连续交互 14/14，窗口与托盘专项 22/22。窗口专项经 Windows UI Automation 调用真实托盘“完全退出”菜单，验证约 3–5 秒结束窗口与服务；标题栏 X 隐藏后服务保留、托盘双击恢复。只读独立审查闭合，无剩余 P1/P2。

自动 Unity 恢复连接由受控 MCP HTTP 夹具验证，包括真实协议和工程路径核对；实际工程 Unity 服务未开启，不宣称真实 Unity 编译或游戏行为通过。真实 Codex 模型/账号已读取。最近正文快读中位数 3.67 ms、最大 17.77 ms，仅是本机测量。

本轮回执在本机 Karolina/revision-inventory/interaction-regression.json、interaction-native.json、direct-interaction.json 和 current-documents.json；核心以实际控制台结果为准。没有截图，没有生产工程暂存、Commit、Push、Pull，Assets/Packages/ProjectSettings 受保护文件继续按 2953 项原始哈希核查。多显示器 DPI、窗口拖动及实际生产 Git 权限仍由人工使用确认；机器和 Agent 不替用户宣布人工验收通过。


## 原生页面嵌入与键盘输入反馈

用户在人工验收中发现外层 Windows 窗口里仍有浏览器控制，AI 指令和 Git 提交说明无法输入。真实桌面 UI Automation 点击输入框并通过 Windows SendInput 输入后，正文仍为空且输入框 HasKeyboardFocus=false。上一轮 GetParent/DOM 检查只证明句柄层级和独立浏览器页面，不证明真实桌面输入，也不能据此声称没有第二套标题栏。

本轮需验证真实窗口中 AI/Git/正文/搜索的鼠标点击与键盘输入，最大化和恢复后的输入，X 收起与托盘恢复后的焦点，窗口控制只属于外层。禁止截图或用 JavaScript 直接赋值代替键盘验收。WebView2 方案需按工程宪法取得新增 SDK 确认，确认前不加入生产依赖。


### 本轮承载实现

用户已授权自行选择性能、维护及扩展更好的方案，采用官方 WebView2 SDK 1.0.4258.31 并固定版本。移除 EnumWindows/SetParent/修改浏览器样式/手动 ResizeContent/Edge --app 子进程启动。WebView2 Dock Fill 由标准控件管理布局和焦点，Program 保留 STA 与 PerMonitorV2；profile 为 webview-profile-current/工程Hash，options.ExclusiveUserDataFolderAccess=true，核对环境实际 UserDataFolder，禁止共享覆盖路径下的进程被误清理。

初始化及退出保持异步消息泵，退出等待正在进行的初始化后统一释放控件；控件 Dispose 后等待所属 BrowserProcessId 结束，3 秒后才兜底回收该独占进程树。主页面导航限定本次服务 origin，用户发起的新窗口 HTTP(S) 链接交给系统浏览器。可恢复 GPU/utility/unresponsive 等异常只报告；主页面/浏览器真正退出时由用户选择是否关闭，拒绝关闭保留后台 Agent。

新增 SDK 的默认 WPF 引用在 ResolveAssemblyReferences 前移除，本工程仅使用 WinForms/Core，避免真实 WindowsBase 版本冲突，不能用屏蔽警告冒充解决。真实输入检查先在旧程序观察失败，再于新控件观察通过；通过 SendInput Unicode 和 Ctrl+End 发送 Windows 键盘事件，通过 UIA Value/TextPattern 读取正文，不直接修改 DOM。


### WebView2 原生输入专项结果

Release 构建 0 错误、0 警告；核心 41/41，HTTP/DOM 43/43，自动连接和 Git 连续交互 14/14。真实桌面输入及隔离 19/19：鼠标点击后中文/英文输入 AI、Git、正文与目录搜索；正文真实点击保存；最大化、最小化和托盘恢复后继续输入；所有客户区子窗口均无标题栏，窗口控制只属于外层；真实托盘菜单完全退出后所属 WebView2/Codex 进程结束。窗口首次出现即点击 X，不等待页面初始化，收起后继续初始化、托盘恢复输入；同时打开两个不同工程，进程树独立，退出一个后另一个仍可输入并正常退出。

原生输入回执为本机 Karolina/revision-inventory/desktop-input.json，测试仅在临时工程发送键盘事件和保存文档；未发送 AI 执行任务或提交生产 Git。独立只读审查已闭合，修复了可恢复页面辅助进程异常不应终止 Agent、用户数据目录被覆盖时不可误清理共享进程的问题，无剩余 P1/P2。官方 SDK 使用当前机器已有 Runtime，不涉及 Unity 包。

没有截图，未进行性能基准对比，因此不宣称数倍性能提升；维护和扩展改进来自使用标准 WebView2 控件，删除了跨进程浏览器窗口样式、父子关系及焦点拼接。中文输入已通过 Windows Unicode 键盘事件，实际输入法候选窗、多显示器拖动和日常使用仍需用户人工验收。


原生窗口及托盘最新回归 22/22，通过真实菜单退出约 3.41 秒、API 退出约 2.59 秒，窗口与服务均正常结束；这是本次本机观测，不是性能保证。本轮合计 139 项行为/协议检查（41+43+14+22+19），文档保护核验另计。机器/Agent 测试完成，回到人工验收，未关闭计划。


### 当前工程启动入口复核

最终从 Start-Karolina.ps1 启动真实工程，发现旧入口的 WindowStyle Hidden 会令标准 WinForms 首次显示遵循隐藏 STARTUPINFO；旧完整浏览器窗口掩盖了此问题。已将用户交互的桌面窗口启动设为 Normal，后台辅助进程继续隐藏。复核实际 Win32 IsWindowVisible=true、客户区控件已建立、当前工程路径正确、Codex 自动连接并读取 8 个模型、desktopError 为空。回执为本机 Karolina/revision-inventory/current-native-launch.json。实际 Unity localhost:26400 拒绝连接，显示真实失败状态并继续自动重试；未把受控 MCP 测试当成真实 Unity 验收。当前窗口保持打开供人工使用。


## 本计划结束：用户取消 Git 方向

2026-10-02 用户明确取消 Git 产品能力，改由 PLAN-KAR-005 实施任务变更审批。本计划以“被后续方案替代”的取消结果关闭，未登记新版任务审批或全部原功能的人工通过。原构建/检查数字仅为当时程序取证。
