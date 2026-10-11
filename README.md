# 托盘图标助手

独立的 Windows 托盘管理软件，保留原应用的通知身份、菜单和动态状态，提供现代界面、持久规则及安全的后台生命周期。

当前开发版本：v0.1.2。**WIP：尚未完成完整产品，不是可替代原工具的稳定包。**

已验证：本机 Windows 11 26200 的真实图标目录与实时像素；受控旧式及 v4 图标的隐藏、定制、透明闪烁帧、原菜单、原生移动、最新源图恢复和桥接卸载。

已实现：按程序路径及 GUID/UID 隔离的规则匹配、增量目录和独立活动计时。`n`n待完成：规则存储与自动应用、提示与排序恢复、微信行为实测、后台与自启、WinUI 3 界面、完整旧功能、性能与整包验收。

## 构建与验证

```powershell
./build.ps1 -Target CoreTests
./build.ps1 -Target Probe
./tests/native_snapshot_integration.ps1
./tests/task1-verification.ps1
./tests/bridge_controls_integration.ps1
```

编译器优先复用工作区 Common 的 LLVM-MinGW。WinRT 头文件使用同目录 WindowsSDK，工具目录不进入仓库。

原生适配通过精确映像/PDB/关键方法校验，错误或未知布局拒绝操作。当前首个适配针对已核验的 Taskbar 映像；不能将这个读取结果扩大成所有系统版本和所有控制功能已支持。

设计与实施计划位于 docs/superpowers。机器快照、用户 tooltip、账号信息、图标截图、PDB、运行日志和构建产物仅保存在被忽略的 artifacts/build 目录。

## 依赖

- nlohmann/json 3.12.0：MIT，配置与通讯编码。
- MinHook 1.3.4：BSD，按必要范围使用的原生接入依赖；原许可及源内声明保留。
- Windows SDK / C++/WinRT、Windows App SDK：微软工具与运行时，不将其安装目录或机器符号加入源码仓库。

旧厂商二进制与 GPL 研究工程实现未复制进产品。独立源码使用 MIT；第三方依赖保留各自许可。
