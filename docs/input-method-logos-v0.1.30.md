# 输入法标志替换 v0.1.30

## 产品范围

系统图标页“输入法”行的开关左侧增加同款三点按钮。复用现有 FloatingCard、标题、叉号、进入/退出动效、滚动模板和 IconChoiceRow；托盘与输入法编辑器共享同一套 56 DIP 画布、44 DIP 预览、36 DIP 原图/替代图滑块及悬停动效。每个已启用的 TSF 键盘输入法一行，未激活输入法也读取注册资源。切回原图保留已选择的替代文件，各行独立保存，关闭/退出等待尚未完成的文件读取与保存。

仅修改 LanguageSystemTrayIconDataModel 对应标志（id3）。ImeSystemTrayIconDataModel 的转换状态图标（id9）、普通键盘布局和原生按钮/命令不修改；不轮换激活输入法收集资源，也不修改输入法注册信息或 DLL。

## 后台与身份

- TSF EnumProfiles(0) 按 enabled 和 keyboard category 过滤；注册资源依次读取用户/机器与 64/32 位视图。每条使用 LANGID + CLSID + profile GUID 的严格规范身份。
- 持久化 inputMethodIcons 数组，key/asset/index/useReplacement 独立于普通托盘规则。重复、无效身份或越界参数不能保存；原图模式不需要重新读取保留的文件。
- 实际显示身份取自任务栏自己的 Language 数据模型。核验 Taskbar.View.dll CodeView GUID C2DF1C2B-2369-46B7-A8AB-9E6B7B1AB98B、age=1、marker IID、vtable 与 producer 地址后，读取 Shell 选择缓存或其 TextInputProfileManager.ActiveProfile.Id；HSTRING、COM 和图像引用按契约释放。不能用助手/Explorer 线程的 TSF active 标志或仅用语言 ID 猜测前台配置。
- 缓存中的普通布局或未知身份保持原画面，不退回上一输入法。每个真实标志实例独立匹配；不匹配不叠加。
- 替代图为原图同 Grid 的无命中 Image；原图 Source/文字、按钮 DataContext、命令和菜单不改，原图最新画面继续由系统更新。原图透明度的本地值/绑定被记录、更新并恢复；布局和原控件实例重建重新发现目标。没有替代图时释放目标与回调。
- stage/commit/rollback 在桥接里保留旧 ImageSource 强引用。提交失败不依赖旧文件，commit/rollback 不依赖重新发现控件；未完成回滚优先重试并阻止后续 stage 覆盖。暂时失败 3 秒退避，配置相同也会恢复重试。

目前只对本机匹配的 Windows 11 25H2 26200.8246 模块启用写入。未知模块或 ABI 保留系统原画面。

## 验证

- 40 项核心契约、配置回归、20 项系统/输入法 Agent 契约、既有显隐/身份/菜单回归、10 项位图与4项注册回归均通过。
- WPF 既有4项及新编辑器2项回归通过：各行互不影响、原图保留选择、文件选择后的可见滑块同步。构建无警告/错误。
- 独立真实 Explorer 驱动4项通过：源标志像素被替换且 id9 不接收叠加；红图 stage 后销毁原 HICON，rollback 后屏幕回到缓存蓝图；非当前 Google 策略不影响当前微信标志；原图模式移除叠加。Explorer PID 未变。
- 正式包身份启动 v0.1.30，保持用户原窗口尺寸与主题，输入法浮层展示微信与未激活 Google。Windows 原生输入切换面板可打开；正常切换 Google/微信后各自规则自动匹配。测试图已恢复为原图，原有普通托盘外观、提示、显隐、顺序和自启动偏好不改。
- MSIX 打包通过。清理脚本保护 loose MSIX 注册目录，即使应用未运行；注册信息无法核实时不执行自动清理。缺失的本机开发注册目录已由同版本最终内容恢复，并用正式 AUMID 激活验证。

本机图像、原始目录、完整用户配置、PDB 和 UIA 快照仅放在仓库外研究目录或被忽略的 artifacts/publish/dist，不进入提交。

## 复验入口

`build.ps1 -Target SettingsTests / CoreTests / AgentSystemTests / AgentVisibilityTests / BitmapTests / BridgeRegistrationTests`。

`tests/InputMethodUiTests` 与 `tests/TrayAssistant.UiTests` 为真实控件回归。`build.ps1 -Target InputMethodLiveTests` 只编译真实桥接驱动，运行需确保助手已正常退出，并传入适配器、桥接 DLL 和实际标志中心坐标；它不会重启 Explorer，也不修改输入法配置。
