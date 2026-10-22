# 授权机制与 MAC 锁定 — 静态分析

- 样本：`target/1102/0931.exe`（PE32 / x86，Delphi + C++Builder）
- SHA-256：`caab20daab4a459539037cc71ae551f12a361310df205e92b169127cae79ddfb`
- 镜像基址：`0x400000`，入口点：`0x2b0c`
- 保护：`.vmp0`（熵 7.999）/ `.vmp1`（熵 7.957），VMProtect 虚拟化
- 方法：字符串扫描 + 导入表 + 立即数交叉引用 + 反汇编（`scripts/pe-xref.py`）；Ghidra 全量反编译因超时未完成

> 地址均为虚拟地址（VA，含 ImageBase）。结论按「已确认 / 推断」标注。

## 1. 结论摘要

1. **授权完全在主程序内实现**，未发现独立授权 DLL；`YYXX.dll`、`libcurl.dll` 等依赖库不含授权字符串。
2. **在线授权**：客户端通过 Indy（`TIdHTTP`）向固定域名提交授权请求，由服务端判定结果；客户端只解析响应并展示错误。
3. **机器绑定以网卡 MAC 为主**：调用 `Netbios` 枚举网卡，取 6 字节 MAC 并格式化为 `%02x%02x%02x%02x%02x%02x`，作为 `MacAddress` 字段随请求上传。
4. **辅助指纹**：`GetComputerNameW` 读取计算机名，`RegOpenKeyExW` / `RegQueryValueExW` 读取注册表信息。
5. **请求参数使用内置密钥加密**：AES 密钥 32 字节、IV 16 字节硬编码在 `.data`，密钥长度以 `push 0x20` 传入，即 AES-256（CBC）。
6. **核心校验分支被虚拟化**：46 处从 `.text` 进入 `.vmp0/.vmp1` 的跨段调用，服务端响应的最终判定逻辑无法通过静态反汇编完整还原。

## 2. 授权接口（已确认）

| 用途 | URL | 字符串 VA |
| --- | --- | --- |
| 私募 MAC 授权 | `http://sunmengze.cc/siyimac/shouquan.aspx` | `0xb1b546` |
| 私募 MAC 授权（备用域名） | `http://yiyikeji.top/siyimac/shouquan.aspx` | `0xb1b59a` |
| 租信授权 | `http://sunmengze.cc/zuxin/shouquan.aspx` | `0xb1b5ee` |
| 租信授权（备用域名） | `http://yiyikeji.top/zuxin/shouquan.aspx` | `0xb1b63e` |
| 认证请求 | `http://yiyikeji.top/AuthRequest.aspx` | `0xb1c532` |
| 认证请求（备用域名） | `http://sunmengze.cc/AuthRequest.aspx` | `0xb1c584` |
| 止损/风控 | `http://sunmengze.cc/StopLossEX.aspx?key=` | `0xb1ad2e` |
| 集团签约 | `http://yiyikeji.top/zuxin/jtqy.aspx?id=` | `0xb1ad76` |
| 用户合同 | `http://sunmengze.cc/UserContract.aspx` | `0xb265ba` / `0xb285ea` |

- 域名 `sunmengze.cc`、`yiyikeji.top` 同时作为配置/行情/更新文件的分发地址（`config.xml`、`Instruments.xml`、`JYAddress.xml`、`HQAddress.xml` 等）。
- 历史样本（`1211A.exe` 等）包含相同 4 个授权 URL，授权逻辑同源。

## 3. MAC 采集与锁定（已确认 + 推断）

### 3.1 导入的硬件/主机信息 API

| DLL | API | IAT VA |
| --- | --- | --- |
| `NETAPI32.DLL` | `Netbios` | `0xb858c0` |
| `KERNEL32.DLL` | `GetComputerNameW` | `0xb85664` |
| `ADVAPI32.DLL` | `RegOpenKeyExW` / `RegQueryValueExW` / `RegSetValueExW` | — |
| `ADVAPI32.DLL` | `CryptAcquireContextW` / `CryptEncrypt` / `CryptImportKey` / `CryptSetKeyParam` | — |

### 3.2 MAC 格式化

- 格式串 `%02x%02x%02x%02x%02x%02x` @ `0xb19bb3`（`.data`）。
- 唯一代码引用位于 `0x436758`：从栈上连续取 6 个字节（`[ebp-0x3e7] … [ebp-0x3e4]` 等）后压栈，调用格式化函数 `0x41f7d8`。
- 该格式化结果随即通过 `0x6c79cc` 赋给字符串对象并返回，调用方位于 `0x436530–0x4367xx` 区间 —— 即 MAC 获取函数。
- `Netbios` 在 `0x436530` 附近的循环中被多次调用，用于枚举网卡（`NCB` 结构 + `NCBRESET`/`NCBASTAT`）。**（推断：枚举后取第一块可用以太网卡）**

