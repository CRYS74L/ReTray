# ReTray 图标用途

图形以 `assets/branding` 中已确认的 SVG 为准。每个像素尺寸直接从 SVG 渲染，ICO 包含 16、20、24、30、32、36、40、48、60、64、72、80、96、128、256 像素帧。

| 使用位置 | 资源及显示方式 |
| --- | --- |
| 自身托盘图标 | 无底板彩色标志；用户自定义优先 |
| 窗口、任务栏、切换窗口 | `WindowIcon.ico`，无底板彩色标志；首帧后明确刷新窗口图标 |
| 界面顶栏 | 从彩色 SVG 生成的 WPF 矢量图形 |
| 根目录入口和运行程序 EXE | `AppIcon.ico`，无底板彩色标志 |
| 开始菜单、应用程序列表 | 包清单的 `ReTrayAppList` / `ReTrayTile`，包括 scale、targetsize 和 unplated 资源 |
| 安装包、已安装应用 | 包清单的 `ReTrayStore`，无底板彩色标志 |
| 桌面快捷方式 | `DesktopIcon.ico`，白色底板；独立 Desktop 标识，避免成为 Window 任务栏组的白底图标来源 |
| README | `retray-readme.svg`，白底、细淡灰色描边、居中 |

运行窗口使用 `CRYS74L.ReTray.Window`，桌面快捷方式使用 `CRYS74L.ReTray.Desktop`。
窗口的重启命令仍通过包激活进入同一个 ReTray 应用，桌面标识不创建第二个常驻进程。
窗口重启图标的缓存文件按软件版本命名，不清除系统全局图标缓存。

发布目录根部只有一个 `ReTray.exe` 启动入口。运行依赖集中在 `runtime`，包图像集中在 `Assets`。
入口启动后即退出，界面和托盘管理属于同一常驻进程。此布局不是单文件程序，不能只复制入口 EXE。

验证时应同时检查生成资源、Windows 提取出的应用/EXE/快捷方式图标、运行窗口图标及实际任务栏显示；文件正确不等于 Shell 当前显示一定正确。
