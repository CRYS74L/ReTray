# 实时网速刷新与连续折线修复实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让设备监控按照设置的刷新周期进行轮询和 UI 更新，使用路由器累计字节变化计算真实的区间平均速率，并恢复连续折线图表现。

**Architecture:** 保留现有单线程轮询和 UI 有界投递结构；轮询周期只在请求耗时超过设置值时受请求耗时限制，不额外再等待一个完整周期，同时保留 100ms 最小退避避免慢请求无间隔重试。DeviceRepository 优先使用本轮明确的瞬时字段（包括 0）；仅在瞬时字段缺失且累计值刚产生正向差分时使用真实观察区间平均速率，没有新证据就不追加样本。TrafficChart 不为未知槽位生成伪造数值，但恢复真实观测点之间的连续折线，避免把缺失槽位错误绘制成断线或散点。在线/离线列表切换继续使用原 1040ms 视觉时长；详情面板仍在列表切换开始时同步启动原有 260ms 退出 + 780ms 进入动画；动画期间仅对列表和详情视觉树启用临时 BitmapCache，结束后立即清除；上下线历史复用稳定的数据源和预留的历史行视图，只更新行内容并折叠多余行，避免切换详情时同步重建整棵历史视觉树，不改变动画时序、缓动、内容或视觉样式。

**Tech Stack:** .NET 8、WPF、C#、PowerShell、项目自带测试程序。

**Spec:** 当前用户关于“回滚散点图、实际刷新频率符合设置、能用累计值换算但不能伪造、修改后启动软件验收并保持打开”的要求。

## Global Constraints

- 保留现有功能、布局、主题和图表颜色/样式；只恢复被 dev.141 改坏的连续折线数据表现。
- 不以随机数、线性插值或旧值冒充未知的每秒瞬时值；累计值差分只作为真实观测区间的平均速率。
- 只部署干净文件到 `C:\Path\To\LanDeviceMonitor`，不创建 E 盘其他备份或目录。
- 修改完成后启动部署版本并保持运行，供用户验收；不进行长时间压力复现。

---

### Task 1: 轮询周期回归

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/Services/MonitoringLoop.cs`

- [x] **Step 1: Write the failing test**

将耗时超过设置周期的断言改为：下一轮不额外等待完整周期，但保留 100ms 最小退避；未超时仍等待剩余 750ms。

- [x] **Step 2: Run the focused test and verify it fails**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore`

Expected: 轮询节奏检查失败，现有实现返回 0 秒而不是 100ms。

- [x] **Step 3: Write the minimal implementation**

在 `MonitoringLoop.GetDelayAfterIteration` 中将超周期分支改为 100ms，保留请求未超周期时的剩余等待和单次循环不重叠。

- [x] **Step 4: Run the focused test and verify it passes**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore`

Expected: 轮询节奏检查通过。

### Task 2: 累计差分的每周期有效显示与图表样本

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/Services/DeviceRepository.cs`
- Modify: `src/LanDeviceMonitor.App/Models/DeviceModels.cs`
- Modify: `src/LanDeviceMonitor.App/MainWindow.xaml.cs`
- Modify: `src/LanDeviceMonitor.App/Services/TrafficHistoryBuffer.cs`

- [x] **Step 1: Write the failing regression test**

在实时速率回归中加入：明确瞬时 0 必须显示 0；瞬时字段缺失时，只有累计值刚产生正向差分才使用区间平均速率；累计值不变且没有瞬时字段时不追加样本。该断言应捕获旧实现拿图表旧值覆盖当前 0 的问题。

- [x] **Step 2: Run the regression and verify it fails for the expected reason**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 失败信息显示明确 0 被旧图表值覆盖，或累计无变化仍被标记为新样本。

- [x] **Step 3: Write the minimal implementation**

让 `SpeedSample` 把“本轮存在有效原始速率/新累计差分”与“数值是否变化”分开；明确的瞬时 0 直接生效，缺少新证据时不沿用旧值、不把未知数据标记为真实样本。

- [x] **Step 4: Run the regression and verify it passes**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 实时速率回归通过，明确 0、累计差分和无新证据断言均通过。

### Task 3: 恢复连续折线视觉

**Files:**
- Modify: `src/LanDeviceMonitor.App/Controls/TrafficChart.cs`
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`

- [x] **Step 1: Write the failing chart-data assertion**

让历史缓冲区在连续有效轮询样本下保持每秒槽位，并保留前段尚未采到数据的 placeholder；测试不得要求用未知数据伪造样本。

- [x] **Step 2: Run the regression and verify it fails**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --realtime-rate-regression`

Expected: 当前实现会把跨 placeholder 的真实点绘制成连续线段，但不会把 placeholder 当作真实数值点。

- [x] **Step 3: Write the minimal implementation**

恢复连续真实样本的系列绘制和面积填充；placeholder 保留为未知槽位并跳过，不生成伪造值，但真实端点之间保持连续连线。保留 hover、缩放和主题资源。

- [x] **Step 4: Run tests and inspect the diff**

Run: `dotnet build tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore` and `git diff --check`。

Expected: 构建成功、无 diff 检查错误，测试断言通过。

### Task 4: 在线/离线列表切换动画

**Files:**
- Modify: `tests/LanDeviceMonitor.Tests/Program.cs`
- Modify: `src/LanDeviceMonitor.App/MainWindow.xaml.cs`

- [x] **Step 1: Write the failing animation-duration regression**

约束列表切换动画必须保持原来的 1040ms；先用检查捕获不应擅自改短的实现。

- [x] **Step 2: Preserve duration while reducing overlap and making completion safe**

保持切换动画 1040ms，并保留左侧列表与右侧详情原有的同时启动时序；切换期间不再把监控刷新逐项写入可见 ListBox，禁用旧列表点击，切换前后清理透明度/位移动画时钟，用 generation 丢弃旧完成回调，避免快速反向切换时旧列表再次提交状态；仅在现有动画期间为列表和详情视觉树使用临时 BitmapCache，完成后清除缓存，并让详情历史复用稳定的数据源和行视图，不改变原有动画时长、缓动或切换内容。

- [x] **Step 3: Run the focused regression**

Run: `dotnet run --project tests/LanDeviceMonitor.Tests/LanDeviceMonitor.Tests.csproj --configuration Release --no-restore -- --filter-animation-regression`

Expected: 原 1040ms 动画时长回归通过。

### Task 5: 发布与实际验收

**Files:**
- Modify: `src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj`

- [ ] **Step 1: Bump the existing dev version once and build**

延续 `v0.1.0-dev.N` 序号，发布 self-contained `win-x64` 到项目 `dist` 目录。

- [ ] **Step 2: Verify published files against the exact deployment directory**

使用 SHA-256 对比 `dist/LanDeviceMonitor.App` 与 `C:\Path\To\LanDeviceMonitor`，要求缺失、额外、哈希不一致均为 0。

- [ ] **Step 3: Launch and leave the final app open**

启动 `C:\Path\To\LanDeviceMonitor\局域网设备助手.exe`，短时检查窗口、版本、设置默认值、标题时间每周期变化和图表连续线，不主动关闭最终进程。

- [ ] **Step 4: Commit, tag, and push**

创建唯一 annotated dev tag，核对工作区干净、标签指向 HEAD、远程分支和标签已推送。
