# AI 对话页窗边立绘候选

用户于 2026-10-04 要求对话页增加独立、较清晰的看板娘立绘：坐在窗边望向室外，前方有工作中的笔记本电脑。随后选定 A 为基础，要求偏侧面、在电脑前工作时抬头望向窗外思考，并放在聊天窗口右边。

当前运行图为 [A-side-thinking-no-window-v2.png](A-side-thinking-no-window-v2.png)，按用户最新要求移除实体窗户，人物、思考姿势、电脑和局部光照保留。通过内置 imagegen 对 [A-side-thinking-v1.png](A-side-thinking-v1.png) 作局部修改，原始 RGBA 输出直接复制到内建主题 `assets/chat-portrait-window-a.png`；生成来源和完整提示见 [A-side-thinking-no-window-v2.prompt.json](A-side-thinking-no-window-v2.prompt.json)。v1 与其[生成来源](A-side-thinking-v1.prompt.json)保留用于追溯。眼睛和服装的权威仍为角色 Final。

| 候选 | 文件 | 构图 |
|---|---|---|
| A | [A.png](A.png) | 窗边侧坐，安静克制；电脑位于前侧 |
| B | [B.png](B.png) | 转椅托腮，姿势轻松；游戏编辑器更清楚 |
| C | [C.png](C.png) | 手搭窗台、侧望，衣发更有动势 |

## 三种工作模式

用户要求对话页三种模式分别对应立绘；按已接受的无实体窗户构图派生以下模式素材，沿用全局 Pivot、缩放与右侧预留空间。

| 模式 | 立绘 | 主题资源键 |
|---|---|---|
| 讨论需求 | [抬头思考](A-side-thinking-no-window-v2.png) | chatPortraitDiscuss |
| 制定计划 | [记录节点草稿](mode-plan-v1.png) | chatPortraitPlan |
| 执行任务 | [专注操作电脑](mode-execute-v1.png) | chatPortraitExecute |

计划和执行两图从已选 A 侧面无窗图派生，真实原始 RGBA 保存，提示见 [interaction-art-prompts.json](../interaction-art-prompts.json)。模式只更新立绘，不修改输入内容；切换快时旧加载结果不能覆盖最新模式。缺省或加载失败时使用通用 `chatPortrait`；失败同时提示。只覆盖通用图的旧主题仍在全部模式使用自己的通用图，可单独提供各模式资源。

使用内置 imagegen，以最终三视图、眼睛零件和主视觉为参考生成透明 PNG；原始输出直接保存，没有后期改图。生成提示保存为 [prompts.json](prompts.json)。这些是姿势与构图草稿；眼眸、单层白短袜、服装结构仍以角色 Final 为权威，选定后再统一精细零件。生成素材不是界面截图或验收证据。

接入点为主题 `assets.chatPortrait`，独立于通用 `assets.mascot`，默认指定修订 A。外观设置提供 Pivot X/Y（0%–100%，左/上至右/下）和缩放（25%–200%），控制右侧区域内的对齐与缩放中心，实时预览，保存后按工程保留并可随主题包导出。位置、比例与不透明度也可通过组件 CSS 和 `--k-chat-portrait-*` 变量更换。图层仅在对话页可见，外观设置隐藏主场景并提供独立缩略预览；沿用“显示看板娘”开关。聊天内容为右侧立绘留出独立空间；放大超出此区域的部分裁切。工作区宽度不足 480px 时收起装饰，保留输入和阅读空间。上表 A/B/C 原始稿保留用于追溯。
