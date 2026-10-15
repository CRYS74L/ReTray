# Store 应用升级后保留托盘图标规则（v0.1.40）

本机旧规则和替换图标文件仍存在。规则中的进程路径为 OpenAI.Codex_26.1007.2314.0/app/ChatGPT.exe，当前注册图标进程路径为 OpenAI.Codex_26.1007.9446.0/app/ChatGPT.exe，图标 GUID 相同。旧版要求完整路径相等，导致新注册未匹配旧配置，并呈现 custom=false。

修复保留普通程序的完整路径匹配。对于标准 WindowsApps Store 安装目录，先用 PackageIdFromFullName 的 BASIC 模式验证完整包名称，再以包家族、resourceId 和包内相对可执行文件路径识别同一程序。包家族保留应用名及发布者标识；版本和处理器架构变化、标准 Store 目录之间的迁移不会打断匹配。不同包名、发布者、资源包或包内程序不合并，任意同名 EXE 和普通版本目录不归并，路径跳转与畸形包名不参与兼容匹配。

GUID 和旧式 UID/角色识别沿用原算法，不将多个托盘图标合并。多个既存规则互相冲突时仍报告冲突，不自动挑选或覆盖。保存的 selector、asset、提示、显隐设置及 PersistentKey 保留，旧版本不存在时也可辨认其合法包身份，无需改写用户配置或复制替换资源。此修复针对正常包升级的版本路径变化。

Windows API 依据：

- [PackageIdFromFullName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-packageidfromfullname)：BASIC 模式可解析未安装但合法的完整包名称。
- [PackageFamilyNameFromFullName](https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-packagefamilynamefromfullname)：从完整包名称提取包家族。

回归覆盖实际版本目录变化、跨卷/架构变化、GUID 隔离、发布者/资源包/程序路径隔离、非法及伪造目录、旧式多图标安全、冲突规则。后台集成回归覆盖两次升级后自动重施图像和提示、目录呈现、规则内容未改写。

验证命令：`build.ps1 -Target CoreTests` 和 `build.ps1 -Target AgentVisibilityTests`。本机验收读取规则与原生目录、更新ReTray后核对替换图标恢复，并保留 ChatGPT/Codex 与 Explorer 的运行进程。

本机最终验收：旧规则逐项保持，当前注册 custom=true、decision=CustomIdle、应用成功、无身份冲突；折叠托盘实拍出现原有紫蓝色终端替换图标。ChatGPT.exe 与 Explorer 的原 PID 和启动时间持续不变。43 项核心回归及后台集成回归通过，独立复核发现的嵌入 NUL 字符校验边界已修复并加入回归。
