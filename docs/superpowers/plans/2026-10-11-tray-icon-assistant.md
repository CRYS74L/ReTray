# 托盘图标助手 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 交付独立的 Windows 托盘管理软件，覆盖设计功能全集，并实际解决刷新丢失定制、自启失效、关机阻塞、漏识别、旧系统图标复活及微信闪烁/菜单失效。

**Architecture:** 原生后台读取当前 Windows 的真实通知模型；最小 Shell 组件只在必要的原生显示及事件路径工作，保留第三方图标注册身份。规则与协议状态独立于按需打开的 WinUI 3 界面；所有生命周期与恢复行为由显式状态机决定。

**Tech Stack:** C++20 / Win32 原生后台及 Shell 组件，C# / WinUI 3 前台，当前用户命名管道，版本锁定的工具链和适配清单。构建工具优先复用本地 LLVM-MinGW/WindowsSDK，并核对已有 .NET/WinUI 工具后再准备缺失项。

**Spec:** ../specs/2026-10-11-tray-icon-assistant.md

**Evidence:** ../../compatibility-research.md

**Execution:** 技术路线已由用户明确委托；采用主会话执行，必要独立研究和最终审查按相应 Skill 使用代理。用户已确认实施计划，连续完成开发与验证，不重复要求用户选择技术路线。

## Global Constraints

- 当前目标主机 Windows 11 10.0.26200 x64；其他版本只有验证后才声明支持。
- 日常仅作用于当前用户、当前会话，后台不依赖 PS Tray Factory 或 Lyrics Tray Icon Fix。
- 自定义保留原 owner、UID/GUID、callback 和通知版本；第三方图标不得删除后代为 NIM_ADD。
- 不注册旧 Windows 10 系统图标补齐 Windows 11 内置控件，不从历史缓存复活外部图标。
- 源动态图像/off 帧及菜单由原应用控制；没有通用的新消息闪烁标志可供猜测。
- 用户已明确授权按需停止当前 PS Tray Factory、Lyrics Tray Icon Fix 以及重启 Explorer；保留停止前状态和可恢复配置。Codex、ChatGPT、Clash Verge 和宿主关机/重启仍遵守原边界。
- Shell 回调不等待外部进程/磁盘；会话查询目标 <50 ms，退出目标 <1 s；性能数字必须实测。
- 新产品在 `托盘图标助手/` 独立目录；旧项目和 _analysis 中的逆向文件、PDB、账号/tooltip、机器快照不进入产品仓库。
- 默认 private 仓库 CRYS74L/Tray-Icon-Assistant，每个有意义的项目提交有唯一 annotated tag 并推送；未完成功能使用 WIP 说明。
- 任何必备功能未完成或未验证，整体状态保持未完成，不用“构建成功”替代验收。

## Review Focus

- 同一个 owner HWND/UID 同时存在 GUID 项和非 GUID 项：两项独立，不互相覆盖规则。
- 原生字段为 WinRT 对象而非 HICON，缓存图像不等于动态当前帧：错误适配拒绝操作，不输出伪造原图。
- 只有 NIF_ICON 的 MODIFY：callback、GUID、tip、version 不被清空或重置。
- 旧式 alpha/mask 图像、透明闪烁帧与状态动画：不能误作垃圾、固定覆盖或“新消息”判断。
- 已收到退出/会话查询后 Explorer 消失，或关机被取消：不能重拉起进程；取消后规则继续正常。

## Shared Interfaces

公共类型在 `native/core/types.h` 定义，所有后续模块使用同一类型。

