# Karolina 架构审查

## 当前结论与范围

2026-10-05按用户提出的七项原则整理Karolina MVP代码。完成说明状态/存储服务、回复完成用例、Codex协议端口与前端纯政策/视图分离；原生窗口、主题和工程文件职责保留。当前是可继续演进的MVP，不能据此认定全部业务已经完全解耦或行为测试通过。

## 当前模块与边界

|模块|职责与依赖|
|---|---|
|Workbench|工程组合入口、运行生命周期、锁与事件；使用ICodexSession协议端口及Core服务|
|Workbench.Contracts / Routes|保留原HTTP合同和JSON字段；映射请求、状态/诊断与本机窗口操作|
|Workbench.Chat / Unity / Tools / Reviews|宿主内的执行、Unity调用、工具和审批接线；仍是同一实例，partial不等于独立服务|
|ReviewExplanationService|唯一拥有不可变说明进度；只依赖IReviewExplanationStore及TimeProvider，拒绝旧RunId及结束后迟到更新|
|JsonReviewExplanationStore|进度JSON读取、工程路径核验、编号匹配及原子落盘；沿用原状态文件与字段|
|ReviewCompletionService|完成执行回复、绑定有写入证明的文件说明、保存冻结差异说明；依赖既有TaskReviewStore，不拥有协议、运行锁或UI事件|
|ICodexSession / CodexConnection|可替换app-server连接边界；具体实现负责stdio RPC与进程。保留Codex协议，不是已经完成跨Harness适配|
|DocumentLibrary / TaskReviewStore|文档及审批快照、来源、意见、批准边界；当前仍是具体文件系统存储|
|IExtensionToolRunner / IUnityToolClient|沿用既有外部/Unity适配器，未再建重复接口；工程互斥与构建pending继续生效|
|Web core/model-policy|模型档位标签、说明低档默认政策；只处理数据，不访问DOM/API/本机存储|
|Web ui/review-progress / tool-operations|纯进度文本及工具卡片视图；时间由调用者给入，视图将调用委托给控制器|
|Web控制器 / 外观系统|控制器管理API、页面状态和错误；主题/原子CSS/表现特效保留，未把业务写入放到视觉模块|
|DesktopWindow|WinForms、WebView2、托盘、窗口退出；不负责审批语义|

依赖方向为Desktop与Web通过明确入口使用Core；Core不引用Desktop或Unity Gameplay。Core目前同时包含领域/应用用例和本地适配器，尚未拆成多个项目。只有实际外部依赖和状态持久化边界增加接口；不为每个类型增加一层接口/工厂。

## 七项原则的实得与限制

|原则|本轮改进|保留的限制|
|---|---|---|
|高内聚低耦合|进度状态、JSON读写、任务完成回复和工具绘制有各自模块及唯一职责|Workbench仍调度共享生命周期，前端context/actions仍跨控制器共享|
|关注点分离|HTTP合同/映射与Chat/Unity流程分别组织；业务完成落库与宿主消息/锁分开；前端政策与绘制分开|宿主partials仍访问同一实例的私有字段；文件拆分不证明全部解耦|
|依赖倒置|协议依赖ICodexSession，进度依赖IReviewExplanationStore；证据与时钟可注入；工具沿用现有端口|文档、审批和Unity宿主仍使用部分具体实现，不宣称统一跨Harness协议|
|清晰边界|状态服务按ReviewId/RunId核对；开始/终态存储，事件在服务锁释放后发布；JSON路径和文件名核验|外部工具只读声明仍不是Windows沙箱，构建pending需要人工确认结束|
|可测试可观测|进度可用内存存储和固定时钟隔离；模型政策/文本是纯函数；后台终态及不支持请求的RPC失败显示事件|本轮未新增/运行行为测试；现有运行日志尚非完整统一可观测平台|
|简单实用|沿用.NET/WinForms/WebView2/Markdown/JSON，无新包、框架、数据库或通用服务总线|保持具体实现以便维护；避免未经真实需求驱动的过度抽象|
|演进式设计|根据真实进度/审批/工具问题提取服务，原API、JSON、资源说明与交互不迁移|第二Harness、Jev、图谱、自有MCP等仍需独立需求与实施，当前未伪造可用|

## 兼容与所有权

- `.karolina/state/explanations/{reviewId}.json`路径及所有原字段保持。启动只恢复一次，无终态Active记录明确中断。说明开始存储失败向上抛出；终态落盘失败在内存与事件中可见，不阻止宿主释放执行锁。
- 服务内没有宿主事件回调，避免持有进度锁时取得聊天/事件锁。快照是不可变记录数组，不暴露内部可变字典。
- 原审批基线、Origin/Decision/Notes、写入证明、冻结哈希及批准规则保留。资源说明不变成原始YAML/二进制视图；人工模型和动画继续排除，.meta永远略过。
- Workbench成功构造后拥有注入的会话并在DisposeAsync释放。证据目录、进度存储与TimeProvider可给入；不自动处置外部持久存储。ICodexSession的JSON消息必须有独立生命周期。
- WorkbenchBuild从实际服务index.html的唯一build标记及文件字节派生版本/哈希，前后端不再重复写版本常量。

## 验证与交付边界

本轮对应[代码质量计划](../../plans/karolina/PLAN-KAR-010_code-quality-refinement.md)。隔离Release编译0警告0错误；独立只读审查已完成：两项P2故障处理缺口修正后复核无剩余P1/P2；诊断记录损坏容错增量也复核通过。6个修改/新增JS模块语法检查通过，58个HTTP路由声明与r3一致。未新增或执行行为测试，没有截图、真实Codex模型任务、Unity构建、启动器调用、生产审批或Git写入。说明保存的权限失败会结束当前进度并释放锁；后台审批同步对损坏JSON等数据错误可见、去重并重试，诊断可独立返回同步失败与记录读取错误。过去PLAN-KAR-007/009的测试数字仅代表当时所审版本，不能作为本轮回归通过的依据。

用户已完全退出r3后，实际bin/Release与artifacts/current均交付architecture-quality-r4；129份Web文件与源码逐字节相同，Core/Desktop程序集在两个输出相同。既有任务审批数据未在本轮代码质量改造中重置。

## 下一步应何时拆分

当新的执行用例导致Workbench生命周期频繁变化时，再提取执行会话服务及专用状态所有者；当需要第二种审批存储或真实故障注入时，再形成具体审批存储端口；第二Harness接入时先明确统一输入/事件合同，再实现转换适配器。当前先保留小而直接的实现和可替换外部边界。
