# 隐藏图标的位置查询与交互

隐藏图标仍保留原应用的注册和回调，但 `NIS_HIDDEN` 会使其退出 Windows 托盘布局。原生回调转发成功后，应用仍可能因 `Shell_NotifyIconGetRect` 失败而放弃处理事件。

例如 EasyCLIProxyAPI 0.3.28 使用 tray-icon 0.24.1。该库的 Windows 回调先读取图标矩形，取得 `S_OK` 后才派发点击或打开菜单；EasyCLIProxyAPI 的 Windows 主窗口由左键双击事件打开。

- [EasyCLIProxyAPI 0.3.28 的托盘事件](https://github.com/router-for-me/EasyCLIProxyAPI/blob/v0.3.28/src-tauri/src/tray.rs)
- [对应依赖版本](https://github.com/router-for-me/EasyCLIProxyAPI/blob/v0.3.28/src-tauri/Cargo.lock)
- [tray-icon 0.24.1 的 Windows 实现](https://github.com/tauri-apps/tray-icon/blob/tray-icon-v0.24.1/src/platform_impl/windows/mod.rs)

## 桥接行为

对 ReTray 主动转发的隐藏图标交互，桥接层以该次操作的屏幕坐标记录临时锚点，提供给原应用随后的矩形查询。单击、双击、菜单及快捷键共用此处理，匹配原生窗口/ID 或 GUID，不依赖应用名称和进程路径。图标的隐藏状态及原生顺序保持不变。

当前受支持的 Shell 使用 `WM_COPYDATA` 的类型 3 查询矩形：40 字节请求内含签名、操作、32 字节身份数据；操作 2 返回尺寸，操作 1 返回位置。响应遵循 Shell 使用的有符号 16 位坐标编码，包括负坐标显示器。

锚点只在显式交互后的 5 秒内有效，覆盖异步投递到原应用的回调处理。已显示图标及其他查询沿用 Shell 原有处理；交互锁定、注册重建、恢复托管状态及桥接卸载会阻止或清除对应锚点。

## 回归入口

`build.ps1 -Target BridgeRegistrationTests` 验证矩形协议、负坐标、未知身份、畸形请求、超时、交互锁定和注册重建。

`tests/bridge_controls_integration.ps1` 在真实 Shell 中验证窗口/ID 与 GUID、版本 0 与版本 4 的隐藏图标单击、双击及菜单，原应用必须成功调用矩形查询，同时检查图标仍然隐藏。受控应用采用与 tray-icon 相同的矩形查询前置条件。脚本可通过 `-SkipBuild -BridgePath <DLL>` 比较已构建的桥接版本；运行前退出常驻 ReTray。
