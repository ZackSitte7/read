# Order Trader v1.0

订单流（Order Flow）虚拟盘交易终端。C++17 + Win32 原生 GUI，行情来自
[TqSdk](https://www.shinnytech.com/tqsdk-brief)（Python 子进程桥接），
除行情接入外全部逻辑由 C++ 实现。

- 单文件 `OrderTrader.exe`（静态链接，无 Qt / MFC / VC 运行库依赖）
- 500ms tick 切片、五档盘口、DOM 价格梯、本地虚拟撮合
- 实时模拟盘 / 最近一万条 tick 历史回放（多合约按时间戳对齐）
- 20 个页面（F1–F12 快速切换），每页可放多个 DOM 梯子
- 账户余额、当日盈亏、手续费、逐笔盈亏、持仓、收益曲线

## 目录结构

```
order-trader/
├── src/
│   ├── main.cpp              WinMain 入口
│   ├── app/                  App 编排、登录窗口、主窗口、信息面板
│   ├── bridge/               Python 子进程桥接（JSON Lines）
│   ├── core/                 行情切片、回放引擎、虚拟撮合引擎
│   ├── ui/                   主题、GDI 绘制、DOM 价格梯控件
│   └── util/                 JSON、Win32 辅助、日志、配置
├── bridge/tqsdk_bridge.py    TqSdk 行情桥接脚本
├── config/order-trader.json  默认配置（账号、页面、梯子尺寸）
├── res/                      图标、manifest、版本资源
├── scripts/                  构建脚本（内置运行时）
├── docs/使用说明.txt          终端用户说明
└── Makefile                  交叉编译 / 打包
```

## 构建

需要 MinGW-w64 交叉编译工具链（Linux 上构建 Windows 产物）：

```bash
sudo apt-get install g++-mingw-w64-x86-64-posix
make -j4          # -> dist/OrderTrader.exe
```

打包（含内置 Python 运行时与 tqsdk，目标机无需安装 Python）：

```bash
bash scripts/build-runtime.sh   # -> ../runtime/python-win-x64
make package                    # -> dist/order-trader-v1.0-win-x64.zip
```

## 运行

解压 `order-trader-v1.0-win-x64.zip` 后双击 `OrderTrader.exe`：

1. 登录窗口输入 TqSdk 账号与密码，验证通过后进入主界面；
2. 主界面顶部切换「实时模拟盘 / 行情回放」，回放会拉取当前页各合约
   最近 10000 条 tick 并按时间戳对齐回放；
3. 工具栏输入合约代码（如 `SHFE.rb2701`，可省略交易所前缀）点「添加」，
   当前页面即出现该合约的 DOM 梯子；
4. 梯子第二列左键挂买、第四列右键挂卖；左侧输入框设置左/右键手数
   （默认 1 / 2）；空格撤销全部挂单；ESC 复位所有梯子价格中心；
   拖动梯子右边缘 / 下边缘可统一调整全部梯子的宽 / 高。

行情桥接使用 `python\tqsdk_bridge.py`，若发布包未内置 Python，则依次查找
`python\python.exe`、系统 `PATH` 中的 `python.exe` / `python3.exe`。

## 配置

`config/order-trader.json`：

| 字段 | 说明 |
| --- | --- |
| `user` / `password` | 登录账号（登录成功后回写） |
| `initialBalance` | 虚拟账户初始资金 |
| `defaultCommission` | 未取到合约手续费时的兜底值（元/手） |
| `ladderWidth` / `ladderHeight` | DOM 梯子尺寸，0 表示自动 |
| `replaySpeedIndex` | 回放倍速档位（0.5x/1x/2x/5x/10x） |
| `lastSymbol` | 上次输入的合约 |
| `pages` | 20 个页面的合约列表 |

配置写入 exe 同目录 `config/`；不可写时回退到 `%APPDATA%\OrderTrader\`。

## 桥接协议

C++ 通过 stdin/stdout 与 Python 交换 JSON Lines：

- 请求：`login` / `symbol` / `subscribe` / `ticks` / `unsubscribe_all` / `shutdown`
- 响应：`ready` / `login_result` / `symbol_info` / `quote` / `tick` / `ticks` / `log` / `error` / `bye`

tick 行固定 28 项：`ts,last,bid1,ask1,total,oi,delta,side` +
`ask_price1..5,ask_volume1..5,bid_price1..5,bid_volume1..5`。

## 说明

本项目为虚拟盘，不连接任何实盘柜台；所有成交均在本地撮合引擎内完成，
手续费取自 `quote.commission`（接口信息），净盈亏不计手续费。

## 版本

v1.0
