# dev.222 独立悬停反馈、底部圆形按钮与边缘缩放恢复

窗口按钮：三色面色与柔边小幅提亮；常态直径仍 17 DIP，中心间距由 24 增至 28 DIP；靠近放大由 12% 增至 24%（约 21.08 DIP），仍与符号共用进度。独立 HotAmount 仅随单个按钮真正可点击区域的 IsMouseOver 变化，提供提亮及深色轮廓，并与靠近动画采用独立代次管理，避免互相中断。

底部路由器/设置：36×36 DIP、半径 18 DIP、细描边、半透明面、独立悬停及按下反馈，保留可访问名称与入口行为。

缩放原因及修复：根容器的 WindowChrome.IsHitTestVisibleInChrome=True 将边缘也强制视为客户端区域。移除该整层标记，将 CaptionHeight 设为 0，保留原本的 WindowDragRegion 拖动/双击逻辑和 6 DIP 原生 ResizeBorder。

验证：Release 编译零错误/警告；新增 WM_NCHITTEST 实测四边、四角分别返回 HTLEFT/RIGHT/TOP/BOTTOM/TOPLEFT/TOPRIGHT/BOTTOMLEFT/BOTTOMRIGHT，窗口按钮中心仍返回 HTCLIENT。靠近与单按钮悬停分离、移出无残留、底部圆形描边、多 DPI 像素、导航、最小化及托盘材质回归通过。已安装运行 dev.222，663 发布文件 SHA256 一致。
