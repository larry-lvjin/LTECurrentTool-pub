# LTE 电流自动化测试工具（LTECurrentTool）

> 面向射频 / 终端产测的 LTE 各频段工作电流自动化测量工具。通过 CMW500 综测仪建立 LTE 连接、控制功率，
> 由 66319D 电源以 `MEAS:CURR?` 多次积分平均采集工作电流并统计，全流程无人值守并一键导出 Excel 报告。

![platform](https://img.shields.io/badge/platform-Windows-blue)
![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)
![UI](https://img.shields.io/badge/UI-WPF-purple)
![status](https://img.shields.io/badge/version-V3.3-green)

---

## 目录

- [简介](#简介)
- [功能特性](#功能特性)
- [硬件与软件要求](#硬件与软件要求)
- [快速开始](#快速开始)
- [使用说明](#使用说明)
- [测试矩阵](#测试矩阵)
- [测量原理](#测量原理)
- [固定测试参数](#固定测试参数)
- [测试结果与导出](#测试结果与导出)
- [项目结构](#项目结构)
- [构建与发布](#构建与发布)
- [文档](#文档)

---

## 简介

**LTECurrentTool** 是一个基于 WPF / .NET 10 的 Windows 桌面应用，用于自动化测量终端设备在各 LTE 频段、
不同功率控制（TPC）条件下的工作电流。它把原本需要人工逐点操作仪器的繁琐流程完全自动化：

- 通过 **VISA** 控制 **R&S CMW500** 综测仪，逐个频段 / 信道建立 LTE 小区、驱动终端 Attach 并设置目标功率；
- 通过 **VISA/GPIB** 控制 **Keysight/Agilent 66319D** 移动通信电源，用 `MEAS:CURR?` 多次积分平均采集工作电流；
- 对读数进行统计（平均值 / 最大值 / 最小值），并将 640 个测试点结果一键导出为 Excel 报告。

程序内置了完整的默认测试矩阵（无需外部配置文件即可开测），并采用“仅在 FDD↔TDD 切换时重连”的智能策略，
显著缩短整批测试时间。

## 功能特性

- **一键批量测试**：内置 640 个测试点（490 FDD + 150 TDD），支持按 Band 勾选 / 全选 / 全不选。
- **双仪器协同**：CMW500（LTE 信令）+ 66319D（电流采集）通过 VISA 统一驱动。
- **积分平均采样**：每个测试点用 `MEAS:CURR?` 连续读取 20 次，每次是仪器内部对约 500 点（约 0.5 s）积分后的平均值，合计约 10 s；相比单次读数，均值标准差从约 8% 降到约 1.8%。
- **稳健统计**：输出平均电流（主结果）、最大 / 最小值与有效样本数，并自动丢弃负值、超量程（9.91E37 哨兵）等物理无效读数。
- **智能重连**：同模式下不同 Band/信道直接切换，仅在 FDD↔TDD 切换时重建连接，节省约 88–92% 的连接时间。
- **测试可控**：支持测试过程中的暂停 / 继续与取消。
- **自动保存**：测试结束（含中途取消）自动把结果 Excel 与完整系统日志写入程序目录下的 `AutoSave/` 文件夹，
  并在下次开测前自动归档上一轮结果，最大限度防止数据丢失。
- **深色科技风 UI**：实时日志、进度与状态显示（UI 仅展示最近日志，完整日志另存文件）。
- **Excel 报告导出**：基于 EPPlus，导出结构化测试结果。
- **自包含单文件发布**：CI 打 tag 自动构建免安装 EXE（无需目标机预装 .NET 运行时）。

## 硬件与软件要求

### 测试仪器

| 设备 | 型号 | 用途 | 默认连接地址 |
|------|------|------|--------------|
| 综测仪 | Rohde & Schwarz **CMW500** | 建立 LTE 小区、信令与功率控制 | `TCPIP0::192.0.2.10::INSTR`（LAN/VISA） |
| 直流电源 | Keysight/Agilent **66319D** | `MEAS:CURR?` 积分平均采集工作电流 | `GPIB0::5::INSTR`（GPIB，通道 1） |

> 66319D 默认输出 **4.0 V**、电流限制 **3.0 A**（可在 `src/LTECurrentRules.cs` 中查看固定参数）。

### 运行环境

- Windows 10/11（x64）
- **NI-VISA** 或 **Keysight IO Libraries Suite**（提供 VISA 运行时与 GPIB 支持）
- GPIB 接口卡 / USB-GPIB 适配器（用于连接 66319D）
- 若使用源码运行：**.NET 10 SDK**

### 关键依赖（NuGet）

| 包 | 版本 | 用途 |
|----|------|------|
| `NationalInstruments.Visa` | 23.8.0 | VISA 仪器通信 |
| `EPPlus` | 7.5.4 | Excel 报告导出 |
| `System.IO.Ports` | 9.0.0 | 串口支持 |

## 快速开始

### 方式一：下载发布版（推荐）

1. 前往仓库 [Releases](https://github.com/larry-lvjin/LTECurrentTool/releases) 页面。
2. 下载最新的自包含单文件 `LTECurrentToolV*.exe`（无需安装 .NET 运行时）。
3. 确认已安装 NI-VISA / Keysight IO Libraries，直接运行 EXE。

### 方式二：源码构建

源码位于 `src/` 目录。

```bash
# 克隆仓库
git clone https://github.com/larry-lvjin/LTECurrentTool.git
cd LTECurrentTool/src

# 还原并运行（需 .NET 10 SDK）
dotnet restore
dotnet run

# 或用 Visual Studio 2022+ 打开 src/LTECurrentTool.slnx 后 F5 运行
```

发布自包含单文件 EXE（在 `src/` 目录下执行）：

```bash
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o publish
```

## 使用说明

1. **连接仪器**：填写 CMW500 地址（VISA/TCPIP）与 66319D 地址（GPIB），分别点击连接。
2. **选择测试项**：在 Band 列表中勾选待测频段，或使用“全选 / 全不选”。
3. **开始测试**：点击开始，程序按 FDD → TDD 顺序逐点执行；日志区实时显示进度与状态。
4. **过程控制**：测试中可暂停 / 继续，或随时取消；取消后已完成的结果仍会自动保存。
5. **查看结果**：每个测试点显示平均电流及统计指标。
6. **导出报告**：测试完成后可手动导出 Excel；程序也会在 `AutoSave/` 中自动留存一份。

> 连接失败排查：确认 NI-VISA / Keysight IO Libraries 已安装、GPIB 适配器已连接、仪器已开机、地址正确
> （66319D 默认 GPIB 地址 5）。

## 测试矩阵

默认内置 **640 个测试点**，覆盖：

- **FDD（490 点）**= 49 个 FDD 信道 × 10 种 TPC 测试类型
  - Band：`B1 B2 B3 B4 B5 B7 B8 B12 B13 B17 B18 B19 B20 B25 B26 B28 B66`
  - 每个 Band 含 Low / Mid / High 三个信道（**B13 仅 Mid**）
- **TDD（150 点）**= 15 个 TDD 信道 × 10 种 TPC 测试类型
  - Band：`B34 B38 B39 B40 B41`（各含 Low / Mid / High）

**10 种 TPC 测试类型**（按上行 RB 分两组，各 5 种）：

| 组别 | Min Power | 0 dBm | 10 dBm | 15 dBm | Max Power |
|------|-----------|-------|--------|--------|-----------|
| UL12（上行 12 RB） | ✅ | ✅ | ✅ | ✅ | ✅ |
| UL50（上行 50 RB） | ✅ | ✅ | ✅ | ✅ | ✅ |

> 测试顺序：先测完全部 FDD，再切换到 TDD，全程仅发生 **1 次**双工模式切换，最大化复用连接。

## 测量原理

- **带宽 / RB**：下行固定 10 MHz（50 RB），上行按测试组为 12 RB 或 50 RB。
- **采样方式**：每个测试点通过 `MEAS:CURR?` 连续读取 **20 次**。每次读数是 66319D 内部对约 **500 个采样点、约 0.5 s** 积分后给出的一个直流平均值，20 次合计约 **10 s**。
- **主结果**：对 20 次读数取平均，作为该点的平均工作电流。仪器内置积分天生无负值、无纹波瞬时伪值，多次平均进一步将均值标准差从单次约 8% 降到约 1.8%。
- **异常剔除**：丢弃负值以及超过电流限制（3 A）的读数（含 9.91E37 超量程哨兵），仅用有效读数参与统计。
- **发射保持**：测量前先启动 CMW500 的 BLER 持续模式，确保终端持续发射 PUSCH（避免进入 DRX 休眠使电流偏低）。
- **收敛等待**：RRC 连接建立后等待 3 秒，确保 TPC 功率收敛后再开始读数。

> **历史说明**：早期版本曾用 66319D Digitizer 以 1 ms 间隔整帧分段采样（2000 / 5000 点，配合 MAD 稳健离群剔除）。
> 相关实现与常量（`SEGMENT_MAX_POINTS`、`TPC_SAMPLE_POINTS` 等）仍保留在代码中，但当前默认测量路径已改为上述
> `MEAS:CURR?` 多次积分平均，不再走 Digitizer 分段采集。

## 固定测试参数

以下参数由测试规范约定，集中定义于 [`src/LTECurrentRules.cs`](src/LTECurrentRules.cs)，运行时不可修改：

| 参数 | 值 |
|------|-----|
| CMW500 信道带宽 | 10 MHz（50 RB） |
| CMW500 参考信号功率（RS EPRE） | −85 dBm/15 kHz |
| 66319D 输出电压 | 4.0 V |
| 66319D 电流限制 | 3.0 A |
| `MEAS:CURR?` 读取次数 | 20 次/测试点（约 10 s） |
| 单次读数积分时间 | ≈ 0.5 s（仪器内置，约 500 点） |
| 测量前等待 | 3000 ms |
| Attach / 连接超时 | 60 s |
| 重连策略 | 仅 FDD↔TDD 切换时重连 |

## 测试结果与导出

结果导出为 Excel（基于 EPPlus），每行一个测试点，共 **11 列**（见 [`src/MainWindow.xaml.cs`](src/MainWindow.xaml.cs) 的 `ExportExcelToFile`）：

| 列 | 含义 |
|------|------|
| BAND / 信道类型 / 信道(EARFCN) | 测试点标识 |
| TPC Setup / Target Power / 上行RB | 测试条件 |
| 平均(mA) | 平均工作电流（主结果，20 次读数的平均） |
| 最大(mA) | 20 次读数中的最大值 |
| 最小(mA) | 20 次读数中的最小值 |
| 样本数 | 参与统计的有效读数个数（正常为 20） |
| 状态 | 该测试点的执行结果 |

> 测试点模型见 [`src/BandConfig.cs`](src/BandConfig.cs)。字段口径详见 [`docs/测试结果列说明.md`](docs/测试结果列说明.md)。
> 注：早期版本导出的 P95、Max−P95 指标已不再输出（对应字段仍保留在代码中以向后兼容，但不参与导出）。

## 项目结构

```
LTECurrentTool/
├── src/                             # 应用源码
│   ├── App.xaml / App.xaml.cs           # 应用入口
│   ├── MainWindow.xaml / .cs            # 主界面与测试流程编排、自动保存
│   ├── CMW500Controller.cs              # R&S CMW500 控制（SCPI over VISA/TCPIP）
│   ├── PowerSupplyController.cs         # 66319D 电源/Digitizer 控制（SCPI over VISA/GPIB）
│   ├── BandConfig.cs                    # 测试点模型 + 默认 640 点矩阵
│   ├── LTECurrentRules.cs               # 固定测试参数与规则常量
│   ├── AssemblyInfo.cs                  # 程序集/主题配置
│   ├── LTECurrentTool.csproj / .slnx    # 项目 / 解决方案文件
│   ├── LTECurrentTool.ico               # 应用图标
│   ├── analyze_excel.py                 # 辅助脚本：Excel 数据分析
│   └── convert_excel_to_md.py           # 辅助脚本：Excel 转 Markdown
├── docs/                            # 设计与实现文档
├── research/                        # 调研资料（预留）
└── .github/workflows/release.yml    # CI：打 tag 自动构建并双仓库发布
```

## 构建与发布

仓库配置了 GitHub Actions（[`.github/workflows/release.yml`](.github/workflows/release.yml)）：向仓库推送形如
`v*` / `V*` 的 tag 时，在 `windows-latest` 上构建**自包含单文件 EXE**，并按 csproj 的目标平台自动选择
`win-x64` / `win-x86` / `win-arm64`，随后发布到 **两处 Release**：

- **私有源码仓库**（`larry-lvjin/LTECurrentTool`）：EXE + GitHub 自动附带的完整源码包，用于内部存档。
- **公开分发仓库**（`larry-plaud/LTECurrentTool`）：仅 EXE，面向同事 / TMS 平台分发。

```bash
# 打 tag 触发自动构建与双仓库发布
git tag V3.3
git push origin V3.3
```

产物命名形如 `LTECurrentToolV3.3.exe`，下载后可直接运行，无需预装 .NET 运行时。
也可在 Actions 页面手动触发（`workflow_dispatch`）仅构建产物、不创建 Release。

> **公开分发说明**：公开仓库的 EXE 由 workflow 经 GitHub API 推送，源码不会 push 过去。
> 若需向公开仓库同步说明文档，在仓库根目录新建 `README.public.md`（只写对外的用途、界面、环境与使用步骤）；
> workflow 只同步该文件，找不到时会跳过同步，不会回退到本内部 README。
> 公开发布依赖名为 `RELEASE_TOKEN` 的仓库 Secret，配置步骤见 workflow 文件顶部注释。

## 文档

`docs/` 目录包含各专题的设计与调试记录，例如：

- [LTE电流规则.md](docs/LTE电流规则.md) — 测试规范与参数依据
- [电流采样优化方案.md](docs/电流采样优化方案.md) — 采样与统计优化
- [数据波动分析报告.md](docs/数据波动分析报告.md) — 波动来源与稳健统计
- [测试顺序优化说明.md](docs/测试顺序优化说明.md) — 测试顺序与重连策略
- [架构简化完成记录.md](docs/架构简化完成记录.md) — 架构演进记录
- [测试结果列说明.md](docs/测试结果列说明.md) — 导出字段口径
- [测试数据找回指南.md](docs/测试数据找回指南.md) — AutoSave 数据恢复

> 采样方式演进记录（Digitizer 整帧分段 → `MEAS:CURR?` 积分平均）保留在以下历史文档中，供追溯参考，不代表当前实现：
> [66319D_Digitizer完整方案.md](docs/66319D_Digitizer完整方案.md)、
> [5000点整帧采样最终配置.md](docs/5000点整帧采样最终配置.md)、
> [TPC自适应采样方案.md](docs/TPC自适应采样方案.md)、
> [瞬态判定规则.md](docs/瞬态判定规则.md)。