- `IconIdentity { shellEpoch, ownerPid, ownerCreatedAt, ownerHwnd, uid, hasGuid, guid }`：运行时身份；GUID 与非 GUID 在 hasGuid 上明确区分。
- `NativeIconRecord { identity, callbackMessage, notifyVersion, sourceState, placement, cachedOriginal, currentFrame, sourceRevision }`：缓存原图和当前帧不能互相代替。
- `IconPatch { operation, suppliedFlags, state, stateMask, callbackMessage, notifyVersion, frame, tip }`：没有对应 suppliedFlags 的字段不参与 MODIFY 合并。
- `Frame { width, height, pixelFingerprint, renderedCoverage, origin, revision }`：图像内容与真实渲染信息，不依赖 HICON 数值比较。
- `FrameDecision { Native, CustomIdle, NativeOff, NativeDynamic, AwaitingBaseline }`：默认保留原动态行为。
- `SessionState { Starting, WaitingForShell, Ready, Reconnecting, UserStopping, SessionQuery, SessionEnding }`。
- `OperationResult { success, errorCode, detail, appliedRevision }`：保存规则与实际应用结果分开。
- `SnapshotResult { outcome, complete, shellEpoch, records }`：outcome 为 OperationResult，records 为 NativeIconRecord 列表；complete=false 不允许作为完整快照替换目录。
- `FrameResult { outcome, frame, isLive }`：isLive=false 的缓存图不能通过动态当前帧验收。
- `FrameBaseline { reliable, pixelFingerprint, sourceRevision }`：reliable=false 必须保持原生动态外观，不推测当前帧就是空闲状态。

Task 1 的 SymbolAdapter 定义准确 PE/PDB 身份、关键方法入口及经验证的布局/接口；Task 2 在 `rule_engine.h` 定义 IconRule、FrameBaseline、CatalogDelta；Task 3 定义 BridgeStatus 和 PresentationPolicy；后续设置/窗口/菜单类型在各任务的对应公共 header 或 Contracts.cs 定义，不能复制另一套独立身份格式。

以下文件名均相对于新产品 `托盘图标助手/`；计划与证据文件目前位于外层工作区，创建产品工程时复制经审阅的文档到产品 docs 并更新内部引用，不复制原厂或第三方 GPL 研究实现。

### Task 1: 真实目录与当前帧的原生适配

**Files:** `native/platform/symbol_adapter.h/.cpp`、`native/platform/native_snapshot.h/.cpp`、`native/platform/native_frame.h/.cpp`、`tests/native_snapshot_tests.cpp`、`tests/fixtures/synthetic_native_model.json`、`build.ps1`。

**Interfaces:** 产生 `ReadSnapshot(const SymbolAdapter&, uint32_t explorerPid, uint64_t epoch) -> SnapshotResult` 和 `ReadCurrentFrame(const NativeIconRecord&) -> FrameResult`；SnapshotResult 含完整记录或明确错误，禁止部分失败假装完整成功。

- [ ] 写失败测试：完整/截断载荷、错 PDB、集合变化、owner 失效、双身份、WinRT 指针误作 HICON，分别断言拒绝错误和不合并真实双身份。
- [ ] 运行 `./build.ps1 -Target CoreTests -Filter NativeSnapshot`，确认因功能缺失而失败，保存 RED 证据。
- [ ] 独立实现 PE/RSDS/关键方法验证与稳定读取；由匹配 PDB/指令解析建立适配清单，不硬套来自研究工程的结构推断。核对 legacy stub；区分 +0x130 首图缓存与当前 WinRT 图像。
- [ ] 在受控原生模型中取得连续变化的当前帧，断言像素/修订更新；读取缓存不能通过这项测试。失败时继续研究真实 getter/事件，不降级成文件图标。
- [ ] 重跑测试与全套；实机只读探测包含微信、GUID/非 GUID、promoted/overflow/hidden。证据只存 artifacts，机器元数据不提交。
- [ ] 创建首个有意义的 WIP 提交、v0.1.0 annotated tag、private 仓库并推送。README 明确此时是原生读取能力，控制功能尚未完成。

### Task 2: 增量协议、身份和动态帧规则

**Files:** `native/core/icon_catalog.h/.cpp`、`native/core/notify_patch.h/.cpp`、`native/core/frame_policy.h/.cpp`、`native/core/rule_engine.h/.cpp`、`tests/notify_patch_tests.cpp`、`tests/frame_policy_tests.cpp`、`tests/icon_catalog_tests.cpp`。

