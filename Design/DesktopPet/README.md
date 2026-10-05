# Karolina 应用内 Q 版桌宠

左下角原看板娘区改为桌宠活动区域，保留手机 Agent 的真实状态表达，但将其拆成独立几何表情特写区。桌宠身体不再包含手机或“小K”脸，也不增加另一只角色陪伴。

## 已选定 B：俏皮又可爱

用户已选定 [B-flat-sticker.png](B-flat-sticker.png) 为桌宠基底，并明确它的感觉是“俏皮又可爱”。原始透明图直接替换内建主题 `assets/karolina-desktop-pet.png`；独立 Agent 表情特写和活动区域继续使用现有组件。选择与资源哈希见 [selection.json](selection.json)。

用户反馈初版不够 Q 萌，提出参考 DeepSeek 鲸鱼娘 Q 版。参考来源为[鲸鱼娘社区资产库](https://treapgogo.github.io/deepseek-whale-girl/index.html)，仅参考大头、小身与圆润饱满的形态；发色、蝴蝶结、服装和三角网络沿用 Karolina Final，未使用对方角色的鲸尾、女仆服或现成图作为工程素材。

| 候选 | 样板 | 方向 |
|---|---|---|
| A | [A-soft-anime.png](A-soft-anime.png) | 细腻软萌：大头短身体，柔和动漫绘制 |
| B | [B-flat-sticker.png](B-flat-sticker.png) | 扁平表情包：粗线条、色块、圆团形态，轻松俏皮 |
| C | [C-soft-toy.png](C-soft-toy.png) | 软胶玩偶：立体体积、圆润团子比例，简化材质 |

三张样板均移除手机及其 Agent 脸，独立保存，B 已指定为运行基底，A/C 保留作为历史候选。完整内置 imagegen 提示和生成来源保存为 [style-candidates.prompts.json](style-candidates.prompts.json)。原始 RGBA 直接复制，没有后期裁剪、调色或改图；样板不是界面截图或验收证据。

初版 [Karolina-chibi-v1.png](Karolina-chibi-v1.png) 及其[生成提示](Karolina-chibi-v1.prompt.json)保留用于追溯。[过渡素材](Karolina-chibi-v1-no-phone.png)只清除该图手机，保留原比例，不代表新风格定稿，来源见[过渡图提示](Karolina-chibi-v1-no-phone.prompt.json)。

运行接口为 `assets.desktopPet`，地面可用 `assets.desktopPetGround` 独立替换。活动和手机特写均按现有“显示看板娘”开关控制。活动只改变视觉，不修改聊天草稿或任务；没有新增依赖、模型或第三方素材包。

## 动作与互动

活动区下方展开“桌宠互动”：摸头、玩球、休息/唤醒、跟随区域内光标、暂停/继续、归位。任务处理中使用工作姿势并停止闲逛、追球和跟随；完成短跳，出错时提示陪伴。休息、跟随、暂停是当前窗口的本地状态，重开回到默认自由活动。位置可以拖拽或方向键调整，Shift 加速。

| 动作 | 独立原图 | 主题资源键 |
|---|---|---|
| 打招呼、回应、短跳 | [wave.png](Actions/wave.png) | desktopPetWave |
| 休息 | [sleep.png](Actions/sleep.png) | desktopPetSleep |
| 工作 | [work.png](Actions/work.png) | desktopPetWork |
| 两步走路帧 | [walk-a.png](Actions/walk-a.png)、[walk-b.png](Actions/walk-b.png) | desktopPetWalkA、desktopPetWalkB |

五张姿势从批准 B 派生，保持俏皮扁平风，身上无手机或 Agent 脸。走路交替两张图并配合轻微 CSS 步伐、平移；挥手、呼吸、键入和短跳是姿势图加 CSS 动效，非骨骼动画/Live2D。缺省动作使用基底，旧主题仅换基底时不会混入默认角色的姿势。完整提示与来源见 [interaction-art-prompts.json](../interaction-art-prompts.json)，原始 RGBA 原样保存。

用户指出初版睡姿腿部错误，运行中的 sleep.png 已换为 [sleep-fixed-v2.png](Actions/sleep-fixed-v2.png)：重画两条腿的折叠与袜子边界，一条深色长袜、一条单层白短袜。原错误图 [sleep-v1-rejected.png](Actions/sleep-v1-rejected.png) 仅供历史追溯；修图来源与提示见 [sleep-fixed-v2.prompt.json](Actions/sleep-fixed-v2.prompt.json)。

后台、隐藏角色、低/关闭动效或用户暂停时停止自动动作。低/关闭动效仍可显示静态姿势、手动移动和按键回应；玩球与跟随只在完整动效下可用。键盘焦点在角色上时停止闲逛。
