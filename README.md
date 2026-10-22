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

## 目标样本

- 主程序：`target/1102/0931.exe`（2026-10-15，最新版本）
- 历史版本：`0107B(1).exe`、`0304B.exe`、`0401.exe`、`1210B(1).exe`、`1211A.exe`、`1212B.exe`、`1219C.exe`、`1222B.exe`、`1223F.exe`
- 关键依赖：`bass.dll`（音频）、`cc32c290mt.dll`（C++Builder 运行时）、`thostmduserapi_se.dll` / `thosttraderapi_se.dll`（CTP 行情/交易 API）、`YYXX.dll`
- 运行数据：`Instruments.xml`、`Users.xml`、`config.ini`、`PnL/*.csv`、`SysLog/*.txt`

样本仅解压到本地 `target/`，不进入 Git；原始压缩包保留在 `.uploads/` 并由 SHA-256 清单固定。

## 工作约定

1. **只读优先**：静态分析在副本上进行，不修改 `target/` 中的原始样本。
2. **证据可追溯**：每个结论记录到 `analysis/`，原始证据放 `artifacts/evidence/`，并标注文件偏移、函数地址或 REA Evidence ID。
3. **版本对照**：多版本样本差异放入 `analysis/10-static/`，用 REA 的 `compare-application-versions` / 二进制布局对比能力验证。
4. **最小副作用**：动态分析前记录网络与文件系统影响；样本中的账号、日志、盈亏数据不对外提交。
