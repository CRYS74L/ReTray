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
任务栏和桌面均直接运行根目录的 ReTray 主程序；主程序在读取配置前确认已安装包的身份。桌面标识不创建第二个常驻进程。
窗口重启图标的缓存文件按软件版本命名，不清除系统全局图标缓存。

当前便携版的发布目录只有 `ReTray.exe`。ICO、字体和全部运行依赖包含在 EXE 中；用户目录不再需要 runtime 和 Assets。
微软运行库内部的 `createdump.exe`、`RestartAgent.exe` 保留原名和功能，通过 .NET 的单文件缓存加载，不是独立部署的 ReTray 后台程序。
便携版采用 Win32 ICO 显示 EXE、任务栏和快捷方式；旧 MSIX 的外部 scale/targetsize PNG 继续保留在源项目中，便携版不依赖这些外部包文件。
用户配置、自定义图标和快捷方式使用的品牌图标在旁边的 Data 中。移植 Data 会重定位内容寻址的图标路径，图像字节保持不变。