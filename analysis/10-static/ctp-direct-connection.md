# 0401.exe 的 CTP 直连实现与 SimNow 账户分析

- 主样本：`target/1102/0401.exe`（PE32 / x86，Delphi + C++Builder，VMProtect）
- SHA-256：`a4b960f451201210794c5fc247e97861436cfeb565c81e9d19cf95d598e323a2`（6,514,176 字节）
- 镜像基址 `0x400000`，版本字符串 `Apr  1 2026` / `21:38:22`（与 `SysLog` 首行“版本 2026年 4月 1日 21:38:22”一致）
- 配套分析对象：`YYXX.dll`、`yy.dll`、`yyVolume.dll`、`thostmduserapi_se.dll`、`thosttraderapi_se.dll`
- 数据证据：`Users.xml`、`config.ini`、`HQAddress.xml`、`SysLog/2026年09月18日.txt`、`SysLog/2026年10月22日.txt`
- 方法：pefile 导入/导出/节表解析 + VA↔文件偏移换算 + `objdump -d` 交叉引用 + REA 6.7.0（Ghidra 12.1.4）反编译 + GB18030 字符串提取

> 地址均为虚拟地址（VA，含 ImageBase）。结论按「已确认 / 推断」标注。

## 1. 结论摘要

1. **0401.exe 不静态链接 CTP**。导入表里没有 `thostmduserapi_se.dll`、`thosttraderapi_se.dll`，也没有 `YYXX.dll`；全部 CTP 能力来自运行时 `LoadLibraryA("YYXX.dll")` + 13 次 `GetProcAddress`。
2. **`YYXX.dll` 才是真正的 CTP 封装层**（MSVC C++，非 Delphi）。它静态导入 CTP 的两个工厂函数 `CreateFtdcMdApi` / `CreateFtdcTraderApi`，并实现 `CThostFtdcMdSpi` / `CThostFtdcTraderSpi` 回调，再通过 15 个 C 导出函数把行情、交易、查询能力暴露给 Delphi 侧。
3. **Delphi 与 DLL 之间用“字段名=值”的字符串包传参**，字段名常量表为 `method / FrontAddr / BrokerID / UserID / AppID / AuthCode / PassWord`。明文口令不落盘，`Users.xml` 里只有 `address / brokerid / userid / appid / shouquan`。
4. **`YYXX.dll` 的登录主流程被 VMProtect 虚拟化**：`StartCptJY`（`0x100015c0`）与 `StartCptHQ`（`0x10001870`）入口即 `push imm; jmp 0x10011xxx` 进入 `.vmp1`，无法静态还原 `RegisterFront → RegisterSpi → Init → ReqAuthenticate → ReqUserLogin` 的完整调用序列；但**未虚拟化的邻接代码暴露了参数结构与日志节点**，可与 `SysLog` 逐条对齐。
5. **日志来源已定位**：`[*]0 用户登陆`、`[*]1 客户端认证成功`、`[*]3 认证响应`、`[*]4 交易登录请求成功`、`[*]6 行情登录请求成功` 等文本常量全部在 `YYXX.dll` 的 `.rdata`（`0x10003154`–`0x1000323c`），编号即日志类别 ID，由 0401.exe 套上 `[*]` 前缀写入 `SysLog`。
6. **李骑潼是 SimNow 模拟账户**：`brokerid=9999`、`appid=simnow_client_test`、`address=tcp://182.254.243.31:30001`；登录时交易前置直接取 `address` 字段，行情前置按 `config.ini` 的 `HQAddress`（实跑为 `tcp://140.207.230.97:61213`）。
7. **0401.exe 还内置了 SimNow 全天模拟前置**：交易 `tcp://182.254.243.31:40001`、行情 `tcp://182.254.243.31:40011`，对应界面开关 `SimNow全天模拟交易` / `使用SimNow全天模拟行情`；勾选时会覆盖用户自填地址。
8. **DLL 缺失有云端兜底**：本地找不到 `YYXX.dll` / `yyVolume.dll` 时，会从 `http://sunmengze.cc/YYXX.txt` / `http://sunmengze.cc/yyVolume.txt` 下载后保存（同一分发域名也用于授权与配置）。

## 2. 0401.exe 的接入方式：运行时动态加载

### 2.1 导入表证据（已确认）

