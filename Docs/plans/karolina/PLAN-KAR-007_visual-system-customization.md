# 前端视觉与可替换主题实施

## 参考需求

[前端视觉、交互与可替换主题系统](../../requirements/karolina/REQ-KAR-008_visual-system-customization.md)全部章节；既有模式和审批合同按 REQ-KAR-007 与 REQ-KAR-006 保持。

## 实施细节

1. 清点现有 Web/API 和定稿图片；保护既有 Assets、Packages、ProjectSettings 与工作区修改。
2. 为 Core 增加主题目录/包/偏好服务，Desktop 接入静态资源与独立外观 API；主题仅写自己的状态子目录。
3. 将 Web 的隐式全局拆为上下文与功能控制器；HTTP、Markdown、图标/按钮/弹窗原子单独管理，保留保存竞态和审批草稿合同。
4. 拆出基础变量、布局、原子组件和业务页面 CSS；制作独立 SVG 图标、B1 K 蝶、工作台场景和透明角色、字体，统一组件状态与轻量动效。
5. 外观设置提供主题包导入/导出、变量编辑和组件预览；渲染器加载 manifest，按键/组件覆盖资源。WebGL Shader 独立加载与生命周期管理。
6. 真实 Release 编译、JS 语法和资源链接检查；临时工程专项测试主题导入/导出/错误/重启恢复及 DOM 基础操作、动效回退。独立只读审查并修复。

## 六阶段与进度

- 准备：已定位批准素材、现有前端和静态 API；基线记录于本机会话工作目录。
- 执行：代码、资源、主题协议和自定义说明已完成。
- 测试：主题核心 15/15、HTTP/DOM 39/39、原生鼠标键盘 14/14 通过；Release 0 错误 0 警告。JavaScript 格式整理逐模块比较 AST，语义保持一致。
- 校正：独立审查发现 Markdown 依赖、保存/导入/导出/变量编辑的异步竞态、主题覆盖顺序、导出变量、动态品牌资源和 Shader 替换缓存问题，均修复并复查，无剩余可报告问题。
- 验收：用户在 Karolina 实际操作与确认视觉；不自动填人工通过。
- 关闭：人工结论明确后处理。

## 机器验证与 Agent 审查设计

主题协议在临时工程核对部分覆盖、版本/ID/路径校验、缺资源与超限拒绝、偏好原子保存、导出重入和工程隔离。DOM 检查所有页面/图标/组件覆盖、主题切换不丢文本、可见焦点、减少动态效果与 WebGL 回退；原生键盘/窗口输入按 RULE-006 单独登记。独立审查聚焦跨模块依赖、草稿/请求竞态、主题资源边界和 Shader 生命周期，不使用截图证明通过。

## 人工验收

启动入口仍是 Start-Karolina.ps1。对话中查看主视觉、选中/悬停三角和手机表情；文档与审批检查文本可读及输入；外观中换主题、编辑变量、导入导出并重开。视觉与真实原生输入最终由用户确认。

## 本轮实际证据

- [HTTP、DOM 与 Shader 回执](PLAN-KAR-007_visual-evidence.json)：39 项；默认/部分/自定义主题、重启端口变化、真实 Shader 编译、关闭动效及错误回退，延迟保存、导入、导出和变量编辑保护。
- [原生鼠标键盘回执](PLAN-KAR-007_native-evidence.json)：14 项；对话、正文编辑保存、目录搜索、角色开关保存、审批摘要/文件意见/整体意见；最大化、最小化恢复与托盘恢复。UIA 仅定位和读值，实际输入来自 Windows 鼠标/键盘，不直接设置 DOM value。
- [原生导出下载回执](PLAN-KAR-007_download-evidence.json)：真实鼠标点击 WebView2 导出按钮，Downloads 中实际 ZIP 可读，声明与请求一致；验证后只删除本次唯一命名的下载文件。
- [源码与工程保护清点](PLAN-KAR-007_source-evidence.json)：资源、模块语法、文档关系、Unity 保护基线与构建结果。
- 核心专项命令：`dotnet run --project Karolina.Tests/Karolina.Tests.csproj -- --appearance`，15 项通过，包括浅色覆盖导出重入、替换归档、边界与版本错误。
- 独立只读审查不运行测试，最终复查没有剩余 P1/P2/P3；上述通过数来自实际工具回执。

实现说明见 [主题指南](../../../THEMES.md)，定稿参考与生成来源见 [视觉资源](../../../Design/VisualAssets/README.md)。默认完整背景是二维插画，支持后续分层替换，当前没有可运行的关卡或三维场景；角色运行图为定稿派生插画，精细眼眸以零件图为权威。Shader 有预算与静态回退，没有性能倍数或 GPU 覆盖率基准。

