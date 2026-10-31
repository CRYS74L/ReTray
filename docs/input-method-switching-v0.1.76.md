# 输入法图标切换 v0.1.76

自定义输入法标志随任务栏的输入法数据变化更新，切换期间保留对应的替代图。恢复原图、未配置替代图的输入法和原生中英文状态图标继续按各自设置显示。

## 实现

`LanguageSystemTrayIconDataModel` 通过 `IIconDataModel.DataModelChanged` 发出 `CurrentData` 通知。桥接为每个语言标志实例订阅该事件，将同一轮更新合并到一次 `CompositionTarget.Rendering` 回调；回调先解除自身订阅，再读取 Shell 的输入法身份并更新标志。空闲期间不保留逐帧回调。

此接口沿用现有 Taskbar.View.dll CodeView GUID 与 age 校验，并核对 IIconDataModel IID、producer vtable 和事件 add/remove 地址。清空策略或桥接退出时解除模型事件和待处理的绘制回调。Image、TextBlock、FontIcon 的原始内容变化也安排在绘制前处理。

## 回归入口

先退出正在运行的 ReTray，再编译和运行真实任务栏驱动：

```powershell
./build.ps1 -Target InputMethodLiveTests
./build/InputMethodLiveTests.exe ./native/platform/supported-adapters.json ./build/ReTray.ShellBridge.dll <标志中心X> <标志中心Y> --switch ./artifacts/input-switch
```

切换检查需要至少两个已启用的输入法，并在标志左侧 46 个物理像素处具有可区分的原生输入状态字形。测试只改变自己的前台输入上下文，退出时恢复该上下文和任务栏替代策略。

驱动在切换期间独立采集屏幕帧，不发送刷新或读取命令。每帧检查替代图的 9×9 像素区域，记录前台窗口，并以原生状态字形的实际变化测量图标更新时差；TSF 输入服务的启动时间不计作图标替换延迟。额外检查未配置输入法显示原图、切回已配置输入法、清空策略、缓存回滚和状态图标独立性。帧和时序文件仅保留在本地 artifacts。