`0401.exe` 的 15 个静态导入 DLL 为：

```
ADVAPI32.DLL  KERNEL32.DLL  NETAPI32.DLL  VERSION.DLL  WINSPOOL.DRV  COMCTL32.DLL
COMDLG32.DLL  GDI32.DLL     SHELL32.DLL   USER32.DLL   WINMM.DLL     OLE32.DLL
OLEAUT32.DLL  WS2_32.DLL    WTSAPI32.DLL
```

**不含** `thostmduserapi_se.dll`、`thosttraderapi_se.dll`、`YYXX.dll`、`yyVolume.dll`。CTP 与封装层全部是动态加载。

对比同期样本 `0931.exe` 同为动态加载方式，只是多了 `CRYPT32.DLL`（授权加密用），说明该架构在多个版本间稳定。

### 2.2 加载与解析代码（已确认）

加载器位于 `0x449468` 起，被一个 Delphi 类实例（下称“DLL 管理对象”，基址存于 `[ebp-0x10]`）持有：

```asm
449468: mov  DWORD PTR [ebp-0x4c],0x6c      ; 行号/异常帧标记
44946f: mov  DWORD PTR [esp],0x85816d       ; "YYXX.dll"
449476: call 0x853d78                       ; LoadLibraryA
44947e: mov  [ebp-0x100],eax
44948d: mov  DWORD PTR [eax+0x494],ecx      ; +0x494 = HMODULE
449493: cmp  DWORD PTR [ebp-0x100],0x0
44949a: je   0x449732                       ; 失败 → 弹窗
```

字段偏移与解析到的导出（按 `GetProcAddress` 顺序）：

| 对象偏移 | 导出名 | 字符串 VA | 用途 |
| --- | --- | --- | --- |
| `+0x494` | （HMODULE） | `0x85816d` | YYXX.dll 模块句柄 |
| `+0x498` | `StartCptHQ` | `0x858c9e` | 启动行情 API |
| `+0x4c8` | `SubscribeMarketData` | `0x858ca9` | 订阅行情 |
| `+0x4d4` | `UnSubscribeMarketData` | `0x858f09` | 退订行情 |
| `+0x4a0` | `StartCptJY` | `0x858f1f` | 启动交易 API（登录） |
| `+0x4a4` | `QryInstrument` | `0x858f2a` | 查合约 |
| `+0x4a8` | `QryTradingAccount` | `0x858f38` | 查资金 |
| `+0x4ac` | `QryInvestorPosition` | `0x858f4a` | 查持仓 |
| `+0x4b0` | `QryInvestorPositionDetail` | `0x858f5e` | 查持仓明细 |
| `+0x4b4` | `QryOrder` | `0x858f78` | 查报单 |
| `+0x4b8` | `OrderInsert` | `0x858f81` | 报单 |
| `+0x4bc` | `OrderAction` | `0x858f8d` | 撤单 |
| `+0x4c4` | `Release` | `0x858f99` | 释放 API |
| `+0x4cc` | `QryTrade` | `0x858fa1` | 查成交 |

共 **13 个导出**。`YYXX.dll` 另有 `NewOrder` 与 `GetTradingDay` 两个导出，0401.exe 未解析（下单走 `OrderInsert`）。

字段先被填成 Delphi 侧同名桩方法（`0x4e72bc` 等），随后被 `GetProcAddress` 结果覆盖；`0x4e72bc` 桩内部仍保留 `"StartCptJY"` / `"method"` / `"FrontAddr"` … 常量，说明桩是“未加载时的降级实现”。

### 2.3 加载失败分支（已确认）

`0x449732` 分支使用两条字符串：

| VA | 文本 |
| --- | --- |
| `0x858fe9` | `运行所需要的文件缺失,请检查是否被杀毒软件隔离?` |
| `0x859018` | `缺少文件,无法运行` |

即 YYXX.dll 缺失时程序无法进入行情/交易链路，这与 `配置项 PW=` 为空的情况无关，纯粹是 DLL 装载失败。

### 2.4 云端兜底下载（已确认）

同一字符串池（`0x45756d` 起）还包含：

