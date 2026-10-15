# v0.1.23 托盘注册生命周期修复

Breeze 退出时，Windows 已不再提供有效注册，但ReTray仍保留旧条目。重新启动产生新的进程、窗口身份，旧条目因缺少可拦截的删除消息而持续累积。现场完整原生快照为 25 项，ReTray缓存为 26 项，其中旧 Breeze 的进程和窗口已经退出。

后台每两秒核对一次原生注册，独立于图标更新事件；界面随后按原有一秒轮询更新。显示/隐藏事务先获取完整的新目录，失效目标在保存前返回“图标已退出”，失效同名实例不会参与当前实例的显隐更新。临时读取失败、共享快照未完成或重复身份快照保留上一份有效目录。

每次注册使用桥接内递增的注册序号。即使同一进程在两次轮询之间用同一 HWND/UID 或 GUID 删除、重建图标，也会重新应用保存的显隐、图标和提示策略。新注册释放上一注册的源位图引用和预览缓存；操作指令首先发现地址变化时也执行相同处理。普通闪烁和图像更新保留当前注册序号及有效显示帧。持久规则、列表顺序与原有图标身份继续使用既有键。

内部桥接共享结构升级至 schema 4，安装消息、指令消息和映射名称同步升级。个人配置仍为 schema 1。

开关的无障碍 TogglePattern 通过 CheckBox 的正常 OnClick 路径更新：单向绑定的托盘行触发业务 Click，双向绑定的设置行继续更新其数据源，禁用状态拒绝操作。

## 验证

- 原生回归 81 项通过：32 项核心、30 项图标/目录/菜单、16 项系统策略、3 项桥接注册/位图释放；配置测试通过。
- 3 项实际生产 SwitchControl 的 WPF 无障碍回归通过：单向托盘绑定、双向设置绑定、禁用状态。修改前前两项失败。
- 真实 Breeze 连续正常退出、重开 5 轮，注册消失用时 29–1796 毫秒，没有持续累积；真实鼠标点击完成隐藏、显示。
- 最终安装版本中，真实 Breeze 的 UIA TogglePattern 和鼠标点击均完成隐藏、显示，随后正常退出并清除条目。
- 完整原生链路覆盖 5 轮不发送 NIM_DELETE 的异常退出、GUID/非 GUID 两个实例并存、显示/隐藏、同身份 DELETE/ADD 后恢复策略、进程仍在但拥有者窗口已销毁、同进程新窗口重新注册。
- 在相同安装身份下核对原有 27 条规则及 23 项列表顺序，测试结束后还原操作前偏好。退出时保留持久偏好，仅清理本轮测试创建的进程及测试规则。
- 最终已退出测试应用的现场，ReTray目录与完整原生快照均为 24 项，没有 Breeze 残留注册。
- 自包含 MSIX 构建、应用包激活与独立代码复核通过。此记录针对本次注册和显隐修复；既有真实微信消息及登录/关机验收范围见 README。

## 重复执行

```powershell
./build.ps1 -Target CoreTests
./build.ps1 -Target SettingsTests
./build.ps1 -Target AgentVisibilityTests
./build.ps1 -Target AgentSystemTests
./build.ps1 -Target BridgeRegistrationTests
dotnet run --project ./tests/ReTray.UiTests/ReTray.UiTests.csproj -c Release -r win-x64 --self-contained true -p:Platform=x64
./tests/agent_catalog_integration.ps1 -ExpectedVersion 0.1.23
```

原生集成脚本对它创建的测试进程执行异常退出，用于模拟缺少删除消息的应用。它恢复操作前的配置并校验，证据写入被忽略的 artifacts/catalog-lifecycle。真实 Breeze 验证使用本机临时验收脚本，机器路径和个人数据不进入仓库。

本机发布布局通过 `winapp run <布局目录> --manifest <布局目录>/AppxManifest.xml --detach` 注册并激活，使用同一 PackageFamilyName 读取原有配置。直接启动未带安装身份的 EXE 会读取独立的未打包配置。发放的 MSIX 正常安装后，从开始菜单激活。
