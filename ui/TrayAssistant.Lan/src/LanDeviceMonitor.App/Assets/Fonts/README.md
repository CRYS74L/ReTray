# 内嵌字体来源

这三个文字字体直接取自用户当前电脑的字体文件。用户已将其字形替换为苹方，并保留“Microsoft YaHei / Microsoft YaHei UI”的字体名称。不可用网上下载的同名字体替换它们。

| 文件 | SHA-256 |
|---|---|
| msyh.ttc | C1B7326EA075E9A2DB40F92C9D516CFA39BB8760A11DB2DA30A18D3247D3BB7E |
| msyhbd.ttc | 356DA57585463AB3A55D79A61789459003014BFC32CD6B9297697013DBC4F53E |
| msyhl.ttc | 6CE6ACF2C33FCFAD88873E5E1C55B5B209E33D6F56C36B505BFF61281CC1857F |
| SegoeIcons.ttf | 9252396FD902FD1A95775EC81F52C5818EE24F8A1297127BFBCCA2787C1B03E4 |

最后一个文件仅用于符号图标，文字始终使用同一套 AppTextFont。桌面字体编译为 WPF Resource，网页字体编译为 EmbeddedResource，由同一程序提供，不依赖访问设备的系统字体或外部字体服务。

网页 WOFF2 来自上述 TTC 中的 Microsoft YaHei UI face，保留完整的 36,374 个码位与字宽。生成工具为 `scripts/build-web-fonts.py`，本次使用 fonttools 4.66.1 和 brotli 1.2.0；本地依赖置于被忽略的 `.tools/font-build-deps`。