| 本地文件 | 下载地址 | 提示 |
| --- | --- | --- |
| `YYXX.dll` | `http://sunmengze.cc/YYXX.txt` | `本地未找到 YYXX.dll，尝试从云端获取...` / `已从云端成功下载 YYXX.dll` / `云端下载  YYXX.dll 失败: ` |
| `yyVolume.dll` | `http://sunmengze.cc/yyVolume.txt` | 同上（yyVolume） |
| `Nomoney.wav` | `http://sunmengze.cc/Nomoney.wav` | 同上（音频） |
| `PositionWin` | `http://sunmengze.cc/PositionWin.txt` | 同上（持仓窗口程序） |

所以 `YYXX.dll` 即使被删也能在联网环境下自愈——这解释了为什么“杀毒软件隔离”只是提示而非致命。

### 2.5 第二个加载器：yyVolume.dll（已确认）

`0x445750` 处还有一套独立加载逻辑，`LoadLibraryA("yyVolume.dll")`（`0x8581ee`），解析字段为：

| 对象偏移 | 导出名 | 字符串 VA |
| --- | --- | --- |
| `+0x494` | （HMODULE） | `0x8581ee` |
| `+0x49c` | `StartCptHQ` | `0x858c9e` |
| `+0x4cc` | `SubscribeMarketData` | `0x858ca9` |
| `+0x4d0` | `UnSubscribeMarketDataMultiple` | `0x858cbd` |

失败提示为 `没有发现刷新成交量需要的文件`（`0x858cdb`）。这条链路只做行情，用于“成交量刷新/白格”模式（同段字符串含 `上传`、`成交量/白格`）。

## 3. YYXX.dll：CTP 封装层

- SHA-256：`b76f5859437fdec5dcf9cd890180786301bcc87340241dd5e4cdbe7d306fdb86`（57,856 字节）
- ImageBase `0x10000000`；节表 `.text`(0x1000, 0x1e00) / `.rdata` / `.data` / `.rsrc` / `.vmp0`(0x7000) / `.vmp1`(0x8000, 0x9a00) / `.reloc`
- RTTI 类型名：`.?AVCMdSpi@@`、`.?AVCTraderSpi@@`、`.?AVCThostFtdcMdSpi@@`、`.?AVCThostFtdcTraderSpi@@`
- 调试路径：`J:\CtpDLL6.7.10\Release\YY.pdb`、模块名 `YY.dll`

### 3.1 静态导入（已确认）

| IAT VA | 导入符号 | 来源 DLL |
| --- | --- | --- |
| `0x100030a0` | `?CreateFtdcMdApi@CThostFtdcMdApi@@SAPAV1@PBD_N1_N@Z` | `thostmduserapi_se.dll` |
| `0x100030a8` | `?CreateFtdcTraderApi@CThostFtdcTraderApi@@SAPAV1@PBD_N@Z` | `thosttraderapi_se.dll` |

两个 IAT 槽在反汇编里**没有任何直接引用**（VMProtect 用运行时计算的地址访问），这是“调用点被虚拟化”的直接证据。

### 3.2 导出表（15 个，已确认）

| Ordinal | 名称 | RVA | 说明 |
| --- | --- | --- | --- |
| 1 | `GetTradingDay` | `0x1e20` | 薄封装：`mov ecx,[0x1000555c]; mov eax,[ecx]; jmp [eax+0xc]` |
| 2 | `NewOrder` | `0x1d10` | 0401.exe 未使用 |
| 3 | `OrderAction` | `0x1c00` | 撤单 |
| 4 | `OrderInsert` | `0x1bb0` | 报单 |
| 5 | `QryInstrument` | `0x1920` | |
| 6 | `QryInvestorPosition` | `0x1a30` | |
| 7 | `QryInvestorPositionDetail` | `0x1a90` | |
| 8 | `QryOrder` | `0x1b20` | |
| 9 | `QryTrade` | `0x19d0` | |
| 10 | `QryTradingAccount` | `0x1980` | |
| 11 | `Release` | `0x1d80` | |
| 12 | `StartCptHQ` | `0x1870` | **虚拟化** |
| 13 | `StartCptJY` | `0x15c0` | **虚拟化** |
| 14 | `SubscribeMarketData` | `0x1de0` | |
| 15 | `UnSubscribeMarketData` | `0x1e00` | |

`GetTradingDay` 的形态说明封装层内部是「全局 C++ 对象 + 虚表分派」：`0x1000555c` 持有对象指针，`[eax+0xc]` 为虚表槽位。

