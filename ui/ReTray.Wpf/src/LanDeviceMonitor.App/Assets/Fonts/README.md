# 内嵌字体来源与 WPF 字重

正文使用用户本机 `C:\Windows\Fonts\NotoSansSC-VF.ttf` 的 Noto Sans SC。原文件原样保存在 `Variable/NotoSansSC-VF.ttf` 并编译为 WPF Resource；没有下载同名字体替换，也没有修改本机字体安装或注册。源字体版本记录为 `Version 2.04;241114210130;non-release`，只有一个 `wght` 轴：最小 100、默认 100、最大 900。

## WPF 兼容实例

在本机 .NET 8.0.26 / PresentationCore 8.0 的独立无窗口 probe 中，系统字体、直接文件和正确的 pack 资源引用均只选择源 VF 已有的命名实例：200 解析到 100、600 解析到 700、800 解析到 900。请求九档字重只得到六种不同字形轮廓。这不表示源字体缺少变化轴，而是当前 WPF 字重选择的实际限制。代码中需采用 `FontFamily(baseUri, relativeFontLocation)` 引用 pack 字体；不能用单参数绝对 pack URI 的程序化构造来测试 XAML 资源是否可用。

`Static/` 内的九个 TTF 是从同一源文件在 `wght=100,200,...,900` 上完整实例化的真实字形，没有裁剪字符、手工加粗或假粗体。字体的字形顺序、30,890 个 cmap 码位、31,036 个 glyph、版权及许可记录均保留。TrueType 的整数坐标固定会产生最多一个字体单位的抽样轮廓舍入差，不能据此承诺每台电脑的像素渲染完全相同。

`AppTextFont` 仅引用 `pack://application:,,,/ReTray.App;component/Assets/Fonts/WpfStatic/#Noto Sans SC`，避免原 VF、旧字体与兼容实例混在同一目录参与选择。源目录 `Static/` 通过 Resource Link 映射到 `WpfStatic/`，使 WPF 嵌入字体与原生菜单的外部 Content 路径独立，防止字体选择依赖外部文件。100–900 的九档分别为 Thin、ExtraLight、Light、Regular、Medium、SemiBold、Bold、ExtraBold、Black。这里支持九档真实字重；WPF 未实现对任意连续轴值的插值。此接入没有修改正文的 14 DIP 字号或现有文字渲染参数。

完整实例化后删除静态 TTF 的变体 `STAT` 表，使 WPF 从真实的 OS/2 权重与 typographic family 信息枚举字重。原 VF 的 `STAT` 与其他所有源文件字节不变。

## 原生托盘菜单与符号

原生菜单可在进程内通过 `AddFontResourceExW(..., FR_PRIVATE, ...)` 加载发布包中的 `Assets/Fonts/Static/NotoSansSC-Regular.ttf`，使用 family `Noto Sans SC` 和 weight 400。这个文件同时作为 Content 原样复制到构建与发布目录，无需安装系统字体。

符号仍使用原样内嵌的 `SegoeIcons.ttf`，family 为 `Segoe Fluent Icons`，SHA-256 为 `9252396FD902FD1A95775EC81F52C5818EE24F8A1297127BFBCCA2787C1B03E4`。旧 `msyh.ttc`、`msyhbd.ttc`、`msyhl.ttc` 保留于源工程，但不再作为正文 WPF Resource 打包；原 LAN 网页字体资产未在本次桌面字体接入中改写。

## 重建与证据

使用 `scripts/build-noto-fonts.py --source <本机原始VF文件> --fonttools-path <已有fontTools目录>` 重建。当前使用已有的 fontTools 4.66.1。该脚本验证原 VF 的 SHA-256 不变、九档权重元数据、完整 cmap 和 glyph 顺序，并将抽样字形轮廓与源 VF 在对应轴值上的轮廓比较。

`noto-font-manifest.json` 包含原文件 SHA-256、轴范围和每个真实实例的文件名、字重、大小、SHA-256 与上述验证结果。WPF 无窗口 probe 的源代码与原 VF / 兼容实例解析记录位于工作区 `artifacts/noto-font-proof-v17/`；产品的实际构建与可见界面检查由主任务负责。

Noto 字体原版权记录和完整 SIL Open Font License 1.1 保存在 `OFL.txt`，同时随发布包复制。许可正文来源：[SIL OFL 官方文本](https://openfontlicense.org/documents/OFL.txt)。
