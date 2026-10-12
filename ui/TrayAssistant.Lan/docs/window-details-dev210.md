# dev.210 窗口样式与侧栏细节

恢复 dev.208 的 WindowChrome 框架参数（CaptionHeight 40、ResizeBorder 6、GlassFrame 0、CornerRadius 12）；保留铺满内容布局，不恢复空白标题行。顶层内容显式允许 Chrome 区域命中，内容区原有拖动处理保留。撤销 dev.209 的 DWM COLOR_NONE，使用系统默认描边。原 Acrylic、托盘与最小化材质路径保持不变。

窗口控制适配 react-desktop 的 macOs Controls：12 DIP 圆形、20 DIP 间距、源 SVG 图形、整组悬停显示、按下加深和失焦灰色。按 Windows 顺序排列。官方 AppKit 按钮不能直接运行于 WPF；此处为 MIT 控件移植，非 Apple 原生实现。来源和许可存于 ThirdParty/react-desktop，许可随发布携带。

滚动条可见滑块位于设备胶囊右缘和侧栏外缘之间。底部在线时仅显示更新于 HH:mm:ss；错误/重连状态仍可见。字体 14 DIP、圆点 9 DIP。

验证：Release 构建零错误/警告，Apple controls、设置快捷键、托盘保持表面、最小化过渡回归通过。新增在线/错误状态互斥显示和滚动条位置检查。真实安装版截图位于 .tools/ui-dev210/installed-window.png；663 个发布文件 SHA256 全部匹配。

参考：https://developer.apple.com/documentation/appkit/nswindow
组件：https://github.com/gabrielbull/react-desktop/tree/bba61374849dec896a9eea321cf4e3f02b3fd4c6/src/TitleBar/macOs/Controls