派生的 `yy.dll`（`c03cf93c…`，15,872 字节）导出 14 个（无 `QryTrade`），`yyVolume.dll`（`fa0b1252…`，10,752 字节）只导入行情 API、只导出 3 个行情函数；三者是同源不同裁剪的构建产物。

### 3.3 登录流程入口被虚拟化（已确认）

```
10001870: 55              push ebp            ; StartCptHQ
10001871: 8b ec           mov  ebp,esp
10001873: 53              push ebx
10001874: 56              push esi
10001875: 57              push edi
10001876: 68 b8 53 68 28  push 0x286853b8     ; VMProtect 入口密钥
1000187b: e9 c4 fb 00 00  jmp  0x10011444     ; → .vmp1
```

`StartCptJY`（`0x100015c0`）同形：`push 0xea5e109; jmp 0x10011023`。

`.vmp1` 内的分派器位于 `0x100018f1`，单字节滚动解密后 `push [eax*4+0x10007000]; ret` 跳表执行：

```asm
100018f1: 8a 0e        mov  cl,[esi]
100018f3: 30 d9        xor  cl,bl
100018f5: fe c1        inc  cl
100018f7: c0 c1 07     rol  cl,7
100018fa: 80 c1 c7     add  cl,0xc7
100018fd: d0 c9        ror  cl,1
100018ff: 80 f1 72     xor  cl,0x72
10001902: fe c9        dec  cl
10001904: 30 cb        xor  bl,cl
10001906: 83 c6 01     add  esi,1
10001909: 0f b6 c1     movzx eax,cl
1000190c: ff 34 85 00 70 00 10  push DWORD PTR [eax*4+0x10007000]
10001913: c3           ret
```

因此 `RegisterFront` / `RegisterSpi` / `SubscribePrivateTopic` / `Init` / `ReqAuthenticate` / `ReqUserLogin` 的**具体调用顺序无法从静态代码直接读出**，只能由未虚拟化的邻接代码与运行期日志反推。

### 3.4 未虚拟化的登录骨架（已确认）

`0x10001144`（`.text` 内，可读）是交易登录请求的构造段：

```asm
1000113f: 68 7c 31 00 10                 push 0x1000317c            ; "用户登陆"
10001144: 6a 00                          push 0                     ; 日志类别 0
10001146: ff 15 78 54 00 10              call [0x10005478]          ; log(id, text)
1000114c: 6a 58                          push 0x58                  ; 结构体大小 88
10001155: e8 79 1a 00 00                 call 0x10002bd3            ; memset
; —— 把 4 个全局配置串拷进栈上结构体 ——
10001160: mov al,[ecx+0x10005550] …      ; 串 A
10001173: mov al,[ecx+0x10005540] …      ; 串 B
10001190: mov al,[ecx+0x100054c4] …      ; 串 C
100011a1: push 0xb; push 0x100054c4; push eax; call [0x10003098]   ; strncpy(dst, 串C, 11)
100011c0: mov al,[ecx+0x10005500] …      ; 串 D
; —— 取全局 API 对象，按虚表槽位调用 ——
100011d1: mov ecx,[0x10005560]
100011dd: push eax                        ; nRequestID（自增计数器 0x100054c0）
100011ea: mov eax,[edx+0x2c]
100011ed: call eax                        ; 虚表 +0x2c
100011ef: test eax,eax
100011f3: push 0x10003188 ; 成功 → "客户端认证成功"，类别 1
100011fc: push 0x10003198 ; 失败 → "客户端认证失败"，类别 2
```

`0x10001220` 是响应回调：先打 `0x100031a8` = `认证响应`（类别 3），再解析 `CThostFtdcRspAuthenticateField`；`0x100012e1` 处 `call [edx+0x40]` 后打 `交易登录请求成功/失败`（类别 4/5）。

`0x10001760` 是行情侧登录结果：

```asm
10001760: sub esp,0x110 … memset(0x110)
10001xxx: mov ecx,[0x1000555c]; call [eax+0x30]
        成功 → push 0x100031e0 "行情登录请求成功" + 类别 6
        失败 → push 0x100031f4 "行情登录请求失败" + 类别 7
```

SPI 转发桩（未虚拟化，直接 `jmp` 到配置的虚表槽）：

