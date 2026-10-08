# DL/T 645-2007 电表红外抄读工具（只读）

通过红外通讯头（USB 转串口芯片 CH340）读取 DL/T 645-2007 电表数据的 Windows 桌面软件。
**只有读取功能**，不包含任何写入、设置、控制命令（程序只会发出 11H 读数据、12H 读后续数据、13H 读通信地址三种命令）。

---

## 1. 技术选型：C# + WPF（.NET 8）

| 对比项 | C# + WPF (.NET 8) ✅ | Python + PySide6 |
| --- | --- | --- |
| 单 exe 免安装 | `dotnet publish` 原生支持单文件、自带运行时，约 65 MB，启动快 | PyInstaller 单文件运行时需先解压到临时目录，启动慢，容易被杀毒软件误报 |
| 串口 / 设备识别 | `System.IO.Ports` 稳定；读注册表即可识别 CH340（VID 1A86），无需管理员 | pyserial 也可以，但打包后体积和兼容性较差 |
| USB 拔插检测 | 直接处理 `WM_DEVICECHANGE` 消息 | 需要额外处理 Qt 原生事件 |
| 现场电脑兼容性 | Win10/11 x64 直接运行，不依赖 VC++ 运行库 | 依赖 Qt 插件、VC++ 运行库，旧系统偶有缺 DLL |
| 单元测试 / 分层 | 协议层是普通 .NET 类库，xUnit 直接测，Linux CI 也能跑 | 也可以 |

结论：目标是“现场抄表人员拷一个 exe 就能用”，所以选 C# + WPF。

---

## 2. 整体架构

```
┌──────────────────────────────── Dlt645.App（WPF 界面层，net8.0-windows）──────────────────────────────┐
│  MainWindow / Views (ReadPageView, CustomDiPageView, ResultsGrid)                                      │
│  ViewModels (MainViewModel, ReadPageViewModel, CustomDiPageViewModel)   Services (设置/诊断导出/对话框) │
└───────────────────────────────────────────────┬────────────────────────────────────────────────────────┘
                                                │ 只依赖下面的类库
┌──────────────────────────────── Dlt645.Core（类库，net8.0，可在任何平台单元测试）──────────────────────┐
│ Communication  MeterClient：收发、过滤回显、重试、后续帧、地址/DI 核对、报文事件                        │
│ DataItems      数据项定义层：DataItems.json 配置表 → DataItemCatalog；DataFormat (BCD/小数/符号/时间/ASCII)│
│ Protocol       协议编解码层：FrameCodec（组帧/拼帧/校验）、MeterAddress、DataId、ErrorWord、FrameDescriber│
│ Transport      串口通讯层：ISerialTransport、SerialPortTransport、PortEnumerator（CH340 识别）          │
│ Simulation     模拟电表：SimulatedMeter + SimulatedTransport（含红外回显、后续帧、异常应答）            │
│ Errors / Logging / Export   错误码与中文提示、按日期分文件日志、CSV/XLSX 导出                            │
└────────────────────────────────────────────────────────────────────────────────────────────────────────┘
tests/Dlt645.Core.Tests   xUnit 单元测试（84 个，使用标准示例报文）
```

一次读取的数据流：

```
界面勾选 → ReadRequest(数据项, 费率→DI1, 月份/次数→DI0) → MeterClient.ReadDataAsync
   → FrameCodec.Build: FE FE FE FE 68 A0..A5 68 11 04 (DI0..DI3)+33H CS 16
   → ISerialTransport.Write / Read（50ms 一片，拼接）
   → FrameCodec.Scan：跳过任意 FE → 校验帧头/长度/CS/帧尾 → D7=0 的帧判定为回显并丢弃
   → 91H 正常 / B1H 有后续帧（发 12H 继续读）/ D1H 异常（错误字逐位解析）
   → DataItemDecoder：数据域 −33H → 低字节在前反转 → BCD → 小数位 / 符号位 / 时间 / ASCII
   → ReadResultRow → 表格显示 / 导出
```

---

## 3. 界面草图

