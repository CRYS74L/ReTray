# dev.214 底层背景采用原外框材质

用户要求：将面积最大的底层软件背景改用以前被移除的外框材质参数。

对照 dev.208：外框空白区域由 Windows Acrylic 与 WindowRoot 的 MicaPanelBrush 组成；浅色遮色为 #0CFFFFFF，深色为 #09202020。内部 WorkspaceSurface 另叠加 SurfaceBrush（浅色 #70FFFFFF、深色 #54505050）。外框移除后，后者铺满窗口，使大背景比原外框更不透明。

本次将 WorkspaceSurface.Background 改为 Transparent，保留底层 WindowRoot 原有遮色，不重复叠加。Windows 合成、全局主题颜色、卡片与控件材质、圆角和窗口行为均未修改。

验证：Release 构建零警告/错误；Apple controls 浅深色布局、关闭到托盘材质、切换列表期间最小化回归通过；安装到 C:\Path\To\LanDeviceMonitor并重新打开；663 个发布文件 SHA256 一致。安装版截图位于 .tools/ui-dev214/installed-window.png。