```asm
100017d0: push [ebp+0x14]; push [ebp+0x10]; push [ebp+0xc]; push [ebp+8]
100017df: call [0x10005480]; ret 0x10
100017f0: … call [0x1000547c]; ret 0x10
10001810: push [ebp+8]; call [0x10005484]; ret 0x4
```

`0x10005460`–`0x100054bc` 是一张指向 `.text`/`.vmp1` 的处理函数表，登录骨架里的 `call [0x10005478]` 等即经由该表进入 VM 保护的实现。

### 3.5 日志类别 ID（已确认字符串，部分为推断映射）

`.rdata` `0x10003154`–`0x1000323c` 的常量与推导出的 ID：

| ID | 文本 VA | 文本 | 触发点 |
| --- | --- | --- | --- |
| 0 | `0x1000317c` | `用户登陆` | 登录会话开始 |
| 1 | `0x10003188` | `客户端认证成功` | `ReqAuthenticate` 返回 0 |
| 2 | `0x10003198` | `客户端认证失败` | `ReqAuthenticate` 返回非 0 |
| 3 | `0x100031a8` | `认证响应` | `OnRspAuthenticate` / 登录响应 |
| 4 | `0x100031b4` | `交易登录请求成功` | `ReqUserLogin` 返回 0 |
| 5 | `0x100031c8` | `交易登录请求失败` | `ReqUserLogin` 返回非 0 |
| 6 | `0x100031e0` | `行情登录请求成功` | MdApi `ReqUserLogin` 返回 0 |
| 7 | `0x100031f4` | `行情登录请求失败` | MdApi `ReqUserLogin` 返回非 0 |
| 8 | `0x10003208` | `行情服务断开` | `OnFrontDisconnected`（行情） |
| 9 | `0x10003218` | `行情长时间未收到` | 心跳超时 |
| 11 | `0x10003154` | `触发断线重连` | 重连分支（`0x100010b4` 直接 `push 0xb`） |
| 12 / 13 | `0x10003164` / `0x10003170` | `已经登出` / `登出失败` | `ReqUserLogout`（虚表 `+0x44`）返回值判定 |

另有 `0x1000322c 交易服务卸载`、`0x1000323c 行情服务卸载`。

日志函数签名为 `log(int id, const char* text)`（cdecl，先压 text 再压 id），函数指针存 `[0x10005478]`。

前缀 `[*]` 由 0401.exe 拼接：`0x869ca6` 处为 `[*]` 常量，引用点 `0x4e6ba0` / `0x4e6ce2`（`[*]` + 类别/文本 的字符串拼接），其后紧跟 `0x86?…` 的 `分组已重新订阅` 等宿主文案。

## 4. Delphi ↔ DLL 的传参协议（已确认）

`0401.exe` 在 `0x869cce`–`0x869d3c` 连续存放一组字段名常量，与 `StartCptJY` / `OrderInsert` 的参数一一对应：

**登录参数**（顺序即字符串池顺序）：

```
method / FrontAddr / BrokerID / UserID / AppID / AuthCode / PassWord
```

**报单参数**：

```
InvestorID / OrderRef / InstrumentID / OrderPriceType / Direction / CombHedgeFlag
TimeCondition / ContingentCondition / LimitPrice / CombOffsetFlag / VolumeCondition
MinVolume / StopPrice / ForceCloseReason / IsAutoSuspend / UserForceClose
VolumeTotalOriginal / ExchangeID
```

**撤单参数**：`OrderAction / OrderSysID / ExchangeID / OrderRef / InstrumentID`

**查询**：`QryInstrument / QryTradingAccount / QryInvestorPosition / QryInvestorPositionDetail / QryOrder / QryTrade / Release`

这些字段与 CTP 官方 `CThostFtdcInputOrderField` / `CThostFtdcReqUserLoginField` 成员同名，说明 DLL 内部直接做「字段名 → 结构体成员」映射，Delphi 侧不需要知道 CTP 的二进制布局。

调用点（`0x449d95`）：

```asm
449d98: mov eax,[eax+0x4a0]        ; StartCptJY
449da4: mov eax,[ebp-0x110]        ; = DLL 管理对象 + 0x560（参数字符串）
449dbe: call 0x8512a8              ; 取 AnsiString 指针
449df4: push …                     ; 空串兜底（0x85816c 为 '\0'）
        call eax                   ; StartCptJY(params)
```