```
┌───────────────────────────────────────────────────────────────────────────────────────────────────┐
│ 串口连接  串口[COM3  【CH340】 USB-SERIAL CH340      ▼][刷新] 波特率[2400▼] 数据位[8▼]            │ ┌──────────┐ ┌─────────────┐
│          校验[偶校验 E▼] 停止位[1▼] [恢复默认]                                                    │ │ 打开串口 │ │ ● 通讯正常  │
│          超时[1500]ms 重试[2]次  ☐ 模拟电表模式（不接硬件演示 / 测试）                            │ └──────────┘ └─────────────┘
│          (未检测到 CH340 时此处红字提示检查驱动/连线)                                              │
├───────────────────────────────────────────────────────────────────────────────────────────────────┤
│ 表　　号 [AAAAAAAAAAAA] [读取表号] [重置为公共表号] │ [一键读取常用数据] [取消读取(Esc)] │ [导出CSV] [导出Excel] │
├───────────────────────────────────────────────────────────────────────────────────────────────────┤
│ [E201] 超时无应答：1500ms 内只收到红外头自身的回显，电表没有应答                       [复制]   │ ← 错误提示条
│ 可能原因：• 红外头没有对准… • 距离… • 强光…      排查建议：• 重新对准… • 先读取表号…  [关闭]   │   （出错时才出现）
├────────────┬──────────────────────────────────────────────────────────────────────────────────────┤
│ ★ 常用数据  │ 电能量                                                                              │
│ ⚡ 电能量   │ 数据项 [全选][全不选]                                                               │
│ 📈 最大需量 │   有功      ☑组合有功电能 ☑正向有功电能 ☑反向有功电能                               │
│ 🩺 实时变量 │   无功      ☐组合无功1电能 ☐组合无功2电能                                           │
│ ⚙ 电表参数 │   四象限无功 ☐第一象限… ☐第二象限… ☐第三象限… ☐第四象限…                            │
│ 🕘 事件记录 │ 费率   (总)(尖)(峰)(平)(谷) [全部费率]  费率1~4依次对应尖/峰/平/谷                  │
│ 📅 冻结数据 │ 月份   (当前)(上1月)(上2月)…(上12月)  [全选13个][仅当前][清空]   ← 月份选择器        │
│ </> 自定义DI│ [读取选中项 (F5)] [清空结果]  已选 2 个数据项，共需通讯 6 次                          │
│            │ ┌序号┬数据项──────┬费率┬时段──┬数值─────┬单位┬状态┬数据标识┬原始数据┬读取时间┐      │
│            │ │ 1  │正向有功电能 │ 总 │ 当前 │ 8137.50 │kWh │成功│00010000│…       │…       │      │
│            │ │ 2  │正向有功电能 │ 总 │ 上1月│ 7852.69 │kWh │成功│00010001│…       │…       │      │
├────────────┴──────────────────────────────────────────────────────────────────────────────────────┤
│ 报文监视  ■发送 ■接收 ■回显 ■错误   [复制全部][清空][导出TXT][导出诊断信息] ☑自动滚动               │
│ 14:02:01.123 [发送] FE FE FE FE 68 AA AA AA AA AA AA 68 11 04 33 33 34 33 AE 16                    │
│                     读数据 地址=AAAAAAAAAAAA DI=00010000（正向有功电能·总·当前）                     │
│ 14:02:01.160 [回显] FE FE FE FE 68 AA … 16   红外回显（本机发出的帧），已过滤                        │
│ 14:02:01.402 [接收] FE FE 68 78 56 34 12 00 00 68 91 08 33 33 34 33 … 16  正常应答 …              │
├───────────────────────────────────────────────────────────────────────────────────────────────────┤
│ 最近通讯：成功 · 读数据 00010000 · 耗时 245 ms     [■■■■□□] 正在读取 3/6…   数据项：内置配置  v1.0.0│
└───────────────────────────────────────────────────────────────────────────────────────────────────┘
```

* 连接指示灯：灰＝未连接，蓝＝已打开串口，绿＝通讯正常（收到过有效应答），橙＝通讯超时，红＝串口异常断开。
* 快捷键：`F5` 读取当前页，`Esc` 取消读取，自定义 DI 页输入框内按 `Enter` 读取。

---

## 4. 目录结构

