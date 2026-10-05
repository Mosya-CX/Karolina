# Karolina 外观与组件自定义

## 在界面中使用

在侧边栏打开「统一设置」，切换到「外观与组件」页。修改主题、配色、动效、场景效果、角色开关、背景强度和四个色彩会即时预览；「聊天立绘」提供 Pivot X/Y 与缩放滑块和数值输入，右侧有立绘预览。「保存外观」按工程保存，离开页面或切换主题前会处理未保存预览。「更多设计变量」接受 JSON，点击「预览变量」应用。编辑中的 JSON 不会被异步预览覆盖。

「导出当前主题」下载 ZIP，包含实际资源及变量覆盖。内建主题导出时生成新的自定义 ID。修改 ZIP 后导入，选择并保存。替换同名包需要勾选替换选项，原包先归档。包内资源更新在下次预览时加载，包括同路径 Shader。

可直接导入的最小示例：[ThemeTemplate.zip](Design/ThemeTemplate.zip)。示例只覆盖按钮框、切角和一个图标，其余从内建主题继承。不会修改业务操作。

## 包结构与覆盖顺序

ZIP 根目录必须有 `theme.json`，不额外套一层文件夹。资源路径相对于声明文件；使用普通相对路径、英文 `/` 分隔，不支持绝对路径、`..` 或目录链接。

```json
{
  "schemaVersion": 1,
  "id": "my-karolina",
  "name": "我的 Karolina",
  "tokens": {
    "--k-pink": "#ed7fce",
    "--k-button-cut": "7px"
  },
  "colorModes": { "light": { "--k-text": "#29304e" } },
  "assets": { "brand": "assets/brand.svg" },
  "icons": { "chat": "icons/chat.svg" },
  "components": { "button-frame": "components/button-frame.css" },
  "styles": ["composition.css"],
  "fonts": [{ "family": "My UI", "source": "fonts/ui.woff2", "weight": "400", "style": "normal" }],
  "effect": { "fragment": "effects/night.frag", "intensity": 0.45 }
}
```

所有外观字段都可只提供部分。解析顺序：内建基础变量 → 内建当前配色 → 所选主题变量 → 所选主题当前配色 → 当前工程的用户变量。资源按语义键合并，保留所属包的路径。CSS 在基础组件之后加载；同名 `components` 键替换对应主题扩展文件。主题 CSS、图片、字体、图标和 Shader 均可独立换，业务控制器不读取具体美术文件名。

字体的 `family` 必须同时用于 `--k-font-ui` 或 `--k-font-code`。完整变量表和资源键以 [内建声明](Karolina.Desktop/Web/themes/karolina/theme.json) 为准；字号、布局宽度、动效时长、按钮切角/节点/指示位置都在其中。

## 最小组件与选择器

| 小件 | CSS 文件 / 主要选择器 | 用途 |
|---|---|---|
| 按钮容器 | `styles/primitives/button.css` / `.k-button` | 尺寸、填充、切角、交互 |
| 按钮框 | `button-frame.css` / `.k-button-frame`, `.k-frame-line`, `.k-frame-leading`, `.k-frame-trailing` | 框与两段装饰线；按实际像素绘制，宽按钮不拉大切角 |
| 标签 | `button-label.css` / `.k-label`, `.k-button` | 文字排版 |
| 角节点 | `button-node.css` / `.k-corner-node` | 不对称小节点 |
| 状态三角 | `button-indicator.css` / `.k-state-indicator` | 框内中心朝下的无尾三角；悬停蓝，选中粉 |
| 图标 | `icon.css` / `.k-icon`, `.k-icon-only` | 纯图标按钮与图标尺寸 |
| 面板 | `panel.css` / `.k-panel` | 面板、标题、材质 |
| 输入 | `field.css`, `input.css` | 输入框、选择框、文本域、勾选框 |
| 列表项 | `list-item.css` | 目录、参考资料和任务条目 |
| 分段选项 | `segmented.css` | 模式选择、选中状态 |
| 焦点 | `focus.css` | 键盘焦点与提示 |
| 手机表情 | `phone-face.css` | 圆、线、弧线组合的 Agent 脸 |
| 基础排版 | `markdown.css`, `badge.css`, `dialog.css` | Markdown、徽标、弹窗与抽屉 |

上述文件全部位于 [styles](Karolina.Desktop/Web/styles)。布局、响应式、业务页面、主视觉合成、动效和外观设置也分别保存，入口 `style.css` 只引用样式。包可通过 `components` 的任意语义键挂独立 CSS，也可通过 `styles` 添加整个页面的覆盖。

强文字按钮按 `data-k-group` 分为 `document`、`approval`、`tool`、`connection`，同组共享框与字号，允许不同长宽。新增按钮使用 `data-k-icon="语义键"`；新增组使用 `data-k-group="组名"`，无需进入绘图函数。按钮装饰由单个观察器处理动态节点，尺寸由单个 ResizeObserver 更新。

## 图标语义

