# 定稿参考与运行素材

## 定稿参考

- `Approved_MainVisual.png`：最终室外地标与深空、室内双屏工作台构图参考。
- `Approved_KButterfly_B1.png`：最终 K / 侧切蝴蝶 / 三角面片品牌参考。
- `Approved_Controls.png`：按钮切角、三原色、非对称细节与框内无尾状态三角参考。
- 角色三视图和零件见 [角色定稿](../KarolinaCharacter/Final/README.md)。

这些参考为当前对话定稿生成图的原文件副本，没有截图验收用途。

## 运行素材

前端 [内建主题](../../Karolina.Desktop/Web/themes/karolina/theme.json)引用：

- `assets/workbench-night.png`：从定稿场景制作的无 UI 背景，保持深空/体素城市/室外蓝图无人机地标/暗室内双屏的层次；当前为完整二维插画。
- `assets/karolina-mascot.png`：从角色定稿及眼睛设定制作的透明运行角色；角色手机的动态脸使用独立 SVG 几何覆盖层。
- `assets/k-butterfly.svg`：为 UI 重绘的可编辑三角面片 K 蝶；为二维表达立体感，不是三维模型。
- `icons/*.svg`：独立语义图标，可逐件替换。`icons/codex.png` 来自 [OpenAI 官方开发者网站](https://developers.openai.com/) 的 favicon；品牌依据见 [OpenAI 品牌资源](https://openai.com/brand/)，没有重绘该官方标志。
- `fonts/HarmonyOS_Sans_SC.ttf` 和 `LICENSE.txt`：用户提供的 `E:/EgdeDownLoad/HarmonyOS Sans.zip` 中的简体中文字体与原许可，运行包保留许可。

两张运行插画通过 imagegen 编辑工具制作，原始生成输出分别是 `exec-61dcd5c0-f141-412a-bd94-9429dfac1573.png`、`exec-42105962-3e43-4ac1-a97b-baa4711de774.png`。实际生成提示保存在 [production-prompts.json](production-prompts.json)，复制运行文件时没有裁切、重绘或丢弃透明通道。图标和品牌矢量由 [build_visual_assets.py](../../maintenance/build_visual_assets.py)保存绘制源；修改后会覆盖同名运行图标，维护时应先保存自定义稿。

## 可替换边界

默认完整背景不提供地标的真实三维旋转、可玩的马里奥关卡或自动施工逻辑。分层资源、Shader 和角色表情接口均已开放，后续可以用独立画稿/动画资源替换。当前角色运行图是根据定稿生成的界面插画，精细眼眸仍应以角色零件图为美术权威。实际视觉贴合程度需要在工作台中确认，不能用机器检查代替审美结论。
