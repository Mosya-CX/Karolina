# Karolina Live2D 素材拆层与 PSD 制作

## 参考需求

[Karolina Live2D 建模素材](../../requirements/karolina/REQ-KAR-009_live2d-source-art.md)：准备可继续建模的分层 PSD，尽量减少用户手动画图。角色视觉以已接受的正面原画为准。

## 工具与运行方式（2026-10-04）

用户检查首轮 PSD 后认为手工切层质量不够，并指定调查 [See-through](https://github.com/shitagaki-lab/see-through)。此前 `Separation-v001` 到 `Separation-v005` 是脚本按人工指定区域裁切、修脸后自行封装的原型，未获用户认可，不作为交付素材。该尝试表明即使组合图能与原画完全对齐，也不能证明语义分层正确。

改用 See-through 处理原画。项目自称能从单张动漫图生成经过补绘的语义层、推断图层顺序并导出 PSD，最多 23 层，包括头发、脸、眼睛、服装和配件。它更贴合本需求的自动拆层和遮挡区补绘；结果仍须检查，之后还需按 Live2D 形变要求调整。

用户选择完全在线处理，以免本机安装模型和占用显存。优先使用上游列出的中国大陆 ModelScope 演示，不下载 Python 推理依赖或模型权重。

## 环境与运行方式调查

- 用户批准的输入：`Design/KarolinaCharacter/Live2D/Candidates/karolina-front-master-v001.png`（1024×1536）。
- 不在本机安装 See-through，不占本机模型存储或显存；使用官方 README 列出的 ModelScope 在线演示。
- 已准备上传输入 `Design/KarolinaCharacter/Live2D/OnlineInput/karolina-seethrough-square-input-v001.png`：将批准的 1024×1536 原画原尺寸居中放进 1536×1536 白底画布，两侧各加 256 px 白边，不缩放、不裁切，适配可能要求方图的演示。
- 图像仍未上传。自动审查阻止开启单独 Edge 窗口；需通过网站操作一次“上传、提交、下载”后检查结果。在线推理需将图像送至 ModelScope 服务。
- 当前输入方图保存在 `Design/KarolinaCharacter/Live2D/OnlineInput`。用户选择在线处理，但尚未在服务上提交原画。
- 本路线不安装第三方包，不触碰 Unity 依赖。在线演示地址为 `https://modelscope.cn/studios/ljsabc/See-Through`。

## 实施步骤

1. 用方图上传素材提交至官方 ModelScope 演示。
2. 下载 See-through 输出的 PSD 和中间结果。
3. 对照原画逐层审查脸、前发、眼睛、手、外套、裙摆、袜子与手机表情。若首轮有显著误切，重跑或调整工具参数；不回退到手工按坐标裁片。
4. 独立检查通道、层名、顺序及合成外观；保留工具原始输出和推理信息。
5. 导入 Cubism 5.3.04 确认 PSD 文档真实存在，再确定素材验收与后续绑定。

## 进度与证据

- [x] 准备：需求、See-through 官方能力、依赖、显存和存储已调查。
- [ ] 执行：用户已选择在线路线；方图已准备，在线提交/下载等待完成。
- [ ] 测试：工具运行及实际 PSD 导出后再记录结果。
- [ ] 校正：按语义层和补绘缺陷进入。
- [ ] 验收：由分层预览和 Cubism 实际导入共同确认；首轮原画接受不等于分层接受。
- [ ] 关闭：验收后关闭。

目前没有 PSD 被认定为本计划的最终交付，也没有完成 Unity 修改或编译。图像尚未传至外部服务；用户通过在线演示提交并下载 PSD 后继续验证。

## 参考

- [See-through 官方仓库与推理说明](https://github.com/shitagaki-lab/see-through)
- [ModelScope 在线演示](https://modelscope.cn/studios/ljsabc/See-Through)
- [LayerDiff 3D 模型卡与许可](https://huggingface.co/layerdifforg/seethroughv0.0.2_layerdiff3d)
- [See-through Marigold 模型卡与许可](https://huggingface.co/layerdifforg/seethroughv0.0.1_marigold)
- [Cubism PSD 要求](https://docs.live2d.com/en/cubism-editor-manual/precautions-for-psd-data/)
