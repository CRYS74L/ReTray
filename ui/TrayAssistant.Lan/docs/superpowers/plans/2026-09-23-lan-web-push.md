> 历史记录：手机推送需求已撤销。dev.193 起仅提供无需配置的局域网网页，当前行为以 README 为准。

# 局域网网页与 Web Push Implementation Plan

**Goal:** 在现有 Windows 助手内提供可关闭的局域网网页与标准系统 Web Push。
**Architecture:** Kestrel 负责 HTTPS 与认证 API；独立数据快照避免跨线程访问 WPF；WebPush 服务负责保护存储、订阅及有界发送；静态网页不使用第三方 CDN。
**Tech Stack:** .NET 8/WPF、ASP.NET Core Kestrel、WebPush、QRCoder、原生 HTML/CSS/JS。
**Spec:** ../specs/2026-09-23-lan-web-push-design.md

用户已指定实施路线，本会话直接连续实施，不再重复请求开发授权。

## Global Constraints
不改路由器凭据存储、不改 Clash、不公开公网服务、不更改已有通知身份；不自动信任根证书。所有验证区分模拟与真实手机。

## Review Focus
未授权读取、恶意推送端点、停用时仍发送、证书/IP 变更、旧数据被误认为实时。

## Tasks
- [x] 添加 WebAccessTests：验证私有地址/端点校验、认证与服务启停、保护存储重载、过期订阅和失败状态。
- [x] 新建 Services/WebAccess：NetworkPolicy、ProtectedWebStore、WebPushService、LanWebServer；服务接口 StartAsync/StopAsync/Publish，按上述测试验证。
- [x] 新建 Web 静态文件：index.html、app.css、app.js、sw.js、manifest.webmanifest；读取 GET /api/snapshot，POST /api/subscribe、/api/unsubscribe、/api/test；Authorization 认证。
- [x] 新建 MainWindow.WebAccess.cs：设置初始化/应用、访问二维码、证书说明、重置授权；在现有监控更新和通知派发点接入服务，退出释放。
- [x] 集成设置持久化，构建与定向回归；真实 HTTPS 测试认证/订阅/停用，浏览器检查横竖屏、路由器与通知设置。
- [x] 更新使用说明与验证记录，发布构建、审查差异与凭据、递增项目版本并提交 annotated tag，推送现有 private origin。
