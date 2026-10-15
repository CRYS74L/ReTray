# ReTray：原版机制与兼容性核查

日期：2026-10-11。仅记录本轮已核实的材料及技术判断；不是新软件功能已通过的报告。

## 结论

PS Tray Factory 的问题存在底层兼容性风险，不能只归为几个配置问题：旧式图标枚举依赖、旧结构重建注册、固定图像覆盖及同步外部等待，都不适合作为当前 Windows 11 独立产品的基础。各次真实故障是否都由这些路径触发仍需运行时追踪，尤其不能把微信回调版本问题当作已实测的唯一原因。

新路线采用原生模型读取及保留源身份的显示层定制。规则可以跨会话保存，第三方图标注册记录不能跨会话直接重放。网络、音量、电源等 Windows 11 内置控件另行适配原生对象，避免使用旧 Windows 10 图标补位。

## 原版证据

动态反编译文件：`PS Tray Factory - V3.2 汉化版（逆向副本）/_analysis/recovered/dynamic/ghidra-renamed/PSTRAYFACTORY_dump_SCY.exe.c`。

| 位置 | 直接读到的行为 | 风险与证据边界 |
| --- | --- | --- |
| 115645–115667，FUN_00496ebc | 构造 0x58 字节旧通知结构，flags=7，写 HWND、UID、callback、HICON 及短 ANSI 提示 | 这个结构不包含 GUID、完整状态及版本；是代码证据，尚未证明每一次自定义都会调用此分支 |
| 116618–116659，原条目显示/恢复路径 | 用保存的条目构造通知包，调用 Shell_NotifyIconA(NIM_ADD) 两次 | 重新注册可能复活已经不应出现的条目；窗口仍存活不能证明其历史托盘注册仍有效 |
| 115015–115087，自有图标修改及回调 | 自有代理使用 callback 0x4AD，按旧 mouse lParam 分派 | 这是ReTray自己的代理路径，不能与保留源 owner 的路径混为一谈；代理和现代源回调需分别处理 |
| HKx64Dll_recovered.cpp 的 UpdateMainRecordFromMapping | 替代图标标志启用时把更新记录的图像字段换成固定句柄 | 如果这种处理覆盖空白/off 帧，会破坏闪烁及动态状态；这个 C++ 文件属于行为重建，未证明它逐字等于当前原厂实现 |
| 当前 Taskbar.dll 匹配 PDB | LegacySystrayCompatStubs 的旧注册回调占位路径 | 与实际 RegisterCallback 返回 S_OK/cookie0/entries0 一致；不能继续把这套旧接口作为完整识别后端 |

现代通知协议还要求正确保留增量字段、状态掩码、GUID 身份与通知版本。删除后重新添加会建立新的注册生命周期；源 SETVERSION 不能由恢复工具省略或猜测。菜单事件仍应交给原 owner 处理。[微软 NOTIFYICONDATA](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-notifyicondataw)、[Shell_NotifyIcon](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shell_notifyiconw)

## 当前主机的只读验证

- 主机：Windows 11 10.0.26200，x64；本机 Taskbar.dll 与所用 Microsoft PDB 的加载身份核对通过。
- 原生目录取得 11 条应用通知记录：3 条 promoted、7 条 overflow、1 条真正隐藏。集合及记录的二次读取稳定，11 条 owner HWND 校验通过。
- 识别到了微信、QQ、Google Drive 等实际源图标，包括同 HWND/UID 的 GUID 与非 GUID 两条 Codex 记录。不能只用窗口和 UID 合并两种身份。
- 微信文件版本 4.1.15.53；当前读到源 UID=0、callback=0x8385、version=0。当前微信并不能直接套用“v4 版本被重置”作为已确认原因。
- 读取图像时发现对象字段与旧研究推断不同：+0xC8 为 WinRT 图像对象地址，不能当 HICON；+0x130 为可复制的原始图像缓存，11 条图像已只读验证可用，但它只在首次有效图像时初始化，不能当成动态当前帧。
- 所有 raw tooltip、用户账号元数据、图标 PNG、PDB 和机器地址仅保存在本地 `_analysis/tray-assistant-native-research-20261011/`，不会用作产品仓库中的测试数据。

这些结果证明本机存在可验证的现代原生识别路径。尚未验证显示层定制、排序、隐藏、事件安装/卸载及微信自定义后的实际闪烁和菜单。

对应的原生当前图像 getter、图标 Changed 事件、promoted/overflow 集合变更事件和 MoveNotificationAreaIcon 方法已在匹配符号中定位。它们还需要正确的进程内 ABI 验证；只读取得的远程对象指针不能在ReTray进程直接当成本地 COM/WinRT 对象调用。

## 微信兼容契约

不删除或代为重新注册微信图标，不改变 owner、UID/GUID、callback 和通知版本。原生图标继续走原始菜单处理链；自定义仅作用于显示外观。

没有标准“新消息闪烁”标志可以直接读。通用层保留源动态图像及节奏，不能把 FlashWindowEx 的任务栏按钮闪烁当作托盘通知机制。静态基准帧可以定制；off/透明帧以及未知动态状态默认保持源显示。实际微信版本需要验证来消息、停止闪烁、鼠标右键及菜单动作。[微软 FLASHWINFO](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-flashwinfo)

图像判断按实际 alpha/mask 绘制结果处理，不能把 alpha 全零或借用句柄相等直接当作空白或稳定帧。[微软 DrawIconEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-drawiconex)

## 尚需通过的实现关口

1. 现代 WinRT 当前帧及源更新事件的正确读取，区分缓存原图、当前源帧和自定义展示帧。
2. 保留源身份的原生显示层改动、完整隐藏、原生排序和安全撤销；这些不能靠新建代理图标替代。
3. Windows 11 内置控件的分类与逐项功能验证，不加载旧组件生成替代图标。
4. 在原版和旧补丁不参与的验证环境，证明首次启动/自启/自然重建不会新增旧系统图标。
5. 受控通知源通过协议、闪烁、菜单及资源测试后，再验证微信实际行为。不会自动向他人发送微信消息；真实收消息由用户自然使用或自行触发。

## 研究来源

[SnowDesktop 现代模型读取研究](https://github.com/FreeFallingSnow/SnowDesktop/blob/main/src/system/tray/tray_modern_bootstrap.h) 提供接入方向，GPLv3 代码只存为研究材料，没有复制进产品。具体对象类型和偏移经本机匹配 PDB/反汇编及只读字段复核，已纠正不能直接沿用的图像推断。

[SystemTray WinRT 模型的作者实现](https://github.com/ramensoftware/windhawk-mods/blob/main/mods/system-tray-folders.wh.cpp) 提供另一条现代显示对象路径；它不是新软件对当前版本的功能通过证明。