## 2026-10-04 页面边界、图标与背景补充

- 新增独立面板布局服务和分隔线组件，五个区域可拖动/键盘调整，尺寸独立保存。初始化隐藏页和异步审批页面显示时重算；审批卡片占用高度计入输入区上限；弹窗支持标题拖动与浏览器尺寸握柄，并在视口变化后约束位置。
- 退出保存采用 keepalive，同窗口序号保护防止旧请求覆盖最新尺寸；非法面板名、范围、非有限值与序号可见拒绝。
- 将 K 蝶制作成多尺寸 ICO，嵌入 exe 并设置原生窗口及托盘图标，不再使用默认 Application 图标。
- 背景去掉其他页面的低透明度和整页遮挡；最大强度显示完整原图，文字用局部玻璃底；外观设置页面隐藏背景。
- 对话页增加可替换 `assets.chatPortrait` 接口；[A/B/C 草稿](../../../Design/WindowPortrait/README.md)已保存，用户选定前不自动指定。现有 Final 和其它任务的 Live2D 素材保持不变。
- [协议/DOM 回执](PLAN-KAR-007_panel-background-evidence.json)：23 项，核心布局专项另有 12 项。
- [原生拖动与图标回执](PLAN-KAR-007_panel-native-evidence.json)：11 项；真实 Windows 指针/键盘、重开恢复、150%/125%缩放、exe 与 WM_GETICON 图标比对均通过；图标匹配率各1.0。原生尺寸专项关闭动效使定位稳定，默认动效另有协议/DOM检查；不使用截图。

本补充独立只读复查已闭合；[最新构建](PLAN-KAR-007_followup-build-evidence.json)与[源码清点](PLAN-KAR-007_followup-source-evidence.json)分别保存。

本补充继续归属 PLAN-KAR-007，不替换其它正在执行计划的 catalog activePlan。视觉效果和立绘选择仍由用户确认。

## 窗边立绘选定后的侧面工作姿势修订

用户选择 A 并要求偏侧面、工作时抬头看窗外思考、放聊天窗口右边。生成 [A-side-thinking-v1.png](../../../Design/WindowPortrait/A-side-thinking-v1.png)，原始 alpha 保留，按独立 `assets.chatPortrait` 接入内建主题。原始 A/B/C 与 Final 设定保留；运行图只用本次修订的派生素材。

图层透明度独立于背景强度；角色开关同时控制可见性与留白。工作区容器宽度驱动右侧留白，窄窗口自动收起装饰，聊天输入不因更换主题或角色开关重置。布局样式为独立 chat-portrait.css，可替换位置、尺寸、留白和亮度。

[专项回执](PLAN-KAR-007_chat-portrait-evidence.json)：18 项，实际资源加载、右侧定位、不覆盖消息和输入、背景强度独立、设置隐藏、角色开关、部分主题资源继承、输入保留、其它页面隐藏、窄/宽工作区恢复与原始图一致；未使用截图验收。[Release 编译](PLAN-KAR-007_chat-portrait-build-evidence.json)为 0 警告、0 错误；[源码清点](PLAN-KAR-007_chat-portrait-source-evidence.json)覆盖 21 个 JS 模块、284 份案与 2953 个未变更的 Unity 保护文件。六项授权前端资源已同步到 artifacts/current，内容哈希一致；未重启正在运行的应用，完全退出后重新打开生效。细部视觉以用户后续反馈继续迭代。

## 无窗立绘与 Pivot / 缩放补充

按用户最新指令，使用内置 imagegen 局部移除实体窗户，保存 [v2 原始 RGBA](../../../Design/WindowPortrait/A-side-thinking-no-window-v2.png) 与[提示来源](../../../Design/WindowPortrait/A-side-thinking-no-window-v2.prompt.json)，更新默认主题运行图。外观设置提供 Pivot X/Y 的 0%–100% 输入和缩放 25%–200%，附独立预览。控制代码独立为 portrait-controls.js；参数写入既有 `--k-chat-portrait-*` 用户变量，复用保存、取消、恢复默认、导出和部分主题继承机制。图片的对齐位置和缩放中心使用同一 Pivot，右侧容器裁切溢出的放大部分。

本补充仅修改 Web、设计素材和对应文档，未修改公开 API、偏好序列化合同或 Unity 文件。本轮没有运行行为测试；此前 18 项回执属于 v1，不作为此版交互验收。当前编译结果单独保存在 [编译回执](PLAN-KAR-007_portrait-controls-build-evidence.json)。正在运行的应用不自动重启，前端文件同步后完全退出重开生效。

## Q 版桌宠活动区域与 Agent 特写