```
Dlt645Reader.sln
publish.bat                         一键测试 + 发布单文件 exe
.github/workflows/build.yml         GitHub Actions：Windows 上测试并产出 exe
src/Dlt645.Core/
  Protocol/      Hex.cs  ControlCode.cs  MeterAddress.cs  DataId.cs  ErrorWord.cs
                 Dlt645Frame.cs  FrameCodec.cs  FrameDescriber.cs
  Transport/     ISerialTransport.cs  SerialPortTransport.cs  SerialSettings.cs  PortEnumerator.cs
  DataItems/     DataItems.json(配置表)  DataFormat.cs  DataItemDefinition.cs  DataItemCatalog.cs
                 DataItemDecoder.cs  ReadRequest.cs
  Communication/ MeterClient.cs  CommOptions.cs  MonitorEntry.cs  ReadResultRow.cs
  Simulation/    SimulatedMeter.cs  SimulatedTransport.cs
  Errors/        ErrorCode.cs  ErrorCatalog.cs  MeterException.cs
  Logging/       FileLogger.cs
  Export/        ResultExporter.cs (CSV / XLSX)
src/Dlt645.App/
  App.xaml(.cs)  MainWindow.xaml(.cs)  app.manifest(asInvoker，无需管理员)
  Views/         ReadPageView  CustomDiPageView  ResultsGrid
  ViewModels/    MainViewModel  ReadPageViewModel  CustomDiPageViewModel  PageViewModel ...
  Services/      AppPaths  AppSettings  DiagnosticsExporter  Dialogs
  Infrastructure/ ObservableObject  RelayCommand  Converters
tests/Dlt645.Core.Tests/
  FrameCodecTests  AddressAndDiTests  DataFormatTests  CatalogTests  MeterClientTests  ExportTests
```

---

## 5. 功能与协议实现要点

### 串口
* 自动枚举串口，读注册表（`HARDWARE\DEVICEMAP\SERIALCOMM` 与 `Enum\USB\VID_1A86...`）识别 CH340，下拉框标注【CH340】。
* 手动“刷新”；收到 `WM_DEVICECHANGE` 自动刷新；另有 2 秒轮询兜底。已打开的串口消失时立即判定为“串口异常断开”。
* 参数：波特率 1200/2400/4800/9600（默认 2400）、数据位 8、偶校验 E、停止位 1，“恢复默认”一键还原；超时（默认 1500ms）和重试次数（默认 2）可配置且即时生效。
* 上次使用的串口和参数保存在 `settings.json`。

### 协议（`FrameCodec` / `MeterClient`）
* 帧格式 `68 A0~A5 68 C L DATA CS 16`，地址低字节在前、BCD；发送前加 4 个 `FE`；接收跳过任意数量 `FE`。
* 数据域发送 `+33H`、接收 `-33H`。
* 校验帧头（第二个 68H）、长度（L≤200）、CS、帧尾 16H，失败时给出具体原因，例如“校验和错误：计算值 54H，帧内值 55H”。
* 红外半双工回显：控制码 D7=0 的帧必然是主站自己发出的，直接过滤并在报文窗口标灰显示。
* 后续帧：应答控制码 D5=1（B1H/B2H）时自动发送 12H（DI + 帧序号）继续读取并拼接。
* 读通信地址 13H→93H；读数据 11H→91H/B1H，异常 D1H 错误字逐位解析：其他错误 / 无请求数据 / 密码错或未授权 / 通信速率不能更改 / 年时区数超 / 日时段数超 / 费率数超。
* 应答表号与请求不一致（请求地址中的 AA 字节视为通配）→ 数据照常显示，同时给出 E205 警告。
* 连续 3 项无应答自动停止本次批量读取，避免长时间空等。

### 数据项（配置表 `src/Dlt645.Core/DataItems/DataItems.json`）
* 电能量（组合有功/正反向有功/组合无功1、2/四象限无功/正反向视在/分相）× 费率 × 当前~上12月
* 最大需量及发生时间 × 费率 × 当前~上12结算日
* 实时变量：三相电压/电流、零线电流、有功/无功/视在功率、功率因数、相角、频率、温度、电池电压等（支持自动刷新）
* 电表参数：日期星期、时间、需量周期、通信地址、表号、资产管理编码、额定电压/电流、准确度等级、常数、型号、生产日期、协议版本、运行状态字、费率数、结算日
* 事件记录：失压/断相/失流次数及累计时间、失压/断相记录、掉电、编程、电表清零、需量清零、事件清零、校时、开表盖、开端钮盒（次数 + 最近 N 次记录）
* 冻结数据：定时、瞬时、整点、日冻结
* 自定义数据标识：输入任意 4 字节 DI，显示原始数据（接收顺序和高字节在前），按配置表或指定格式解析，未知项尝试按 BCD/ASCII 解析
* 一键读取常用数据（配置表中 `common: true` 的项）

**扩展数据项无需改代码**：把 `DataItems.json` 复制到 exe 同目录下修改，重启程序即生效（加载失败会自动回退到内置配置并在报文窗口提示）。新增的分类会自动出现在左侧菜单。字段说明见文件头部注释。

### 错误码