| 场景 | 资源键 |
|---|---|
| 主导航 | `chat`, `document`, `plan`, `book`, `approval`, `tools`, `settings`, `theme` |
| 对话 | `chat-add`, `reference`, `attach`, `search`, `send`, `stop`, `execute` |
| 文档 | `read`, `edit`, `save`, `info`, `timeline` |
| 审批 | `compare`, `comment`, `check`, `return`, `refresh`, `meta` |
| 工具 | `add`, `play`, `stop`, `refresh`, `record` |
| 通用 | `clear`, `close`, `menu`, `filter`, `copy`, `external`, `chevron`, `upload`, `download`, `reset` |
| 连接 | `codex`, `unity` |

每个图标独立保存为 SVG；Codex 使用官方站点的 OpenAI 标志 PNG，Unity 使用通用立方体。语义映射在 [icon-registry.js](Karolina.Desktop/Web/ui/icon-registry.js)，不会将字符符号作为图标合同。

## 主视觉、角色与表情

左下角为应用内桌宠活动区：展开“桌宠互动”可摸头、玩球、休息/唤醒、跟随区域内光标、暂停/继续、归位。闲置时小范围走动和随机短暂休息/打招呼，可拖拽、点击回应、方向键移动，Shift 加速。真实任务处理中使用工作姿势并停止闲逛/玩球/跟随，完成时短跳回应；状态文字与几何表情来自实际回执，互动不会改任务状态。窗口隐藏、角色开关关闭、减少/关闭动效或用户暂停时停止自动动画；低/关闭动效仍允许静态姿势和手动拖动，玩球/跟随只在完整动效下可用。键盘焦点在角色上时停止闲逛。休息、跟随、暂停与位置只保留在当前窗口。活动区域与角色尺寸可用 `--k-pet-stage-height`、`--k-pet-width`、`--k-pet-height` 定制。

`assets.desktopPet` 单独替换桌宠基底，`desktopPetWave`、`desktopPetSleep`、`desktopPetWork` 和 `desktopPetWalkA/B` 独立替换回应、休息、工作及两步走路姿势，`desktopPetGround` 可选替换活动区背景。只覆盖 `desktopPet` 或旧 `mascot` 的主题会将自己的基底用于缺省姿势，避免混入默认角色的动作。活动区、地面、角色、阴影、回应、球和气泡是独立小件，样式为 [desktop-pet.css](Karolina.Desktop/Web/styles/primitives/desktop-pet.css)。走路使用两帧交替加 CSS 平移/步伐，其它动作是姿势图配合 CSS 动效，非骨骼动画/Live2D。左右朝向通过翻转实现；代码分别为 pet-controller.js 和 pet-artwork.js。

手机 Agent 从桌宠身体移出，独立的表情特写区在活动区下方，仅显示基础几何脸与状态，不显示名字。`--k-agent-closeup-size` 控制特写大小；外观独立为 [agent-closeup.css](Karolina.Desktop/Web/styles/primitives/agent-closeup.css)。Q 版桌宠已采用用户选定的 B，定位为“俏皮又可爱”；基底素材、历史候选与生成提示见 [桌宠素材](Design/DesktopPet/README.md)。

`assets.background` 为完整主视觉。还支持 `sky`、`city`、`landmark`、`foreground` 四张透明分层图，按该顺序合成；未提供时使用完整背景，默认背景本身已有远近亮暗与室内/室外构图。若改为分层素材，提供一张不重复含各层的 `background` 基底，再配置这些图层。当前默认并非可编辑的三维场景。

`assets.brand` 替换 K 蝶；`assets.mascot` 替换角色透明图；`assets.buttonFrame` 可用一张框图片整体替换 SVG 框。细节定位由 CSS 配合变量覆盖。角色设定与最细零件保存在 [KarolinaCharacter/Final](Design/KarolinaCharacter/Final/README.md)，运行图保存在内建主题，避免将整张设定板当界面图。

背景覆盖对话、需求、计划、规则、审批和拓展工具页，包括已有消息的对话；外观设置抽屉隐藏场景。背景强度为 1 时使用完整原图透明度，额外暗幕归零；内容通过局部玻璃底保持文字可读。

`assets.chatPortrait` 单独提供对话页的角色与笔记本透明图，默认已接入用户选定 A 的侧面思考修订图，已移除实体窗户，透明度 1，不与背景强度绑定，沿用“显示看板娘”开关。来源见 [窗边立绘](Design/WindowPortrait/README.md)。变量 `--k-chat-portrait-width`、`--k-chat-portrait-bottom`、`--k-chat-portrait-inset`、`--k-chat-portrait-opacity` 控制大小、底边、右侧位置和透明度；`--k-chat-portrait-gutter` 控制右侧留白。`--k-chat-portrait-pivot-x` / `--k-chat-portrait-pivot-y` 为百分比，对应图片对齐位置与缩放中心，0% 为左/上，100% 为右/下，默认均为 100%；`--k-chat-portrait-scale` 为倍率，默认 1，界面提供 25%–200%。参数使用既有用户变量保存并随主题包导出，没有额外偏好格式。放大超出预留区域的部分裁切，避免遮挡消息和输入。样式独立保存为 [chat-portrait.css](Karolina.Desktop/Web/styles/primitives/chat-portrait.css)，可通过组件 CSS 单独更换。工作区宽度达到 480px 时为立绘留出空间，更窄时收起装饰；放左侧需要同时覆盖 `left`/`right` 和聊天区域留白。

