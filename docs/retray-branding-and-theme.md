# ReTray 品牌与主题

本次将产品名称、程序集、原生组件、IPC 标识、包身份、界面文案与仓库统一为 ReTray。
旧身份仅保留在配置迁移和兼容性检查中，避免破坏既有设置和导出的配置包。

## 主题与图标

- 系统主题读取 HKCU Personalize 的 `SystemUsesLightTheme`；独立指定的浅/深色界面仍优先。
- 后台在既有刷新周期检查默认 Windows 模式，自动选择黑/白自身托盘图标。
  `ownIcon.asset` 非空时保留用户选择；原图预览显示当前 Windows 模式对应的默认标志。
- 窗口和任务栏使用透明彩色标志。为区分 MSIX 安装图标和任务栏图标，窗口显式设置
  `CRYS74L.ReTray.Window` 及重启命令、名称和图标资源；EXE 和包图标仍保留白色底板。
  任务栏的稳定图标副本位于 `%LOCALAPPDATA%\ReTray\Branding\WindowIcon.ico`。
- 四份定稿 SVG 和参考 PNG 位于 `assets/branding`；`geometry.json` 保留用户确定的完整参数。
  环带宽 110、圆盘 R112、尖角延长 54.5、尖端 R5、转角 R66.5，平滑 R133 / 24°。
  包含 27 个渐变色标的 HSV 与 HEX；四款图标共用轮廓。构建直接从 SVG 渲染各尺寸，
  每个 ICO 包含 15 帧。托盘读取任务栏 DPI 对应的小图标尺寸，DPI 改变后重新加载。

窗口属性的使用依据：[Microsoft AppUserModel.ID](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-id)、
[RelaunchIconResource](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchiconresource)。

## 数据与启动迁移

旧普通目录和 MSIX LocalCache 中可能存在不同配置。首次启动选择最近保存的旧配置，
以同级暂存目录复制并重写托管图标引用，最后重命名发布。已有 ReTray 配置优先，旧数据保留。
损坏主文件、错误字段类型和仅有备份的情形继续使用正常配置恢复逻辑。
旧窗口激活身份由新界面重新注册。

新 MSIX 关闭文件/注册表写入虚拟化，让安装版、免安装版使用相同的数据位置与登录启动设置。
依据：[Microsoft MSIX flexible virtualization](https://learn.microsoft.com/en-us/windows/msix/desktop/flexible-virtualization)。
计划任务只按确切名称、Source 标记和用户 SID 识别。旧 Run 命令还需核验参数和旧程序产品身份。
清理失败时恢复先前的启动开关；不覆盖其他程序的同名入口。

## 验证

- 核心逻辑、配置与备份恢复、原生系统/图标事务、登录启动及配置包回归。
- 界面主题检查覆盖本机 Windows 深色、应用浅色的组合，显式浅/深色设置仍有效。
- 窗口属性检查验证任务栏身份、彩色图标和完整重启信息；实际任务栏截图确认无底板彩色图标。
- 本机迁移保留 37 条规则、2 项输入法设置，13 个自定义图标哈希一致。
- 从真实 Shell 目录读取自身图标像素，确认默认白色和临时自定义黑色均生效，并恢复原设置。
- 定稿图标构建后逐帧解码检查四套 ICO，每套 15 帧；本机 125% 下，原生托盘帧为 20px。
- 最终资源索引覆盖 100/125/150/200/250/300/400% 和 15 个 targetsize；打包显式生成
  主 PRI，防止工具的默认 scale-200 索引漏掉其他比例。提供基础 PNG 供开发注册使用。
- 实机界面检查覆盖“默认 Windows 深色、默认应用浅色”；通过设置页切换系统主题后
  界面显示深色，再恢复用户原有的浅色偏好。37 条规则、2 项输入法设置和 13 份图标保持不变。

实机材料保存在被忽略的 `artifacts/retray-migration`；包含用户配置的备份不进入 Git。
