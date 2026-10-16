# ReTray

**Redefine Your Tray — 重新定义你的 Windows 托盘。**

ReTray 是一个 Windows 11 托盘管理工具，帮助你隐藏或显示图标、替换图标和提示文字，
并保留原应用的鼠标交互与动态状态。

<p align="center"><img src="assets/branding/retray-macos-white.svg" width="128" alt="ReTray"></p>

## 能做什么

- 按应用和图标身份保存显示、隐藏、替换图标及提示文字规则。
- 预览原图与替换图，管理输入法图标和受支持的系统托盘元素。
- 导出、导入 `.trayconfig` 配置包，将使用到的自定义图标一起打包。
- 提供浅色、深色和跟随系统主题；**跟随系统读取的是默认 Windows 模式**。
- 默认使用无底板彩色自身托盘图标，不随深浅主题改变；
  如果你替换了 ReTray 自身图标，则保持你的选择。
- 可选择随用户登录启动；关闭主窗口后继续在后台工作，可从托盘菜单明确退出。
- 支持简体中文、繁体中文、英语和日语界面。

## 系统要求与兼容性

- Windows 11 x64。图标管理依赖 Windows Shell 内部接口，只会在
  [`supported-adapters.json`](native/platform/supported-adapters.json) 已验证的
  Taskbar.dll 版本上启用相关写入操作。
- 不支持的 Shell 版本会保留原状态并报告不支持；Windows 更新后可能需要更新适配。
- 一般以普通用户权限运行。同权限边界外的程序、受保护系统元素可能无法修改。
- 当前提供开发构建；自签名 MSIX 不等同于受信任 CA 签名的正式发行包。

## 开始使用

**免安装构建：** 将整个 `ReTray-<版本>-win-x64.zip` 解压到固定目录，运行
`ReTray.exe`。界面与托盘管理运行在同一进程中；请保留同目录的运行库、桥接 DLL、字体和图标，不要只复制单个 EXE。

**MSIX 构建：** 使用发行包附带的安装脚本。脚本会请求管理员权限，将附带的
CRYS74L 开发证书导入本机 TrustedPeople，再为当前用户安装软件。仅在你信任此构建时执行。
安装后从开始菜单打开 ReTray。免安装版本不需要导入该证书。

打开“托盘图标”页面，选择图标并编辑所需规则。设置会自动保存。
普通关闭窗口会收起界面；从 ReTray 托盘菜单选择“退出”会提交修改、退出后台并撤销本次运行策略。

## 图标与主题

| 位置 | 图标 |
| --- | --- |
| 自身托盘图标 | 无底板彩色标志；自定义图标优先 |
| 任务栏、窗口、界面内标志 | 无底板彩色标志 |
| EXE、程序目录、开始菜单、安装包和已安装应用 | 无底板彩色标志 |
| 桌面快捷方式 | 白色底板图标 |

界面“跟随系统”以默认 Windows 模式为依据，也可以单独固定浅色或深色。
自身托盘图标始终默认使用彩色标志。
四份 SVG 源图与用途说明位于 [`assets/branding`](assets/branding)。
开启状态的开关使用图标中的品牌紫色。标题图标使用 WPF 原生矢量绘制；
顶栏文字使用较小的正常字重，以实际字形中心与图标垂直对齐。自身托盘图标的默认悬停文字为 ReTray。

## 数据与升级

配置、图标资源和窗口位置统一保存在 `%LOCALAPPDATA%\ReTray`，安装版和免安装版共用这一位置。
更名升级会检查普通目录和旧 MSIX 的虚拟化目录，复制最近保存的旧配置并调整托管图标路径，
保留旧目录作为备份；已有 ReTray 配置不会被覆盖。
用户规则、自定义图标与提示文字会保留，旧的默认软件提示文字会更新为 ReTray。
旧版导出的 `.trayconfig` 仍可导入。

迁移前退出旧版，避免两个版本同时管理托盘。更换免安装目录后，如已启用开机启动，
运行新版会更新对应的用户登录计划任务。需要转移到其他电脑时，优先使用配置导出功能。

## 从源码构建

需要 Node.js 22 或更新版本、.NET SDK 10（支持构建 net8.0 Windows 项目）、LLVM-MinGW x64、Windows SDK C++/WinRT
头文件，以及制作 MSIX 所需的 `winapp` CLI。依赖可通过 NuGet 还原。
脚本会优先使用工作空间 `【Common】` 下的已有工具；其他环境可调整脚本工具路径或将工具加入 PATH。

```powershell
npm ci --prefix packaging
./packaging/build-assets.ps1
./build.ps1 -Target CoreTests
./build.ps1 -Target SettingsTests
./build.ps1 -Target Runtime
./build.ps1 -Target Bridge
./package.ps1 -SkipNative
```

主界面构建入口是 `ui/ReTray.Wpf/src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj`。
`native/agent` 编译为同进程加载的 `ReTray.Runtime.dll`，负责配置与托盘管理；`native/shell` 负责受支持的 Shell 桥接，`tests` 保存回归检查。`Agent` 构建目标仅为原生隔离验收保留，不进入发行包。
包和免安装文件输出到 `dist/<版本>`，运行布局输出到 `publish/<版本>-origin/layout`。
历史设计与问题分析放在 `docs`，README 仅介绍当前产品。

## 反馈与许可证

请在 [ReTray Issues](https://github.com/CRYS74L/ReTray/issues) 提供 Windows 版本、
问题复现步骤和相关日志；提交配置前请检查其中的本机路径及其他个人信息。

ReTray 采用 **GNU GPL v3.0（GPL-3.0-only）**，允许使用、修改和商业分发；
分发衍生作品及二进制时需遵守相应源码提供等义务。完整条款见 [`LICENSE`](LICENSE)。
第三方组件保留原许可证，详见 [`NOTICE.md`](NOTICE.md)。