### 3.3 绑定字段

- 字段名 `MacAddress` @ `0xb1bdcd`，代码引用：`0x478e8b`、`0x47d3c4`、`0x480d55`、`0x482aa5`。
- 字段名 `ExpireDate` @ `0xb1aa55` / `0xb28547`，与 `MacAddress` 同属授权字段列表，说明服务端同时下发绑定 MAC 与到期时间。
- 采集到的 MAC 字符串随授权请求发送；服务端将其与授权记录中的绑定值比对，客户端不保存独立的 MAC 白名单。

## 4. 请求参数与加密（已确认）

### 4.1 参数名

| 参数 | 字符串 VA |
| --- | --- |
| `brokerid` | `0xb19c65` |
| `userid` | `0xb19c6e` |
| `appid` | `0xb19c75` |
| `shouquan` | `0xb19c7b` |

- 拼接片段：`zhexiuser=` @ `0xb1acf1`、`&&qz=` @ `0xb1acfc`。
- 错误提示：`服务器忙` @ `0xb1ad06`、`服务器响应错误` @ `0xb1ad12`、`软件认证失败` @ `0xb1ad21` —— 表明客户端仅根据服务端返回码提示，不独立计算授权有效性。

### 4.2 加密实现

- 密钥字符串：`1234567890abcdef1234567890yykj12` @ `0xb1c4a2`（32 字节）。
- IV：`yykj121234567890` @ `0xb1c4c3`（16 字节）。
- 调用点 `0x495324`：`push 0x20` + `push 0xb1c4a2`，密钥长度显式为 32 字节 → **AES-256-CBC**。
- IV 引用点 `0x49537f`；加密流程位于 `0x4952f5–0x4957d1` 区间。
- 结论：授权请求中的敏感字段使用固定密钥加密，密钥可直接从二进制提取，加密不构成有效防篡改手段。

## 5. 本地存储

- `target/1102/Users.xml`：13 个用户条目，每条含 `shouquan` 字段（16 位大写字母/数字）及 `ExpireDate` 一类字段；客户端用其缓存授权结果，服务端为准。
- `target/1102/config.ini`：`[User]` 段含 `CloudRiskOn=1`、`HQAddress`、`RunMode` 等，与云端风控/行情连接相关。
- `target/1102/SysLog/*.txt`：记录「云端风控已连接」「客户端认证成功」「交易登录请求成功」，日志中**未出现** MAC 或授权码明文。

## 6. 判定流程（推断，依据上述证据）

```text
启动
  → Netbios 枚举网卡 → 取 MAC → "%02x..." 格式化
  → 读取计算机名 / 注册表辅助信息
  → 读取本地 Users.xml（userid / shouquan / ExpireDate 缓存）
  → 组装 brokerid, userid, appid, shouquan, MacAddress
  → AES-256-CBC（固定 key/IV）加密
  → POST siyimac/shouquan.aspx（失败切 yiyikeji.top）
  → 解析服务端响应：成功则写入 ExpireDate，失败提示「软件认证失败」
  → 定时器复检，MAC 变更 / 到期则拒绝交易登录
```

流程中的加密、请求组装与错误提示均为静态确认；「服务端按 MAC 比对」「定时复检」为基于字段语义与窗体 `LoadTimerTimer` 的推断，尚需动态抓包验证。

## 7. 保护与局限

- `.vmp0` / `.vmp1` 熵接近 8.0，授权判定与响应解析的关键分支位于虚拟化代码中；Ghidra headless 全量分析超时（`provider_timeout`），未完成反编译。
- 未验证项：
  1. 服务端响应报文结构（需 `30-network` 抓包）；
  2. 多网卡/虚拟网卡下的 MAC 选择顺序；
  3. `shouquan` 字段的生成/校验算法（本地仅存储，未发现本地生成逻辑）；
  4. VMProtect 虚拟化段内的最终判定条件。

## 8. 复现命令

```bash
source config/rea/rea.env
python3 scripts/pe-xref.py target/1102/0931.exe --strings 'sunmengze|yiyikeji'
python3 scripts/pe-xref.py target/1102/0931.exe --xref 0xb19bb3   # MAC 格式化
python3 scripts/pe-xref.py target/1102/0931.exe --xref 0xb1c4a2   # AES 密钥
python3 scripts/pe-xref.py target/1102/0931.exe --xref 0xb1bdcd   # MacAddress 字段
```