同一函数内 `0x449cb1` 处检查 `BYTE [eax+0x92c]`（“SimNow 全天模拟交易”开关）：

```asm
449cb1: cmp BYTE PTR [eax+0x92c],0x0
449cb8: je  0x449ff9                       ; 未勾选 → 用用户地址
449ce0: push "tcp://182.254.243.31:40001"  ; 勾选 → 覆盖交易前置
449d35: mov BYTE PTR [eax+0x8a4],0x1
449d5e: push "SimNow全天模拟交易"          ; 并在界面提示
```

## 5. CTP 登录时序（SysLog 实证）

### 5.1 成功链路（`SysLog/2026年09月18日.txt`，09:56:57–09:57:05）

```
-------------09:56:57-------------
选择了用户: 李骑潼
-------------09:56:58-------------
选择交易服务:
tcp://182.254.243.31:30001
正在读取成交量.....
读取完毕 未推送: 2255 0成交: 5754
[*]0 用户登陆
[*]1 客户端认证成功
-------------09:57:05-------------
[*]3 认证响应
[*]4 交易登录请求成功
登入认证成功 交易日: 20260918
会话编号: -1703447027
本机时间: 09:57:05
交易所时间: 09:57:05
时间差: 00:00:00
[*]6 行情登录请求成功
查询持仓... → 查询持仓成功 → [fu2610] 持仓 昨:1 多空:3
查询明细... → 查询明细成功
查询报单... → 查询成交...
```

对应到 CTP 回调：

| SysLog 行 | CTP 阶段 |
| --- | --- |
| `选择交易服务: <address>` | Delphi 侧把 `Users.xml.address` 作为 `FrontAddr` 传入 `StartCptJY` |
| `[*]0 用户登陆` | TraderSpi `OnFrontConnected` 后发起认证 |
| `[*]1 客户端认证成功` | `ReqAuthenticate` 返回 0（请求发送成功） |
| `[*]3 认证响应` | `OnRspAuthenticate` 收到响应 |
| `[*]4 交易登录请求成功` | `ReqUserLogin` 返回 0 |
| `登入认证成功 交易日: …` | `OnRspUserLogin` 成功，取 `TradingDay` / `SessionID` / 交易所时间 |
| `[*]6 行情登录请求成功` | MdSpi `OnRspUserLogin` 成功 |
| `查询持仓/明细/报单/成交` | `QryInvestorPosition` / `QryInvestorPositionDetail` / `QryOrder` / `QryTrade` |

`会话编号` 与 `时间差` 是 0401.exe 自己算的（`0x8590??` 段有 `会话编号: ` / `本机时间: ` / `交易所时间: ` / `时间差: ` / `新的会话编号: `）。

### 5.2 失败与重试（`SysLog/2026年10月22日.txt`，10:33:26–10:33:54）

```
[*]0 用户登陆
[*]1 客户端认证成功
[*]3 认证响应
[*]4 交易登录请求成功
[X]3 CTP:不合法的登录          ← OnRspUserLogin 的 ErrorID != 0
-------------10:33:36-------------
正在读取成交量.....
-------------10:33:52-------------
[*]0 用户登陆                  ← 自动重登
[*]1 客户端认证成功
[*]3 认证响应
[*]4 交易登录请求成功
登入认证成功 交易日: 20261022
[*]6 行情登录请求成功
```

可确认两点：

1. `[X]<id> <文本>` 是错误变体，`id=3` 即登录响应，文本直接透传 CTP 的 `ErrorMsg`（`不合法的登录`），**客户端不做二次解释**。
2. 登录失败后 0401.exe 会自动重试，重试前会重新拉取成交量（`正在读取成交量.....`），说明重连路径与首次启动共用同一套初始化。

## 6. 李骑潼账户如何使用 CTP

### 6.1 `Users.xml` 实际字段（已确认）

根标签 `Users`，13 个 `<User>`；李骑潼为第 12 项（0 基）。子标签顺序：

```
WindowHistory → title → address → brokerid → userid → appid → shouquan
```

| 字段 | 值 |
| --- | --- |
| `title` | `李骑潼` |
| `address` | `tcp://182.254.243.31:30001` |
| `brokerid` | `9999` |
| `userid` | `259561` |
| `appid` | `simnow_client_test` |
| `shouquan` | 16 位（`0**************0`，此处按前次约定打码） |