**Interfaces:** 消费 Task 1 的记录与公共类型；产生 `ApplyPatch(NativeIconRecord&, const IconPatch&) -> OperationResult`、`ChooseFrame(const Frame&, const FrameBaseline&, const IconRule&) -> FrameDecision`、`MergeSnapshot(IconCatalog&, const SnapshotResult&) -> CatalogDelta`。

- [ ] 写失败测试：仅 NIF_ICON 更新保留 callback/GUID/tip/version；状态按 mask 合并；SETVERSION 与 timeout union 不混读；source DELETE 后偏好仍在但运行实例消失。
- [ ] 写序列测试：idle→off→idle 保持源节奏；A↔B 未知动画和警告帧透传；尚无可信静态基准时 AwaitingBaseline；同 HWND/UID 双身份、PID/句柄复用不串规则。
- [ ] 运行相应 CoreTests，观察失败后实现最小逻辑。原图与展示图独立；有界缓存与资源所有权不依赖借用句柄。
- [ ] 验证 1000 次增量更新后的目录、排序键、选择身份和资源计数；完整套件通过后提交下一版本并推送。

### Task 3: 保留源身份的 Shell 显示与控制

**Files:** `native/shell/bridge_entry.cpp`、`native/shell/native_event_adapter.h/.cpp`、`native/shell/presentation_adapter.h/.cpp`、`native/shell/lifetime_guard.h/.cpp`、`native/test-source/tray_source.cpp`、`tests/shell_bridge_integration.ps1`。

**Interfaces:** 消费目录、规则和 FrameDecision；产生 `ApplyPresentation(const IconIdentity&, const PresentationPolicy&) -> OperationResult`、`RestorePresentation(const IconIdentity&) -> OperationResult`、`AttachBridge(const SymbolAdapter&, uint32_t explorerPid) -> BridgeStatus`、`DetachBridge() -> BridgeStatus`。原始通知消息不替换其身份或 callback。

- [ ] 写受控源：旧式/v4、GUID/非 GUID、多图标、off/动画/徽标帧，记录自身收到的菜单/鼠标/键盘回调及源修订；这些测试目标不涉及用户消息。
- [ ] 写失败集成测试：修改外观前后原 owner/identity/version 不变，动态源帧仍变化；菜单回到源；停用后最新源图正确恢复；外部 ADD/DELETE 调用计数为零。
- [ ] 在隔离的适配 Windows 环境实现局部 Shell 接入及原生显示层变换；应用名单不作为接入范围，只作用于 Explorer 的必要线程/对象路径。
- [ ] 逐项验证完全隐藏、原生排序、提示替换和撤销；不使用代理重新注册，不用助手内部排序冒充原生排序。
- [ ] 测试错误载荷、未知 ABI、后台失联、递归及卸载时序；DLL 未移除所有回调前不能卸载。测 p95 回调耗时 ≤2 ms。
- [ ] 完整套件和受控菜单/帧测试通过后提交下一版本；对应真实控制能力才标为通过。

### Task 4: 后台生命周期、自启与自己的图标

**Files:** `native/agent/main.cpp`、`native/agent/session_lifecycle.h/.cpp`、`native/agent/startup.h/.cpp`、`native/agent/own_icon.h/.cpp`、`native/agent/ipc.h/.cpp`、`tests/session_lifecycle_tests.cpp`、`tests/agent_integration.ps1`。

**Interfaces:** 消费 Shell 操作和目录；产生 `HandleSessionQuery(SessionState&) -> bool`、`HandleSessionEnd(SessionState&, bool ending)`、`ShouldRecover(SessionState) -> bool`、`SetOwnIcon(const OwnIconPolicy&) -> OperationResult`。命名管道请求使用 `catalog.read/rule.apply/ownIcon.apply/agent.stop/status.read`，响应带 requestId、revision 及 OperationResult。