## 可调整区域与程序图标

对话右侧立绘随实际工作模式切换：`chatPortraitDiscuss`（讨论需求/抬头思考）、`chatPortraitPlan`（制定计划/记录节点草稿）、`chatPortraitExecute`（执行任务/专注电脑）。缺省模式图或加载失败使用通用 `chatPortrait`，失败提示且快速切换不接受旧加载结果。只提供通用图的主题会将自己的通用图用于缺省的全部模式；只覆盖某个模式时其它模式保留基础图。Pivot/缩放与组件替换方式沿用原配置，模式切换不改变聊天草稿；保存的模式在启动恢复时同步立绘。生成来源见 [三种模式立绘](Design/WindowPortrait/README.md)。

目录/工作区、文档目录/正文、审批文件/差异、审批差异高度、对话/输入区分别提供分隔线；拖动调整，方向键微调，Shift 加速，Home 或双击恢复该区域默认，Escape 取消当前拖动。弹窗标题可拖动，右下角可调整尺寸；窗口缩小时保持可操作范围。审批卡片和页面异步显示会触发尺寸重算。

尺寸保存到 `.karolina/state/appearance/layout.json`，与外观设置、聊天草稿独立，切换主题不清除。布局 API 为 GET/POST `/api/layout`；保存携带独立窗口 `writerId` 和递增 `sequence`，退出使用 keepalive，服务忽略同窗口迟到的旧快照。文件只保存尺寸，不持久化传输序号。

程序图标使用 [karolina.ico](Karolina.Desktop/Resources/karolina.ico)，包含 16–256px 多尺寸，由品牌 K 蝶 SVG 几何生成。exe 通过 `ApplicationIcon` 嵌入，原生窗口/任务栏和托盘共享同一资源。修改该资源需要重新构建并完全退出重开应用；主题包不会改变 Windows 原生 exe 图标。生成脚本为 [build_application_icon.py](maintenance/build_application_icon.py)。

Agent 默认用基础几何表达闲置、工作、完成、失败、离线五种状态，不显示名字。状态来自实际连接/任务回执，角色保持克制。可提供 `agentFace` 通用资源，或 `agentIdle`、`agentWorking`、`agentHappy`、`agentError`、`agentOffline` 分状态替换；通过 CSS 调整手机与装饰的关系。

## Shader 与动效

使用 WebGL 1 的 GLSL 片元着色器，无新的渲染依赖。固定顶点着色器的属性为 `aPosition`；片元可使用：

| Uniform | 类型 | 含义 |
|---|---|---|
| `uResolution` | `vec2` | 实际画布像素 |
| `uTime` | `float` | 动画秒数；轻量模式固定 0 |
| `uPointer` | `vec2` | 归一化指针位置，Y 向上 |
| `uIntensity` | `float` | 包内效果强度 0–1 |
| `uPink`, `uCyan`, `uPurple` | `vec3` | 当前主色 |

输出透明 RGBA，叠加在背景上。默认效果是稀疏星点、分层流星和轻微氛围；程序控制画布上限 180 万像素、DPR 上限 1.5、30fps，窗口不可见时暂停。`auto` 与 `shader` 均尝试 Shader 并允许静态回退；`static` 关闭。`motion=system` 遵循系统减少动态效果，`reduced` 固定 Shader 时间，`off` 不渲染 Shader。编译错误和上下文丢失显示状态、保留静态背景。

CSS 动效在独立 `motion.css`，主题能更换时长、缓动和整个文件的覆盖。框内指示、悬停位移、面片转场及主视觉动画都有减少动效分支。

## 状态、导入与开发边界

- 偏好：工程 `.karolina/state/appearance/preferences.json`，不依赖临时 HTTP 端口。
- 用户包：`.karolina/state/appearance/themes/<id>`；被替换的旧包进入 `appearance/archive`。
- 内建包：`Web/themes`；导入不能覆盖内建 ID。
- 只接收资源与声明；不执行包内 JavaScript。上传 64MB、展开 256MB、最多 512 条目；引用文件必须存在。失败不发布半个主题。
- 前端入口 `app.js` 只接线与启动；状态在 `core/context.js`，协议在 `core/api.js`，业务控制器显式接受依赖。共享业务状态仍存在，未声称所有用例都已彻底解耦。
- 一次性迁移脚本 `refactor_visual_frontend.cjs`、`split_visual_styles.py` 不应在当前代码上重复执行。

## 本轮证据

机器与原生输入证据见 [PLAN-KAR-007](Docs/plans/karolina/PLAN-KAR-007_visual-system-customization.md)。视觉是否达到定稿感觉由实际使用确认；DOM 检查、编译和 Shader 成功不代替人工视觉验收。
