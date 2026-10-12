# 工程起点

本目录由 CRYS74L/lan-device-monitor 完整仓库导出，源提交 aa9250653225ecd324babbf25cc27c6240ee6416。

原 src、项目文件、解决方案、脚本、测试与资源一同复制。产品实际构建入口为原 src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj。原 Controls、主题、窗口框架与材质服务保留；原启动与 MainWindow 业务入口接到 TrayIntegration。LAN 监控、通知注册、网络服务及原 NotifyIcon 不启动，原配置路径隔离为本产品目录。

TrayIntegration 替换业务内容。源窗口资源实例由原 MainWindow 提供，没有另建近似样式库。原字体文件、SegmentItem/SegmentedControl 的悬停和选中画刷、控件按压代码保留。产品适配包括小窗口、顶栏、紧凑行布局及用户明确要求的文字渲染调整。

托盘列表按产品要求使用平面行与名称列起始的细分隔线，不绘制整行胶囊或整行悬停/按压底色。拖拽时按当前 DPI 截取完整行，在无输入命中的 Adorner 层按原鼠标抓取位置跟随，结束后立即移除；开关与原生图标交互仍各自独立。

顶栏文字绑定原 SegmentItem 的动画 Foreground。页面横向切换沿用原设备筛选切换的 340 DIP、480 毫秒、Cubic EaseOut 与透明度交叉过渡，滚动条只淡入淡出；结束后释放缓存与动画时钟。移除托盘页和编辑页底部空白边距，视口渐变高度缩至 12 DIP。

下拉选框与菜单文字按用户要求与邻近正文共用 BodyTextStyle 和 14 DIP 字号，保留原字体与正常字重；撤销独立 ClearType 与固定提示候选。输入框与小标题保持已获用户确认改善的文字渲染设置。