| 码 | 含义 | 码 | 含义 |
| --- | --- | --- | --- |
| E101 | 未选择串口 | E201 | 超时无应答 |
| E102 | 串口被占用 | E202 | 校验错误 |
| E103 | 串口打开失败 | E203 | 帧不完整 |
| E104 | 串口异常断开（被拔出） | E204 | 帧格式错误（帧头/帧尾/长度） |
| E105 | 串口发送失败 | E205 | 应答表号与请求不一致 |
| E106 | 未检测到 CH340 设备 | E206 | 应答类型不符 |
| E107 | 串口未打开 | E207 | 应答数据标识不一致 |
| E301 | 电表异常应答（附错误字解析） | E208 | 后续帧读取失败 |
| E401 | 表号格式错误 | E402 | 数据标识格式错误 |
| E403 | 数据解析失败 | E501 | 导出失败 |
| E999 | 未知错误 | | |

每个错误码都配有“可能原因”和“排查建议”（见 `Errors/ErrorCatalog.cs`），显示在界面顶部的错误提示条中，可一键复制。

### 日志与诊断
* 日志：程序目录 `logs\yyyy-MM-dd.log`，记录操作、全部收发报文和异常。程序目录不可写时自动改到 `%LocalAppData%\Dlt645Reader`。
* “导出诊断信息”：zip 包含软件版本、系统信息、串口参数、串口列表、当前错误、最近报文、最近错误、最近 3 天日志。

---

## 6. 编译、测试与打包

### 环境
* Windows 10/11 x64
* [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（只有编译需要，运行发布后的 exe 不需要）
* 可选：Visual Studio 2022 17.8+（“.NET 桌面开发”工作负载），直接打开 `Dlt645Reader.sln`

### 命令行
```bat
:: 运行单元测试
dotnet test tests\Dlt645.Core.Tests

:: 调试运行
dotnet run --project src\Dlt645.App

:: 打包为单个 exe（或直接双击 publish.bat）
dotnet publish src\Dlt645.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -p:DebugSymbols=false -o publish
```
得到 `publish\Dlt645Reader.exe`（约 65 MB），自带 .NET 运行时，拷到任意电脑双击即可运行，不需要安装、不需要管理员权限（manifest 为 `asInvoker`）。

> 如需更小的体积（约 1 MB），可改用 `--self-contained false`，但目标电脑需安装 “.NET 8 桌面运行时”。

### GitHub Actions
* 推送代码后 `.github/workflows/build.yml` 会在 Windows 上运行测试并发布 exe，在 Actions 页面的 Artifacts 中下载 `Dlt645Reader-win-x64`。
* 推送 `v*` 标签（如 `git tag v1.0.1 && git push origin v1.0.1`）时，`.github/workflows/release.yml` 会自动打包并发布到 **Releases** 页面（`Dlt645Reader.exe` 与 zip 包）。

> Core 类库和单元测试可在 Linux/macOS 上运行；WPF 项目设置了 `EnableWindowsTargeting`，在非 Windows 系统上也能编译，但只能在 Windows 上运行。

---

## 7. 现场使用步骤

1. 插入红外通讯头，确认串口下拉框中出现【CH340】；没有时按红字提示检查驱动/连线。
2. 参数保持默认（2400 / 8 / E / 1），点“打开串口”，指示灯变蓝。
3. 红外头对准电表红外窗口，点“读取表号”；成功后表号自动填入，指示灯变绿。
4. 点“一键读取常用数据”，或在左侧选择分类、勾选数据项/费率/月份后按 F5。
5. 需要时点“导出 CSV / 导出 Excel”，文件中包含表号和读取时间。
6. 遇到问题看顶部错误提示；无法解决时点“导出诊断信息”，把 zip 发给工程师。

没有电表时可勾选“模拟电表模式”→“启动模拟”，所有功能（含回显过滤、后续帧、异常应答）都可以演示。

---

## 8. 说明与限制

* 只实现了读取（11H/12H/13H），不发送任何写入、校时、清零、冻结、拉合闸等命令。
* 事件记录中较长的记录（如失压记录）只在配置表中定义了“发生时刻”等前几个字段，其余部分以原始数据显示；需要时可在 `DataItems.json` 中补充字段定义。
* 按国内习惯把费率 1~4 显示为尖/峰/平/谷，如电表费率定义不同，以电表实际为准（DI1 = 费率号）。
* ASCII 类参数（资产编号、型号等）按“低字节在前”反转后显示；个别电表不反转时，可在自定义 DI 页用“ASCII”格式对照原始数据。
