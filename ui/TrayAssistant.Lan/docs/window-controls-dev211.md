# dev.211 窗口圆角与 iPadOS 参考按钮

窗口 WindowChrome、WorkspaceSurface 与 CardStyle 共用 CardCornerRadius=24 DIP，避免外框与卡片独立漂移。保留原窗口材质与生命周期。

按用户提供的 iPadOS 截图调整整组胶囊底座与高饱和圆钮：直径 24 DIP、点击区 32 DIP、符号 16 DIP。保留既定 Windows 顺序（最小化、最大化、关闭）。沿用已有 MIT 矢量符号，不以此宣称 Apple 原生组件。

旧模板的 IsKeyboardFocused 触发器会令点击后符号继续显示，表现为移出后一个按钮仍悬停。新模板符号常显，悬停遮罩只依赖 IsMouseOver，按下颜色只依赖 IsPressed，没有保留动画时钟。

验证：Release 构建零错误/警告；Apple controls 回归通过，新增圆角共享、三个符号布局中心、焦点离开后状态检查；最小化与关闭到托盘回归通过。检查安装版截图 .tools/ui-dev211/installed-window.png；663 个发布文件 SHA256 一致。备份 dev210-before-ui211 保留于 .tools/deployment-backups。