- [ ] 写失败测试：查询立即 TRUE；查询期间不恢复进程；ENDSESSION(FALSE) 回到可用状态；真实结束/用户停止不恢复；Explorer 缺失只在允许运行状态重连。
- [ ] 实现顶层隐藏消息窗口及可取消的事件等待；配置正常编辑时写入，退出没有磁盘/跨进程无限等待。
- [ ] 实现单实例、再次启动前台连接、当前用户登录自启以及 Shell 就绪后读现有目录再同步增量事件；不使用 VBS/无限 Watchdog。
- [ ] 自身图标默认可见，可自定义和关闭；关闭后从再次启动和快捷键打开界面，正确更新/删除自己的图标并保持别人图标原样。
- [ ] 受控测试登录顺序与自然重建；会话查询 <50 ms、退出 <1 s、结束后没有进程拉起。真实关机与取消关机在隔离环境验证，不能用合成消息宣称端到端通过。
- [ ] 完整套件通过后提交下一版本并推送。

### Task 5: 现代 UI、持久配置与真实状态

**Files:** `ui/TrayAssistant.App/`（App.xaml、MainWindow.xaml、Pages/IconsPage.xaml、IconDetailsPage.xaml、SettingsPage.xaml、DiagnosticsPage.xaml）、`ui/TrayAssistant.Shared/`（PipeClient.cs、Contracts.cs、SettingsStore.cs、IconAssetStore.cs）、`tests/ui-config-tests/`、`tests/ui-smoke.ps1`。

**Interfaces:** 消费 Task 4 管道契约；产生 `PipeClient.RequestAsync(Command, CancellationToken) -> CommandResult` 和 `SettingsStore.SaveAsync(SettingsDocument, CancellationToken)`；JSON 契约字段对应公共 native 类型，版本不一致明确报错。

- [ ] 先核对现有 .NET/WinUI 工具，使用 winui-design、winui-dev-workflow 的当前指导。缺失的可信工具准备到 Common 对应目录或本项目工具目录，不覆盖已有版本。
- [ ] 写配置测试：原子保存、损坏恢复、资源复制、迁移冲突、锁定秘密不明文、保存成功但应用失败显示不同状态；观察失败后实现。
- [ ] 实现中文默认、浅/深/跟随系统、四页导航、图标列表和右侧属性、原图/替代图预览、搜索筛选、批量操作、原生排序和撤销。
- [ ] 关闭窗口退出 UI，后台仍运行；自身图标关闭时仍可打开；界面展示真实操作结果和等待/失败/冲突状态，不填模拟图标当真实目录。
- [ ] 检查键盘、高对比度、屏幕阅读器名称、100–200% DPI 和窄窗；实际拖动、图标选择、原菜单和主题切换有效后保存截图证据。
- [ ] 配置测试、受影响交互及构建通过后提交下一版本并推送。

### Task 6: 完整菜单、快捷键和窗口最小化器

**Files:** `native/agent/shortcuts.h/.cpp`、`native/agent/tray_menu.h/.cpp`、`native/agent/window_minimizer.h/.cpp`、`native/core/activity_policy.h/.cpp`、`ui/TrayAssistant.App/Pages/MinimizedAppsPage.xaml`、`tests/window_minimizer_tests.cpp`、`tests/menu_activity_tests.cpp`。

**Interfaces:** 产生 `InvokeOriginal(const IconIdentity&, IconAction, ScreenPoint) -> OperationResult`、`MinimizeWindow(WindowIdentity) -> OperationResult`、`RestoreWindows(RestoreScope) -> OperationResult`、`NextActivityDeadline(const IconRule&, ActivityState) -> Deadline`。

- [ ] 写失败测试：旧式/v4 坐标与事件格式、快捷键冲突、菜单独立显示策略；活动图像与用户访问分别计时；off 帧不会删除目录项。
- [ ] 实现图标点击/双击、快捷面板/悬停延迟、菜单顺序/过滤/显示程序名、始终显示和标记新旧；不修改第三方回调身份。
- [ ] 写窗口恢复测试：指定/最后一个/全部，正常退出恢复助手隐藏窗口，复用 PID/HWND 拒绝恢复，系统结束不重开窗口；实现置顶、自动最小化规则和恢复记录。
- [ ] 实测隐藏图标原菜单、焦点/Esc、左右键交换及各恢复动作；通过后提交下一版本并推送。

