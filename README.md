# 托盘图标助手

独立 Windows 托盘管理软件，保留原应用的通知身份、菜单与动态图像。原生后台常驻，WinUI 3 界面按需打开。

当前开发版本：v0.1.3。**WIP：仍在持续开发，尚未完成全部功能和整包验收。**

已通过本机实测：真实目录/实时像素、旧式及 v4 图标隐藏与定制、透明闪烁帧、原菜单、原生位置与提示恢复、后台意外退出恢复及 DLL 卸载；Windows 11 原有网络/音量/电源/输入法/开始对象读取，网络单项隐藏/恢复；30 个核心契约、配置原子保存及损坏恢复；后台会话查询/取消及有界正常退出。WinUI 3 界面已连接真实后台并成功运行，构建 0 警告、0 错误。

待完成或未验证：全部旧附加功能与交互、持久规则跨重建验收、压力/资源测试、微信真实消息、真实关机/取消、配置迁移及完整发行包。当前仅适配经过核验的本机任务栏映像；未验证系统版本不猜偏移操作。

## 构建与验证

```powershell
./build.ps1 -Target CoreTests
./build.ps1 -Target SettingsTests
./tests/task1-verification.ps1
./tests/bridge_controls_integration.ps1
./tests/bridge_controls_integration.ps1 -SkipBuild -Orphan
./tests/agent_integration.ps1
./build.ps1 -Target SystemControls
```

编译器复用工作区 Common 的 LLVM-MinGW，WinRT 头文件使用 WindowsSDK；UI 使用锁定的 .NET 10 / Windows App SDK 2.5.1。工具目录、机器快照、tooltip、账号、图标截图、PDB、运行日志与构建产物只保存在被忽略的目录中。

## 依赖和许可

nlohmann/json 3.12.0（MIT）、MinHook 1.3.4（BSD）、CommunityToolkit.Mvvm 8.4.2；各自原许可保留。Windows SDK、C++/WinRT、Windows App SDK 使用微软工具和运行时。旧厂商二进制、GPL 研究实现没有复制进产品源码；独立代码使用 MIT。