左下角拆为桌宠活动区与独立几何表情特写区。新组件 desktop-pet.js 只拥有视觉位置、闲逛与回应定时器，真实状态继续由现有 mascot.js 消费工作台回执。拖动使用指针捕获，边界随 ResizeObserver 重算，键盘方向键可移动；后台、不可见、关闭角色和低/关闭动效时停自动活动，焦点在角色上时不闲逛。独立样式为 desktop-pet.css 与 agent-closeup.css；`assets.desktopPet`、可选 `desktopPetGround` 与尺寸变量复用主题保存/导出接口。旧包仅覆盖 mascot 时仍使用其覆盖资源。

初版用户反馈“不够Q萌”，已按要求出 [A/B/C 风格候选](../../../Design/DesktopPet/README.md)，分别为软萌动漫、扁平表情包、软胶玩偶；候选均无手机或 Agent 脸，正式风格待用户选择。暂用初版清除手机后的过渡图保持活动区可显示，不将过渡比例视为已认可。新增角色按钮明确标为原生控制，组件装饰器跳过该按钮，其键盘/焦点语义保留。

本次仅 Web、设计素材和需求/计划更新，未改 API、C# 或 Unity 资源；未运行行为测试、未使用截图验收。编译与 JS 语法结果另存 [桌宠编译回执](PLAN-KAR-007_desktop-pet-build-evidence.json)。现阶段动作是单张透明图平移和轻微 CSS 步伐/短跳，不宣称已实现逐帧动作序列或 Live2D。

## 桌宠 B 基底选定

用户选定 B 并明确“俏皮又可爱”的定位。将 [B 原始透明 PNG](../../../Design/DesktopPet/B-flat-sticker.png) 原样替换主题 `assets.desktopPet` 对应图片并同步到 artifacts/current；工程基底、用户所指原始生成图与运行资源的 SHA256 一致。角色开关、活动组件与独立 Agent 特写复用现有实现；未改 JS、CSS、API 或 Unity 文件。本次没有运行行为测试，Release 编译真实结果与复制记录见 [选定回执](PLAN-KAR-007_desktop-pet-selection-evidence.json)。正在运行的应用未自动重启，完全退出再打开加载新素材。

## 桌宠互动、动作素材与三模式立绘

用户追加两项目标，继续在本案实现。桌宠分为 pet-controller.js（本地活动状态）与 pet-artwork.js（动作资源与异步切图），旧入口仅转发导出；增加独立挥手、休息、工作、两步走路素材。互动面板提供摸头、玩球、休息/唤醒、跟随区域内光标、暂停/继续、归位；自动动作与真实任务状态、后台/可见性、角色开关及动效配置协调。走路为两帧图交替加 CSS 平移，其它为姿势图加 CSS 动效，非骨骼动画/Live2D。小K独立表情仍由真实任务回执驱动。

工作模式通过既有选择函数发布视觉事件；chat-portraits.js 独立管理模式映射、加载顺序与失败回退，保留原 Pivot/缩放。三图分别是已接受的抬头思考、派生的记录节点草稿和专注操作电脑，均无实体窗户。启动恢复保存模式时可同步视觉，普通用户切换仍遵守任务忙碌时禁止切换。通用图/基底覆盖会同时覆盖该包未提供的模式/姿势，避免旧主题混入默认角色；单项覆盖仍可继承其余资源。

新增 7 张原始 RGBA 直接复制进工程和运行主题，完整提示见 [interaction-art-prompts.json](../../../Design/interaction-art-prompts.json)，各图和资源接口在[桌宠](../../../Design/DesktopPet/README.md)与[立绘](../../../Design/WindowPortrait/README.md)说明中列出。没有新增第三方依赖、C# 或 Unity 资源变更，也未修改正式 API/偏好序列化合同。本轮未运行行为测试、未使用截图验收；源码语法、资源清点、真实编译及运行目录同步记录见 [本轮回执](PLAN-KAR-007_pet-animation-mode-evidence.json)，编译不作为交互验收。正在运行的应用不自动重启，完全退出再打开加载资源。

用户随实施反馈睡姿腿部问题，已用内置 imagegen 修正下半身并同步 sleep.png 与主题 pet-sleep.png；旧图作为 rejected 历史图保留，修订 PNG 和完整提示在桌宠 Actions 下。真实忙碌状态优先于上一轮短暂完成情绪，防止新任务启动后仍显示完成姿势。

## 恢复与边界

不修改 Gameplay、Unity 资产或生产审批记录，不提交 Git。只替换授权的 Karolina 前端实现并追加主题服务/设计资源/文档；主题包不执行脚本。已有工作区改动保留。
