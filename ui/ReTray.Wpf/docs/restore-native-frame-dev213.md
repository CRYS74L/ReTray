# dev.213 恢复 Windows 预设外框与原始材质

用户在实机观察到自绘圆角方案改变材质并带来问题，明确改用 Windows 预设。

- 删除强制 SetWindowRgn 圆角及相关加载/尺寸/DPI 钩子。
- 移除本轮未提交的独立合成背景、阴影窗口、自绘外框实验；原始 MicaWindowService 与 dev.210 完全一致。
- WindowChrome 回到既有 12 DIP 参数、DWM 使用系统圆角预设，主背景采用 8 DIP，避免卡片半径套到窗口内部后露出双层角。网络信息及其他卡片的 24 DIP 不变。
- 保留无底座、仅整组悬停显示符号的窗口按钮，直径 20 DIP。

验证：Release 编译零错误/警告，Apple controls、最小化过渡及关闭到托盘回归通过；实机截图核对材质恢复；663 个发布文件 SHA256 一致。截图位于 .tools/ui-dev213/window-with-shadow.png。实验源码保存在忽略目录 .tools/custom-frame-experiment-backup，未纳入项目。

## 自定义圆角方法调查

Microsoft DWM_WINDOW_CORNER_PREFERENCE 公开 API 只有默认、不圆、圆角、小圆角预设，不接受自定义数值。
https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwm_window_corner_preference

已核实 Windhawk 官方模块仓库的 Custom Window Corner Radius 1.3（m417z）。其源码明确 include dwm.exe，使用 DWM 内部挂钩改变圆角；并提示超过 20 像素可能随 DPI 产生视觉瑕疵。这是已发布的现成系统级方案，但不是普通 WPF 控件或公开 Windows API；不能将其存在等同于本应用可无副作用嵌入。未安装、未修改 DWM 或系统设置。
https://github.com/ramensoftware/windhawk-mods/blob/main/mods/custom-corner-radius.wh.cpp
