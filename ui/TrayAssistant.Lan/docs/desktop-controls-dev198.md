# 桌面控件与设置页重构 · dev.198

设备栏改为在线／离线分段控件，保留设备数量；流量页改为直接选择 1、5、10 分钟。两个控件共用可中断的滑动底板，支持点击、按住拖选、方向键与 Home/End，并遵循 Windows 的关闭动画偏好。

全部六处启用选项使用绿色轨道、白色圆形滑块的开关。保留现有配置绑定、自动保存及 UI Automation Toggle 接口，补全辅助名称与键盘焦点。关闭免打扰或紧急快捷键时，对应编辑区域禁用。

设置页按通用、路由器连接、通知、局域网访问、快捷键分组。设置名在左，控件在右；连接信息双列排列，版本收至页脚。移除二维码、扫码提示、生成代码和 QRCoder 依赖，保留访问地址、复制链接、服务状态及原有访问功能。时间输入框明确设置宽度与内边距，避免系统缩放时末位数字被截断。

短按钮、状态徽标和时长徽标采用实际高度一半的圆角；WPF 的过大固定 CornerRadius 会产生椭圆，因此不能用任意大常数代替胶囊半径。信息卡片和输入框保留适合其用途的圆角。

## 设计依据与边界

- [Apple iOS 27](https://images.apple.com/os/ios/)：Liquid Glass 的可读性和对比度改进。
- [Apple Adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass)：分段选择、控件层级和形状关系。
- [Apple Human Interface Guidelines](https://developer.apple.com/design/human-interface-guidelines/toggles)：二元状态开关语义。

本实现是 Windows WPF 上的 Apple 风格适配；开关采用 iOS 18 的经典形态。它不是原生 UIKit 控件，也不声称完整复现系统 Liquid Glass 的实时光学折射。

## 验证

- Release 构建：0 警告、0 错误。
- `--apple-controls-regression`：直接选择时段、快速反向切换、键盘选择、六处开关、UI Automation Toggle、关联输入框、二维码移除、明暗主题及 1180×760 / 980×650 布局。
- `--settings-shortcut-regression`：设置、快捷键、设备详情。
- `--rapid-filter-reversal-regression`：列表过渡中反向切换。
- `--chart-regression`：历史采样连续性。
- `--web-access-regression`：局域网服务启停、配置保存与原网页接口兼容。
- 使用测试设备数据渲染流量页及设置页；另核对已安装程序的真实窗口、版本及路由器连接状态。

本地截图和旧安装备份仅保存在忽略的 `.tools` 中，不提交真实设备信息、凭据或运行数据。
