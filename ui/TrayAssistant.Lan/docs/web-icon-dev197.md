# 主屏幕图标修正

用户要求保留原图内容和配色，仅调整比例与背景。本次直接读取原始 `Assets/SourceIcon.svg` 的每条 path 和 fill，不重绘元素、不替换颜色、不修改 Windows 桌面图标。

- 去掉上一版的三项蓝色替换，保留原有四种紫蓝色。
- 按原始路径包围盒等比缩放并居中，图形最长边占母版约 74%；这是光学留白选择，不声称苹果规定了统一百分比。
- 增加完整、无透明通道的白色底板。依据苹果要求，交付底图保持方形，由 iOS 本身应用圆角遮罩；不预裁角、不猜测所谓“iOS 27 固定半径”。主屏幕最终外观是系统规定的圆角矩形。
- 采用新的 v197 图标 URL，旧 v195 URL 也映射到修正后的图像。已有主屏幕书签仍可能需删除后重新添加以清除系统图标缓存。
- 检查原色保留、无替换色、等比居中、尺寸、RGB 无 alpha、完整白底和旧 URL 兼容。

## 最终浏览器方案
按用户最新要求，浏览器 favicon 与主屏幕资源分开：浏览器版本没有底板、没有圆角矩形，透明背景，按原始图形包围盒等比撑满画布。HTML 的 rel=icon 和页面标识使用 browser-v197 文件；apple-touch-icon 使用带留白的不透明 network-v197 文件。manifest 的 any 使用浏览器原图，maskable 使用不透明底板版，避免混用。

苹果当前资源明确列出 iOS 27 / iPadOS 27 模板：https://developer.apple.com/design/resources/

苹果关于方形底图与系统遮罩的规范：https://developer.apple.com/design/human-interface-guidelines/app-icons
