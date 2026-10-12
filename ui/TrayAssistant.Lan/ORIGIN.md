# 工程起点

本目录由 CRYS74L/lan-device-monitor 完整仓库导出，源提交 aa9250653225ecd324babbf25cc27c6240ee6416。

原 src、项目文件、解决方案、脚本、测试与资源一同复制。产品实际构建入口为原 src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj。原 Controls、主题、窗口框架与材质服务保留；原启动与 MainWindow 业务入口接到 TrayIntegration。LAN 监控、通知注册、网络服务及原 NotifyIcon 不启动，原配置路径隔离为本产品目录。

TrayIntegration 替换业务内容。源窗口资源实例由原 MainWindow 提供，没有另建近似样式库。原字体文件、绘制、悬停和按压代码保持原始实现。修改仅涉及产品身份、业务接线与用户指定的小窗口、顶栏、紧凑行布局。