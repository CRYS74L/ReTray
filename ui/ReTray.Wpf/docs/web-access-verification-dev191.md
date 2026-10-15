> 历史记录：手机推送需求已撤销。dev.193 起仅提供无需配置的局域网网页，当前行为以 README 为准。

# dev.191 局域网网页与 Web Push 验证

## 已执行
- Release 构建：0 warnings / 0 errors。
- `--web-access-regression`：私有地址、推送端点白名单、防 SSRF、P-256 公钥校验、DPAPI 重载、密钥不明文、授权重置、旧代次拒绝发送。
- 真实 Kestrel HTTPS：用导出 CA 构建自定义信任链（同时校验主机名），验证认证读取、公开证书、明文端口禁止监控 API、跨源拒绝、订阅持久化、关闭后端口不可访问。
- Web Push 假传输：由正式 WebPush 库产生加密负载和 VAPID 授权、TTL 300 秒；HTTP 410 清理订阅，禁用取消正在等待的请求。假传输只验证协议请求，未发送真实手机通知。
- 实际 WPF 控件与设置处理：无效端口下独立关闭推送仍保持网页服务；无效端口下关闭局域网服务与推送，重新加载后仍关闭。测试用独立目录，不写入日常配置。
- `--settings-shortcut-regression`、`--notification-once-regression`、`--offline-confirmation-regression`、`--rate-arrow-regression` 通过。
- 侧边浏览器：390×844 竖屏列表/详情/返回、搜索、离线筛选；844×390 横屏双栏；IPv4/IPv6、Mesh、通知关闭状态；无横向溢出，无 console error/warn。使用明确标注的合成示例数据。
- 发布部署：目标 `C:\Path\To\LanDeviceMonitor`，652 个发布文件 SHA-256 逐一比对，0 差异；旧文件保存在 Git 忽略的 `runtime-data/deployment-backups`。

## 审查修复
独立审查发现并修复：无效端口阻止关闭访问/推送、重置与订阅登记的竞态、进行中的推送未取消、测试通知跨授权代次、连接类型丢失细分、浏览器订阅与服务器状态不一致。定向复核无阻断问题。

## 必须由真实手机确认
尚未验证 iOS/iPadOS/安卓实机的 CA 信任、安装到主屏幕、系统授权、锁屏实际到达。浏览器厂商网络、系统免打扰和后台限制不能通过本机模拟证明。首次配置需要用户在手机上完成，不能把“推送服务接受”描述成“已显示通知”。本次未更改系统证书信任、防火墙、Clash 或公网映射。