**没有 `password`，也没有 `tradeaddress`**。其余 12 个用户均为实盘（`brokerid` = 2016 / 88888 / 7010 / 8000 / 4080），`appid` 形如 `Wenge_jingqi_1.0`、`client_sunjiafei_1.1`、`YTD_yiyitech_2.0`。

对照 `0401.exe` 的 `Users.xml` 解析常量（`0x8580xx` 段）：`Users.xml` / `title` / `address` / `brokerid` / `userid` / `appid` / `shouquan` / `WindowHistory` / `:` —— 即程序只读这 7 个字段，口令一律登录时手输（`config.ini` 的 `PW=` 为空）。

### 6.2 SimNow 判定（已确认 + 推断）

`brokerid=9999` + `appid=simnow_client_test` + 前置 IP `182.254.243.31` 三项同时指向 SimNow 模拟环境。程序内置的 SimNow 前置只有两个：

| VA | 地址 | 界面文案 |
| --- | --- | --- |
| `0x858fbb` | `tcp://182.254.243.31:40001` | `SimNow全天模拟交易` |
| `0x859041` | `tcp://182.254.243.31:40011` | `使用SimNow全天模拟行情` |

而用户 `address` 写的是 `:30001`（另一组端口）。SysLog 的 `选择交易服务: tcp://182.254.243.31:30001` 证明**未勾选全天模拟时，交易前置就是 `address` 原值**，程序不做端口改写。

### 6.3 交易前置的来源优先级（已确认 + 推断）

1. `Users.xml` 的 `address` —— 实测生效值（`2026年09月18日` / `2026年10月22日` 两次登录均为 `tcp://182.254.243.31:30001`）。
2. 若打开“SimNow 全天模拟交易”（对象 `+0x92c` 非 0），覆盖为 `tcp://182.254.243.31:40001`。
3. 另有 `JYAddress.xml` 解析代码（`0x457dae` = `JYAddress.xml`，含 `格式错误或为空。` / `JYAddress.xml 出错: ` / `JYAddress.xml 失败: `），但**本次压缩包内该文件缺失**，无法确认其字段结构；推断它是按 brokerid 维护交易前置清单的配置文件，缺失时回落到 `address`。

### 6.4 行情前置的来源（已确认）

| 来源 | 值 |
| --- | --- |
| `config.ini [User] HQAddress` | `tcp://140.207.230.97:61213`（东证联通） |
| `SysLog` 启动行 | `默认行情服务: tcp://140.207.230.97:61213` |
| `HQAddress.xml` | 13 个候选行情前置（海通 / 东证联通 / 东证电信 / 创元 / 天鸿 / 中信 / 国富 / 国联 / 盛达 …） |
| SimNow 全天模拟 | `tcp://182.254.243.31:40011` |

即：**行情与交易走两套独立前置**。李骑潼的 SimNow 账户登录时，交易连 `182.254.243.31:30001`，行情仍连 `140.207.230.97:61213`（SysLog 中行情登录在交易登录之后 1 秒成功，`[*]6 行情登录请求成功`）。

### 6.5 认证参数

| CTP 参数 | 值来源 |
| --- | --- |
| `BrokerID` | `Users.xml.brokerid` = `9999` |
| `UserID` | `Users.xml.userid` = `259561` |
| `Password` | 登录框手输，不落盘 |
| `AppID` | `Users.xml.appid` = `simnow_client_test` |
| `AuthCode` | 推断取 `Users.xml.shouquan`（16 位），经 `StartCptJY` 的 `AuthCode` 字段下传 |
| `FrontAddr` | `Users.xml.address`（或被全天模拟覆盖） |

`shouquan` 与 `[AuthCode]` 的对应关系是**推断**：字段名语义（“授权”）与 CTP `ReqAuthenticate` 的鉴权码用途一致，且 `StartCptJY` 参数表里没有其他候选字段；但 DLL 内的解析在 `.vmp1` 中，未能静态证实。

### 6.6 登录后的账户行为（SysLog 实证）

- 持仓：`[fu2610] 持仓 昨:1 多空:3`
- 下单日志：`[ag2612]价:13741.00 方: 0 手: 1 开平: 3 Rf: 140`，回报 `[ag2612] S:a Y: 340097 379ms`
- 风控：`达到了今日最大亏损额 禁止开仓`、`本账号止损金额: 200`
- 撤单异常：`CTP:撤单找不到相应报单`

