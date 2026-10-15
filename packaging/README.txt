ReTray：本机开发构建。

这是开发阶段的自签名 MSIX，不是受信任 CA 签名的正式发行包。安装将向本机 TrustedPeople 添加包内的 CRYS74L 开发证书，并为当前用户安装软件。需要管理员确认。
安装完成后从 Windows 开始菜单打开“ReTray”。启动即应用保存的图标设定；普通关闭窗口会收起界面并继续后台工作。从托盘选择退出，会提交修改、退出后台并撤销软件的图标策略。托盘菜单提供打开 ReTray、重新启动和退出。

也可直接使用同目录的 ReTray-<版本>-win-x64.zip：完整解压后运行 ReTray.App.exe，无需导入证书。配置保存在 %LOCALAPPDATA%\ReTray，首次升级会保留并迁移旧配置。

ReTray 采用 GPL-3.0-only；协议与第三方声明见 LICENSE、NOTICE.md。源码：https://github.com/CRYS74L/ReTray

图标定制目前只写入匹配本机已验证 Taskbar.dll 身份的 Windows 11 版本；未知版本保留原状态并报告不支持。
真实微信收消息、宿主机登录/关机仍需最终实机验收；合成消息测试不代表已经完成真实关机验证。

This is a self-signed development MSIX. Installing trusts its CRYS74L development certificate on this machine and installs the app for the current user. Administrator confirmation is required. Launch it from Start. Unknown taskbar versions retain original behavior.
