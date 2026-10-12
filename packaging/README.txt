托盘图标助手：本机开发构建。

这是开发阶段的自签名 MSIX，不是受信任 CA 签名的正式发行包。安装将向本机 TrustedPeople 添加包内的 CRYS74L 开发证书，并为当前用户安装软件。需要管理员确认。
安装完成后从 Windows 开始菜单打开“托盘图标助手”。启动即应用保存的图标设定；关闭窗口或从托盘选择退出，会提交修改、退出后台并撤销软件的图标策略。托盘菜单提供打开助手、重新启动和退出。

图标定制目前只写入匹配本机已验证 Taskbar.dll 身份的 Windows 11 版本；未知版本保留原状态并报告不支持。
真实微信收消息、宿主机登录/关机仍需最终实机验收；合成消息测试不代表已经完成真实关机验证。

This is a self-signed development MSIX. Installing trusts its CRYS74L development certificate on this machine and installs the app for the current user. Administrator confirmation is required. Launch it from Start. Unknown taskbar versions retain original behavior.