说明 0401.exe 在 `OnRtnOrder` / `OnRtnTrade` 之上又叠了一层自有风控（`CloudRiskOn=1`，云端风控 0x4? 段日志 `云端风控已连接`）。

## 7. 配置文件职责

| 文件 | 作用 | 关键内容 |
| --- | --- | --- |
| `Users.xml` | 账户列表 + 每账户窗口历史 | `address / brokerid / userid / appid / shouquan`，13 个账户 |
| `config.ini` | 界面尺寸、下单风控、行情前置、开盘抢单 | `HQAddress`、`PriceListRatios=10,25,30,25,10`、`MOrderXSpeed=200`、`MOrderXStop=2200`、9 个 `MOrderTime*`、`MaxCancelGZ/SP/QQ` |
| `HQAddress.xml` | 行情前置候选清单 | 13 条 `名称 + tcp://ip:port` |
| `JYAddress.xml` | 交易前置候选清单（**本次缺失**） | 仅有解析代码与错误提示 |
| `Instruments.xml` / `Instruments(2).xml` | 合约表 | 启动时 `期货 3160 股指 254 期权 6274`，`正在订阅: 9688个合约` |
| `SysLog/*.txt` | 运行日志（GB18030） | 版本行、配置回显、CTP 登录时序、下单/风控记录 |
| `Private.con` / `Public.con` / `QueryRsp.con` / `DialogRsp.con` / `TradingDay.con` | CTP 流文件/回报缓存 | 交易 API `SubscribePrivateTopic` / `SubscribePublicTopic` 的落盘目录 |

`config.ini` 中的 `PW=` 为空，进一步印证口令不持久化。

## 8. 限制与未确认项

1. **`StartCptJY` / `StartCptHQ` 的 CTP 调用序列不可静态还原**：两者入口即跳入 `.vmp1`，虚表槽位（`+0x2c` / `+0x30` / `+0x40`）只能确定“是虚调用”，槽位语义按 CTP 头文件顺序推断（`+0x2c≈ReqAuthenticate`、`+0x40≈ReqUserLogin`），未经运行时验证。
2. **`0x10005460`–`0x100054bc` 处理函数表**指向 `.vmp1`，无法确认每个槽位对应的具体 SPI 回调（`OnRspQryTradingAccount` 等）。
3. **`AuthCode` 是否等同 `shouquan`** 为推断，未在 VM 代码中证实。
4. **`JYAddress.xml` 缺失**，交易前置的完整优先级链只能确认前两级（`Users.xml.address` → 全天模拟覆盖）。
5. **`[*]` 数字与文本的拼接格式**（是否固定为 `[*]%d %s`）由 `0x4e6ba0` / `0x4e6ce2` 的拼接调用推断，未逐字节核对格式化串。
6. 本环境无 Windows/wine，`0401.exe` 未实际运行，全部结论来自静态证据 + 用户提供的 `SysLog`。

## 9. 对复刻项目的结论

- **可以直接对齐的架构**：`IMarketDataClient` / `ITradeClient`（对应 `StartCptHQ` / `StartCptJY`）+ 查询方法组（`QryInstrument` / `QryTradingAccount` / `QryInvestorPosition` / `QryInvestorPositionDetail` / `QryOrder` / `QryTrade`）+ 报单撤单（`OrderInsert` / `OrderAction`）。
- **参数模型应对齐字段名**：登录 7 项、报单 18 项、撤单 5 项，字段名与 CTP 结构体成员一致；复刻版用同名字段即可保证与原版日志/回调语义一致。
- **日志类别 ID 应保留**：`[*]0/1/3/4/6` 与 `[X]<id> <CTP错误>` 的格式是原版可读性的关键，复刻版 `LogHub` 直接沿用该编号体系。
- **双前置分离**：交易前置来自 `Users.xml.address`，行情前置来自 `config.ini.HQAddress` / `HQAddress.xml`，两者互不影响；SimNow 全天模拟用 `:40001` / `:40011` 覆盖。
- **不实现的部分**（按用户要求）：`shouquan` 授权校验、MAC 绑定、云端认证、`sunmengze.cc` 系列 HTTP 请求、DLL 云端自愈下载。