### Task 7: Windows 11 内置控件与旧附加功能

**Files:** `native/platform/system_tray_controls.h/.cpp`、`native/platform/taskbar_options.h/.cpp`、`native/core/protection.h/.cpp`、`ui/TrayAssistant.App/Pages/AdvancedPage.xaml`、`tests/system_controls_integration.ps1`、`docs/feature-coverage.md`。

**Interfaces:** 产生 `ReadSystemControls(const SymbolAdapter&) -> SystemControlsResult`、`ApplySystemControlPolicy(SystemControlId, ControlPolicy) -> OperationResult`、`ApplyTaskbarOptions(const TaskbarOptions&) -> OperationResult`、`SetProtection(const ProtectionPolicy&) -> OperationResult`；系统控件身份与应用 GUID/UID 不混用。

- [ ] 写失败验收：首次启动/复核/自然重建不能创建原本不存在的旧系统通知项；系统对象枚举及应用目录互不补造；错误适配时原生行为保持正常。
- [ ] 实现并实测网络/音量/电源/语言/隐私等内置对象的必要原生控制、任务栏透明度、隐藏开始按钮、安全模式及主窗透明/置顶。
- [ ] 实现菜单/设置/交互保护、密码修改、锁定快捷键、配置导入导出及用户选择的跨用户部署作用域；实际 UAC 由用户完成。
- [ ] 按设计功能表逐项记录实现入口、受影响动作、证据及撤销结果；不能以“不支持”或无效开关宣称全功能完成。
- [ ] 全部受影响功能通过后提交下一版本并推送。

### Task 8: 整包验证、微信实测与交付

**Files:** `tests/acceptance.ps1`、`tests/performance.ps1`、`package.ps1`、`README.md`、`docs/verification.md`、`.gitignore`、`.github/workflows/build.yml`。

**Interfaces:** 产生完整可运行包和可复现验证报告；`./tests/acceptance.ps1 -PackageRoot <verified-path>` 失败非零退出，报告列出实际执行/未执行的各项，而不是忽略未测项。

- [ ] 跑完整测试、Release 构建及整包启动，200 图标/100 轮新增删除/1000 更新测性能、资源、身份及定制稳定性；记录真实 CPU、内存、p95 回调和退出时间。
- [ ] 在没有原版和旧补丁参与的环境对比 Windows 11 原生托盘；自启和恢复没有新增旧图标，已存在图标不必重启应用才能识别。
- [ ] 受控帧/菜单通过后，验证本机微信 4.1.15.53 的自定义前后消息闪烁、读取后回稳、右键菜单和实际菜单动作。不会给其他人发送消息；需要真实消息时由用户自然使用或自行触发，一次说明必要验证。
- [ ] 运行最终独立审查，修复有证据的问题并补受影响验证；完整功能、现代对象覆盖、原图当前帧与缓存区别、取消关机和异常退出必须在检查范围内。
- [ ] 打包部署在用户可识别的固定目录，核对版本/组件一致、配置迁移与退出恢复；签名和未测限制如实说明。
- [ ] 扫描暂存区敏感数据、大文件和本地辅助产物；核对仓库 owner/private、工作区、annotated tags 与远程提交。只有所有必备验收通过才创建完成状态提交；否则保留 WIP 和准确未完成项。

## Plan Self-Review

原设计功能表由 Tasks 1–8 覆盖；微信动态/菜单和系统旧图标防复活分别在 Tasks 2/3/8、1/7/8 验证。自身图标、自启与退出在 Task 4；原菜单、排序、最小化器在 Tasks 3/6；完整旧附加功能、保护与部署在 Task 7。Shared Interfaces 的身份、版本和源帧类型统一，研究机器快照不进入 fixture。当前帧、显示层及内置对象的未通过能力被设置为真实实现关口，没有将只读识别成功扩大成全功能成功。

计划阶段未创建产品代码、Git 仓库或版本；实施后按各任务记录 RED/GREEN、实测、提交及剩余限制。
