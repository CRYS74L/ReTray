# ReTray

**Redefine Your Tray — 重新定义你的 Windows 托盘。**

<p align="center"><img src="assets/branding/retray-readme.svg" width="128" alt="ReTray"></p>

ReTray 是 Windows 11 托盘管理工具，可以按应用隐藏、显示或替换图标，修改悬停文字，并保留原应用交互和动态状态。

## 开始使用

下载便携版 `ReTray.exe`，放到可写的固定文件夹，双击运行。无需安装 .NET、注册 MSIX 或导入开发证书。
程序、运行库、字体、图标和第三方依赖均打包在一个 EXE 中。

首次运行会在 EXE 旁边创建 `Data`，保存配置、自定义图标和窗口位置：

```text
ReTray.exe
Data/
```

移动软件时一起移动这两项。升级时先从托盘菜单退出，再替换 EXE，保留原有 `Data`。
不要把软件放在没有写入权限的目录，也不要把自己的 Data 一起发布给其他人。
在界面中指定的普通图标、自身图标和输入法图标都会复制到 `Data/Assets`，按文件内容散列命名并去重；选取 EXE 或 DLL 时复制整份资源文件。修改原始文件不会改变已保存的副本，重新选择后才会更新。
导入 `.trayconfig` 或旧 JSON 配置时，其图标也统一复制到这里；旧 AppData 中已被配置引用的托管图标会复制回 Data 并保留原文件。

日常自动保存的配置是 `Data/settings.json`，`settings.json.bak` 是自动备份；窗口位置另存于 `Data/window-placement.json`。
点击导出生成的 `.trayconfig` 是包含配置及引用图标的备份包，保存在你选择的位置，不是软件运行时直接使用的配置文件。

旧安装版迁移时，先导出 `.trayconfig`，退出旧版，再在便携版中导入。
首次使用的便携目录有独立配置；已有 Data 不会被其他安装版的配置覆盖。

## 能做什么

- 按应用和图标身份保存显示、隐藏、图标替换及提示文字规则。
- 拖动托盘图标列表中的应用名称调整顺序，相邻行会平滑让位；松手自动保存，按 Esc 取消，拖到列表上下边缘可自动滚动。
- 预览原图和替换图，管理输入法图标和受支持的系统托盘元素。
- 导出和导入 `.trayconfig`，将配置引用的自定义图标一起打包。
- 浅色、深色、跟随系统主题；跟随系统读取**默认 Windows 模式**。
- 支持简体中文、繁体中文、英语和日语。
- 点击设置页底部的版本号，可用默认浏览器打开 ReTray 的 GitHub 仓库。
- 可选择随用户登录启动；新配置默认关闭开机启动，主题默认跟随系统。

打开“托盘图标”页面编辑规则，设置会自动保存。普通关闭窗口会收起界面，托盘菜单提供“打开主界面”“退出应用”。界面与托盘管理在同一个常驻进程中运行。

## 图标和资源

自身托盘、任务栏、窗口和 EXE 默认使用无底板彩色标志，自定义自身图标优先。
桌面快捷方式使用白色底板图标，可使用 `Data/Branding/DesktopIcon.ico`；开始菜单使用彩色标志。
README 的图标使用白色底板和淡灰色细描边。

ICO 包含多个像素尺寸，每个尺寸直接从已确认的 SVG 生成。字体、图标和界面渲染随单文件打包保留。
四份 SVG 源稿及用途说明见 [assets/branding](assets/branding) 和 [图标用途](docs/icon-usage.md)。

原生 DLL 必须以文件形式交给 Windows 加载，因此 .NET 在首次运行时把内置依赖释放到用户临时缓存。
这不需要用户部署 runtime 或 Assets 文件夹。程序只清理带有自身标记的旧缓存，保留当前和最近一份，配置不放在缓存中。
`RestartAgent.exe` 属于微软 Windows App SDK 的内部组件，其原名和功能在包内保留，与原软件中文名无关。

## 系统要求和兼容性

- Windows 11 x64，通常使用普通用户权限运行。
- 图标管理依赖 Windows Shell 内部接口，只对 [supported-adapters.json](native/platform/supported-adapters.json) 已验证的 Taskbar.dll 版本启用写入。
- 未支持的 Shell 版本保留原状态并报告不支持；Windows 更新后可能需要更新适配。
- 同权限边界外的程序、受保护系统元素可能无法修改。

自定义应用图标、悬停文字、主题、开机启动等设定位于 Data。目录移动后，托管图标引用会自动调整；外部应用的匹配路径保持原值。
启用开机启动后，移动软件并运行一次，会更新对应的用户登录任务。

## 从源码构建

需要 Node.js 22 或更新版本、.NET SDK 10、LLVM-MinGW x64 和 Windows SDK C++/WinRT 头文件。
脚本优先复用工作空间 Common 中的工具，也可按本机环境调整工具路径。

```powershell
npm ci --prefix packaging
./package.ps1
```

输出位于 `dist/<版本>/ReTray.exe`，便携 ZIP 只包含这个 EXE，不含用户 Data。
打包后保留最近两个版本，并保护当前运行、注册中的版本和包含个人 Data 的目录。

主界面入口为 `ui/ReTray.Wpf/src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj`。
`native/agent` 编译为同进程原生运行库，`native/shell` 为 Shell 桥接，二者都包含在单文件 EXE 内。
原生 `Agent` 构建目标仅用于隔离验收，不进入发行目录。历史开发记录位于 docs。

## 反馈与许可证

通过 [ReTray Issues](https://github.com/CRYS74L/ReTray/issues) 提供系统版本、复现步骤和相关日志；分享配置前请检查其中的本机路径和个人信息。

ReTray 使用 **GNU GPL v3.0（GPL-3.0-only）**，完整条款见 [LICENSE](LICENSE)。第三方组件保留原许可证，见 [NOTICE.md](NOTICE.md)；对应许可文本同时包含在 EXE 的资源中。
