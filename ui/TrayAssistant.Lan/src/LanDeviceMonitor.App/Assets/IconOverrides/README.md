# Lucide 局部适配

当前仅保留 `tablet.svg`：按用户要求稍加宽平板机身，将底部实心点改为小圆环。保留 Lucide 的线条语言与许可证归属。

摄像头和插座已撤销定制轮廓，恢复固定版本 Lucide 的 `cctv`、`plug`。笔记本继续使用官方 `laptop-minimal`。

`scripts/build-device-icons.py` 优先读取此目录的同名覆盖文件；其余图标使用 `ThirdParty/lucide/icons`。桌面和网页从同一来源生成。
