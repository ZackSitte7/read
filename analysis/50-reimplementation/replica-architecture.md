# 复刻版架构说明

复刻目标是保留原软件的**交易终端架构**：登录、行情、下单、成交、持仓、日志与本地配置，
移除全部**厂商认证**能力：授权码校验、MAC 指纹绑定、云端认证接口与版本准入。

## 1. 分层结构

```text
Replica.Desktop (WinForms)
├── LoginForm            登录前置、经纪商、资金账号
└── MainForm             行情表 / 下单面板 / 委托 / 成交 / 持仓 / 日志
        │
        ▼
Replica.Core (平台无关)
├── TradingSession       组合行情与交易，统一事件、校验、日志
├── Abstractions          IMarketDataClient / ITradeClient
├── Models                Account / Instrument / Quote / Order / Trade / Position
├── Services              InstrumentRepository / UserRepository / IniFile
└── Mock                  MockMarketDataClient / MockTradeClient
        ▲
        │
Replica.Ctp (Windows 原生适配)
├── CtpMarketDataClient   thostmduserapi_se.dll 行情链路
└── CtpTradeClient        thosttraderapi_se.dll 交易链路

Replica.Simulator (控制台)
└── 无界面验证 登录 → 行情 → 下单 → 成交 → 持仓
```

`Replica.Core` 不引用 WinForms，也不引用 CTP。桌面端只负责表现与交互，
交易链路可以独立在 Linux 上用 Mock 实现验证。

## 2. 原软件组件映射

| 原软件 | 复刻版 | 说明 |
| --- | --- | --- |
| `TForm1` 登录窗 | `LoginForm` | 仅保留交易前置、行情前置、经纪商、资金账号、密码 |
| `TYYWIN` 主窗 | `MainForm` | 行情表、下单面板、委托/成交/持仓/日志页签 |
| `TYYCTRLWIN` 控制台 | `MainForm` 的“日志”页签 | 用 `TradingSession.Log` 事件承载 |
| `TUSEREDIT` 用户管理 | `UserRepository` | 只读取 `brokerid`/`userid`/`title`/`address`，不读取 `shouquan` |
| `TCONFIGINI` 配置窗 | `IniFile` | 保留 ini 读取能力，认证相关键值不实现 |
| `Instruments.xml` | `InstrumentRepository` | 合约代码、交易所、合约乘数、最小变动价位、保证金率 |
| `Users.xml` | `UserRepository` | 兼容原文件格式，忽略授权字段 |
| `thostmduserapi_se.dll` | `CtpMarketDataClient` | 当前为适配器骨架，需原生 C ABI 桥接层 |
| `thosttraderapi_se.dll` | `CtpTradeClient` | 当前为适配器骨架，需原生 C ABI 桥接层 |
| `PnL/*.csv`、`SysLog/*.txt` | 日志页签 | 首版不落盘，避免与样本数据混淆 |

## 3. 主链路时序

```text
LoginForm
  → 构造 Account（无授权码、无 MAC）
  → TradingSession.ConnectAsync
      → ITradeClient.ConnectAsync
      → IMarketDataClient.ConnectAsync
  → TradingSession.SubscribeAsync(合约列表)
  → MainForm 显示

MainForm 下单
  → TradingSession.PlaceOrderAsync（校验合约、数量、限价）
  → ITradeClient.PlaceOrderAsync
  → OrderUpdated / TradeReceived / PositionUpdated
  → MainForm 更新委托、成交、持仓页签
```

## 4. 已移除的认证要素

原样本的六要素联合校验 `brokerid` / `userid` / `appid` / `shouquan` / `MacAddress` / `ExpireDate`
在复刻版中全部不存在：

- `Account` 没有授权码、MAC、到期日字段；
- 登录流程没有任何 HTTP 认证请求，也不读取网卡；
- `Users.xml` 解析显式跳过 `shouquan`、`appid`；
- 主窗体没有授权状态、心跳或版本准入提示。

## 5. CTP 接入边界

CTP 行情/交易 API 以 C++ 抽象类导出，不能直接 P/Invoke。计划中的
`ReplicaCtpBridge` 提供 C ABI，把 `OnRtnDepthMarketData`、`OnRtnOrder`、
`OnRtnTrade`、`OnRspQryInvestorPosition` 回调转换为 `Replica.Core` 事件。
当前版本在 Windows 之外或缺少原生库时抛出带说明的异常，不影响 Mock 链路。

## 6. 构建与验证

```bash
dotnet build Replica.sln -c Release
dotnet run --project src/Replica.Simulator -c Release
```

桌面端目标框架为 `net8.0-windows`，在 Linux 上通过
`EnableWindowsTargeting` 完成编译；运行需要 Windows 桌面环境。
