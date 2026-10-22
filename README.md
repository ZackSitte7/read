# REA 逆向工程工作区

以 [morluto/rea](https://github.com/morluto/rea)（REA: Reverse Engineer Anything）为核心的本地逆向分析工程。
目标样本为 `1102` 期货交易客户端（Windows PE32 / x86，Delphi + C++Builder 运行时）。

## 当前状态

| 项目 | 状态 |
| --- | --- |
| Git 仓库 | `main` 分支，origin 已连接 |
| Node.js | v24.22.1（满足 REA >= 24.11 要求） |
| REA CLI | `rea-agents` 6.7.0，`rea` 已安装到全局 PATH |
| 原生分析引擎 | Ghidra 12.1.4 PUBLIC（`GHIDRA_INSTALL_DIR`） |
| 目标样本 | `target/1102`，224 个文件，约 136 MB（不入库） |
| 证据基线 | `artifacts/manifests/` 中的 SHA-256 清单 |
| 交易终端复刻版 | `src/`，.NET 8 + WinForms，Mock 主链路已验证 |

> `rea doctor` 中 Ghidra 12.1.4 的安装、平台、架构、Headless、反编译器与 JDK 检查均通过。
> Hopper、IDA、JADX、Kuna 为可选引擎，未安装不影响 Windows PE 的 Ghidra 静态分析。

## 目录结构

```text
.
├── analysis/                    # 分析工作区（按阶段隔离）
│   ├── 00-triage/               # 初筛、样本登记、IOC
│   ├── 10-static/               # 静态分析：反编译、字符串、导入表
│   ├── 20-dynamic/              # 动态调试与运行时观测
│   ├── 30-network/              # 网络协议、抓包与回放
│   ├── 40-data/                 # 配置与数据格式（XML/CSV/INI）
│   └── 50-reimplementation/     # 行为重建、验证用例
├── artifacts/
│   ├── evidence/                # 证据包（不入库）
│   ├── exports/                 # 导出结果（不入库）
│   ├── manifests/               # 哈希与文件清单（入库）
│   └── snapshots/               # 运行时快照（不入库）
├── config/rea/rea.env           # REA / Ghidra 环境变量
├── docs/                        # 方法论文档与 ADR
├── scripts/                     # 环境校验、样本清点
├── src/                         # 交易终端复刻版（.NET 8）
│   ├── Replica.Core/            # 模型、接口、会话、Mock、XML/INI 读取
│   ├── Replica.Ctp/             # CTP 行情/交易适配层
│   ├── Replica.Desktop/         # WinForms 登录窗与主窗体
│   ├── Replica.Simulator/       # 无界面主链路验证
│   └── Replica.sln
├── target/1102/                 # 目标样本（不入库）
└── tools/rea/                   # REA 上游源码（git submodule，固定提交）
```

## 快速开始

```bash
# 1. 加载分析引擎环境变量
source config/rea/rea.env

# 2. 校验工具链与 REA 就绪状态
make verify

# 3. 重新生成目标样本哈希清单
make inventory

# 4. 直接使用 REA CLI
rea --help
rea doctor
```

## 交易终端复刻版

按原软件架构重建的 .NET 8 交易终端，保留登录、行情、下单、成交、持仓与日志，
不包含授权码、MAC 绑定、云端认证与版本准入。详细映射见
[复刻版架构说明](analysis/50-reimplementation/replica-architecture.md)。

```bash
# 编译（Linux 可编译 WinForms，运行桌面端需要 Windows）
dotnet build Replica.sln -c Release

# 无界面验证主链路：登录 → 行情 → 下单 → 成交 → 持仓
dotnet run --project src/Replica.Simulator -c Release
```

桌面端运行后勾选“使用模拟行情与模拟撮合”即可在无 CTP 环境下操作；
CTP 适配层当前为骨架，原生桥接层 `ReplicaCtpBridge` 尚未接入。

### TqSdk 行情

桌面端默认行情通道为 **TqSdk（天勤）**，通过 `bridge/tqsdk_bridge.py`
子进程与 C# 以 JSON Lines 通信，行情与交易共享同一个 `TqApi`。

1. 目标机安装 Python 3.10+ 与 `tqsdk`：双击 `install-tqsdk.bat`，或执行
   `python -m pip install -U tqsdk`。
2. 账号写入 `config/tqsdk.local.json`（随仓库保存，发布时自动复制到程序目录）：

   ```json
   { "user": "alyzc26", "password": "130100", "tradeMode": "kq" }
   ```

   `tradeMode`：`none` 仅行情、`kq` 快期模拟、`account` 实盘。
3. 登录窗选择「TqSdk 天勤行情」+「TqSdk 快期模拟」即可；没有 Python 时可选
   「TqSdk 本地模拟行情」走内置撮合引擎（`--sim`），无需任何依赖。

发布目录包含 `bridge/tqsdk_bridge.py`、`tqsdk.local.json` 与安装脚本。

## 目标样本

- 主程序：`target/1102/0931.exe`（2026-10-15，最新版本）
- 历史版本：`0107B(1).exe`、`0304B.exe`、`0401.exe`、`1210B(1).exe`、`1211A.exe`、`1212B.exe`、`1219C.exe`、`1222B.exe`、`1223F.exe`
- 关键依赖：`bass.dll`（音频）、`cc32c290mt.dll`（C++Builder 运行时）、`thostmduserapi_se.dll` / `thosttraderapi_se.dll`（CTP 行情/交易 API）、`YYXX.dll`
- 运行数据：`Instruments.xml`、`Users.xml`、`config.ini`、`PnL/*.csv`、`SysLog/*.txt`

原始压缩包保存在 `target/source/1102.zip`（50 MB，SHA-256 `0a3267c5…22e3e2c`），随 Git 提交保存；解压内容 `target/1102/` 仍不入库。

## 工作约定

1. **只读优先**：静态分析在副本上进行，不修改 `target/` 中的原始样本。
2. **证据可追溯**：每个结论记录到 `analysis/`，原始证据放 `artifacts/evidence/`，并标注文件偏移、函数地址或 REA Evidence ID。
3. **版本对照**：多版本样本差异放入 `analysis/10-static/`，用 REA 的 `compare-application-versions` / 二进制布局对比能力验证。
4. **最小副作用**：动态分析前记录网络与文件系统影响；样本中的账号、日志、盈亏数据不对外提交。
