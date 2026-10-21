# Store 应用更新后的路径派生 GUID

ReTray 支持 H.NotifyIcon 默认托盘 GUID 随 Store 安装路径更新而变化的应用，包括 Lyricify Fusion。已有的隐藏、替换图标、悬停文字及列表顺序会继续使用，无需重新设置。

匹配要求包家族、资源包和包内程序路径一致，并验证新旧 GUID 均符合 H.NotifyIcon 的默认路径散列算法。普通程序路径、自定义 GUID 和命名的其他托盘图标继续独立匹配。

多个旧版本规则内容相同时，ReTray 沿用同一设置；再次编辑或删除时整理对应的重复规则。内容不同的规则继续显示冲突，不自动覆盖。

旧配置中的程序路径已转成小写时，使用当前程序路径的大小写还原旧路径，再校验散列。若应用同时更改了包内路径或路径大小写，使旧 GUID 无法还原，需重新设置该图标。

算法依据：[H.NotifyIcon 2.1.4 TrayIcon.cs](https://github.com/HavenDV/H.NotifyIcon/blob/v2.1.4/src/libs/H.NotifyIcon/Core/TrayIcon.cs)。默认 GUID 使用 `SHA256(UTF8(ProcessPath + "_"))` 的前 16 字节。

回归命令：`build.ps1 -Target CoreTests`、`build.ps1 -Target AgentVisibilityTests`。覆盖版本、安装盘和架构变化、重复规则的编辑与删除、冲突检测，以及无关 GUID 隔离。
