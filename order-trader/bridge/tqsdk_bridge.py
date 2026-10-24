#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Order Trader v1.0 - tqsdk 行情桥接进程。

C++ 主程序通过 stdin/stdout 的 JSON Lines 与本进程通信。本进程只负责行情，
虚拟撮合完全在 C++ 侧完成。

连接层与 pullstack_meter 的天勤实时行情源保持同一套做法：
  · TqApi(auth=TqAuth(user, pwd), disable_print=True)：账号认证与行情服务器连接
    全部交给 tqsdk 自己完成，不再另外拼一套 HTTP 校验 / 代理探测
  · TqApi 有线程归属，构造与 wait_update 都在同一个“行情线程”里完成
  · 主循环 wait_update(deadline=now+0.5)：非交易时段服务器没有行情帧，
    不带 deadline 会永久阻塞，订阅/退出都推进不了
  · tick 变化判定：逐字段与上一笔快照比较（价/量/持仓/时间 + 五档全量），
    比 is_changing 单字段更稳，不会漏掉 2~5 档的堆单/撤单
  · 断线指数退避重连（1s -> 30s）

  C++ -> Python
    {"cmd":"login","user":"alyzc26","password":"130100"}
    {"cmd":"symbol","symbol":"SHFE.rb2701"}
    {"cmd":"subscribe","symbols":["SHFE.rb2701"]}
    {"cmd":"ticks","symbol":"SHFE.rb2701","count":"10000"}
    {"cmd":"unsubscribe_all"}
    {"cmd":"shutdown"}

  Python -> C++
    {"type":"ready","source":"tqsdk"}
    {"type":"env","ready":true,"version":"..."}
    {"type":"login_result","ok":true,"account":"alyzc26"}
    {"type":"api_ready","account":"alyzc26"}
    {"type":"api_error","message":"..."}
    {"type":"symbol_info","symbol":"SHFE.rb2701",...}
    {"type":"quote","symbol":"SHFE.rb2701",...}
    {"type":"tick","symbol":"SHFE.rb2701","row":[...]}
    {"type":"ticks","symbol":"SHFE.rb2701","rows":[[...],...]}
    {"type":"log","message":"..."}
    {"type":"error","message":"..."}
    {"type":"bye"}

tick row 布局（28 项）：
  0 ts(ms) 1 last 2 bid1 3 ask1 4 累计成交量 5 持仓量 6 本 tick 增量 7 主动方向
  8..12 ask_price1..5   13..17 ask_volume1..5
  18..22 bid_price1..5  23..27 bid_volume1..5
"""

from __future__ import annotations

import json
import math
import os
import queue
import sys
import threading
import time
import traceback
from datetime import datetime

_OUT_LOCK = threading.Lock()

# tqsdk 的导入（numpy/pandas/scipy）很重，进程一启动就在后台预热，
# 与界面启动并行，点击登录时通常已经就绪。
_TQSDK = None
_TQSDK_ERROR = None
_TQSDK_READY = threading.Event()

# Windows 管道默认使用本地代码页，这里统一成 UTF-8，避免中文乱码。
for _stream in (sys.stdout, sys.stderr):
    try:
        _stream.reconfigure(encoding="utf-8", errors="replace")
    except (AttributeError, ValueError):
        pass

# tqsdk 会向 stdout 打印连接/账户日志，先保存协议流再把 stdout 指向 stderr。
_PROTOCOL_OUT = sys.stdout
sys.stdout = sys.stderr


def emit(payload: dict) -> None:
    with _OUT_LOCK:
        _PROTOCOL_OUT.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
        _PROTOCOL_OUT.flush()


def log(message: str) -> None:
    emit({"type": "log", "message": str(message)})


def num(value, default: float = 0.0) -> float:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return default
    if math.isnan(result) or math.isinf(result):
        return default
    return result


def integer(value, default: int = 0) -> int:
    try:
        result = float(value)
    except (TypeError, ValueError):
        return default
    if math.isnan(result) or math.isinf(result):
        return default
    return int(result)


def to_ms(value) -> int:
    """兼容 tqsdk 的 datetime 字符串与 pandas 纳秒浮点数。"""
    if value is None:
        return 0
    try:
        number = float(value)
        if not math.isnan(number) and number > 1e15:
            return int(number) // 1_000_000
    except (TypeError, ValueError):
        pass

    text = str(value)
    for fmt in ("%Y-%m-%d %H:%M:%S.%f", "%Y-%m-%d %H:%M:%S"):
        try:
            return int(datetime.strptime(text, fmt).timestamp() * 1000)
        except ValueError:
            continue
    return 0


def prewarm_tqsdk() -> None:
    global _TQSDK, _TQSDK_ERROR
    started = time.time()
    threading.Thread(target=_import_progress, args=(started,), daemon=True).start()
    try:
        # 分模块计时，卡住时能直接看出是哪个包；每步先报"正在加载"，卡死也停在这一行。
        # scipy 的子模块是懒加载，`import scipy` 本身几乎不耗时，真正的工作量在
        # scipy.stats（tqsdk.tafunc 里 `from scipy import stats`），按子模块计时才准。
        for module in ("numpy", "pandas", "scipy.stats", "aiohttp", "tqsdk"):
            step = time.time()
            log(f"正在加载 {module}…")
            __import__(module)
            log(f"已加载 {module}（{time.time() - step:.1f}s）")
        log("科学计算栈版本：" + " / ".join(
            f"{name} {getattr(sys.modules.get(name), '__version__', '?')}"
            for name in ("numpy", "pandas", "scipy")))
        import tqsdk  # noqa: F811  （上面已导入，这里取模块对象）
        _TQSDK = tqsdk
        log(f"tqsdk 运行环境已就绪，用时 {time.time() - started:.1f}s")
        emit({"type": "env", "ready": True, "version": getattr(tqsdk, "__version__", ""),
              "seconds": round(time.time() - started, 2)})
    except Exception as exc:
        _TQSDK_ERROR = exc
        log(f"tqsdk 运行环境加载失败：{exc}\n{traceback.format_exc()}")
        emit({"type": "env", "ready": False,
              "message": f"加载 tqsdk 失败：{str(exc).strip() or type(exc).__name__}"})
    finally:
        _TQSDK_READY.set()


def _import_progress(started: float) -> None:
    """导入期间每 12 秒报一次进度：Windows 首次加载 numpy 可能被安全软件扫描很久。"""
    while not _TQSDK_READY.wait(12.0):
        log(f"正在加载 tqsdk 运行环境（已等待 {time.time() - started:.0f} 秒，首次启动较慢）")


# 品种 -> 交易所（天勤代码必须带交易所前缀）。表按天勤合约服务实际上市品种核对。
_EXCHANGE_PRODUCTS = {
    "SHFE": "rb hc cu al zn pb ni sn au ag ss sp fu bu ru wr br ao ad op".split(),
    "DCE": "a b c cs i j jm jd l m p pp v y eg eb pg lh bb fb rr lg bz".split(),
    "CZCE": ("MA TA SR CF CY FG RM OI ZC SM SF AP CJ UR SA PF PK RS RI WH JR LR PM PX SH PR PL"
             .split()),
    "CFFEX": "IF IH IC IM IO HO MO T TF TS TL".split(),
    "INE": "sc lu bc nr ec".split(),
    "GFEX": "si lc ps pd pt".split(),
}

# 期权代码带横线的交易所：DCE.m2601-C-2500 / CFFEX.IO2601-C-3800 / GFEX.lc2401-C-100000；
# 上期所、能源中心、郑商所不带横线：SHFE.ag2612C13500 / CZCE.SR601C5800
_DASH_OPTION_EXCHANGES = {"DCE", "CFFEX", "GFEX"}


def infer_symbol(instrument_id: str) -> str:
    """为缺少交易所前缀的合约代码补齐前缀，例如 rb2701 -> SHFE.rb2701、
    ag2612c13500 -> SHFE.ag2612C13500、m2601c2500 -> DCE.m2601-C-2500。"""
    text = instrument_id.strip()
    if not text or "." in text:
        return text

    idx = 0
    while idx < len(text) and text[idx].isalpha():
        idx += 1
    product = text[:idx]
    if not product:
        return text

    rest = text[idx:].replace("-", "")
    month = ""
    for ch in rest:
        if not ch.isdigit():
            break
        month += ch
    if not month:
        return text

    tail = rest[len(month):]
    option = ""
    strike = ""
    if tail and tail[0].upper() in ("C", "P"):
        option = tail[0].upper()
        strike = "".join(ch for ch in tail[1:] if ch.isdigit() or ch == ".")
    if option and not strike:
        option = ""

    wanted = product.upper()
    for exchange, products in _EXCHANGE_PRODUCTS.items():
        for item in products:
            if item.upper() != wanted:
                continue
            suffix = ""
            if option:
                if exchange in _DASH_OPTION_EXCHANGES:
                    suffix = f"-{option}-{strike}"
                else:
                    suffix = f"{option}{strike}"
            return f"{exchange}.{item}{month}{suffix}"
    return text


def quote_arrays(quote):
    ask_prices, ask_volumes, bid_prices, bid_volumes = [], [], [], []
    for i in range(1, 6):
        ask_prices.append(num(getattr(quote, f"ask_price{i}", 0)))
        ask_volumes.append(integer(getattr(quote, f"ask_volume{i}", 0)))
        bid_prices.append(num(getattr(quote, f"bid_price{i}", 0)))
        bid_volumes.append(integer(getattr(quote, f"bid_volume{i}", 0)))
    return ask_prices, ask_volumes, bid_prices, bid_volumes


def snapshot(quote) -> dict:
    """把 quote 拍成一份纯数值快照，用于”有没有变化”的逐字段判定。"""
    ask_prices, ask_volumes, bid_prices, bid_volumes = quote_arrays(quote)
    return {
        "ts": to_ms(getattr(quote, "datetime", None)),
        "last": num(getattr(quote, "last_price", 0)),
        "volume": integer(getattr(quote, "volume", 0)),
        "oi": integer(getattr(quote, "open_interest", 0)),
        "stamp": str(getattr(quote, "datetime", "") or ""),
        "ask_prices": ask_prices,
        "ask_volumes": ask_volumes,
        "bid_prices": bid_prices,
        "bid_volumes": bid_volumes,
    }


def snapshot_key(snap: dict):
    return (snap["stamp"], snap["ts"], snap["last"], snap["volume"], snap["oi"],
            tuple(snap["ask_prices"]), tuple(snap["ask_volumes"]),
            tuple(snap["bid_prices"]), tuple(snap["bid_volumes"]))


def side_of(last: float, ask1: float, bid1: float, prev_last: float) -> int:
    if last <= 0:
        return 0
    if ask1 > 0 and last >= ask1:
        return 1
    if bid1 > 0 and last <= bid1:
        return -1
    if prev_last > 0 and last > prev_last:
        return 1
    if prev_last > 0 and last < prev_last:
        return -1
    return 0


def build_row(ts: int, last: float, bid1: float, ask1: float, total: int, open_interest: int,
              delta: int, side: int, ask_prices, ask_volumes, bid_prices, bid_volumes) -> list:
    row = [ts, last, bid1, ask1, total, open_interest, delta, side]
    row.extend(ask_prices)
    row.extend(ask_volumes)
    row.extend(bid_prices)
    row.extend(bid_volumes)
    return row


class Bridge:
    """行情线程持有 TqApi；主线程只读指令并转发。"""

    def __init__(self) -> None:
        self.running = True
        self.user = ""
        self.password = ""
        self.api = None
        self.thread: threading.Thread | None = None
        self.cmds: "queue.Queue[dict]" = queue.Queue()
        # 以下仅在行情线程内读写
        self.quotes: dict[str, list] = {}       # symbol -> [quote, 上一笔快照键]
        self.active: set[str] = set()           # 需要持续推送行情的合约
        self.prev_total: dict[str, int] = {}
        self.prev_last: dict[str, float] = {}
        self.ready_reported = False

    # ------------------------------------------------------------------ 登录
    def cmd_login(self, request: dict) -> None:
        user = str(request.get("user") or "")
        password = str(request.get("password") or "")
        log(f"收到登录请求：{user}")
        if not user or not password:
            emit({"type": "login_result", "ok": False, "message": "账号或密码为空"})
            return
        if self.thread is not None and self.thread.is_alive():
            log("行情线程已在运行，忽略重复登录请求")
            emit({"type": "login_result", "ok": True, "account": user})
            return
        self.user = user
        self.password = password
        self.running = True
        # 先让界面放行：tqsdk 首次导入（Windows 上可能被安全软件扫描很久）不应该
        # 挡住主界面。真正的连接结果由 api_ready / api_error 给出；
        # 账号密码错误时 report_fatal 会再回一条 ok=false 的 login_result。
        emit({"type": "login_result", "ok": True, "account": user})
        log("账号已受理，后台加载 tqsdk 并连接行情…")
        self.thread = threading.Thread(target=self._market_loop, name="market", daemon=True)
        self.thread.start()

    def _market_loop(self) -> None:
        """与 pullstack_meter 一致：连接、轮询、断开重连都在本线程内完成。"""
        # 实测导入全套库只要几秒，冷启动叠加杀毒扫描最多一两分钟；
        # 5 分钟还没就绪基本就是被拦截/文件缺失，继续等只会让人白等。
        if not _TQSDK_READY.wait(300):
            self.report_fatal("tqsdk 运行环境加载超时（5 分钟），请运行「检查环境.bat」查看卡在哪一步")
            return
        if _TQSDK is None:
            self.report_fatal(f"加载 tqsdk 失败：{_TQSDK_ERROR or '未知错误'}")
            return

        delay = 1.0
        started = time.time()
        while self.running:
            try:
                self._session()
                break
            except Exception as exc:  # noqa: BLE001
                message = str(exc).strip() or type(exc).__name__
                log(f"行情连接异常：{message}")
                if self._is_auth_error(message):
                    self.report_fatal(f"tqsdk 账号认证失败：{message}")
                    return
                if not self.running:
                    break
                self._reset_session()
                if self.ready_reported:
                    log(f"{delay:.0f} 秒后重连行情服务器")
                else:
                    # 首次连接失败：把原因先显示出来，同时继续退避重试
                    emit({"type": "api_error", "message": message})
                    if time.time() - started > 60.0:
                        self.report_fatal(f"行情服务器连接失败：{message}")
                        return
                    log(f"{delay:.0f} 秒后重试连接")
                time.sleep(delay)
                delay = min(delay * 2, 30.0)

        self._reset_session()
        self.api = None

    @staticmethod
    def _is_auth_error(message: str) -> bool:
        text = message or ""
        return ("用户权限认证失败" in text or "快期账户" in text
                or "grant_type" in text or "unauthorized" in text.lower())

    def report_fatal(self, message: str) -> None:
        """连接彻底失败：登录阶段回 login_result(失败)，连过线则只报 api_error。"""
        log(message)
        if self.ready_reported:
            emit({"type": "api_error", "message": message})
        else:
            emit({"type": "login_result", "ok": False, "message": message})

    def _reset_session(self) -> None:
        self.quotes.clear()
        self.prev_total.clear()
        self.prev_last.clear()
        self.api = None

    def _session(self) -> None:
        started = time.time()
        log(f"正在连接天勤行情（账号 {self.user}）")
        # 与参考实现一致：不传 web_gui，disable_print 抑制 tqsdk 自身打印
        api = _TQSDK.TqApi(auth=_TQSDK.TqAuth(self.user, self.password), disable_print=True)
        self.api = api
        log(f"行情服务器连接成功（{time.time() - started:.1f}s）")

        self.ready_reported = True
        emit({"type": "api_ready", "account": self.user})

        # 断线重连后补订阅（界面也会在 api_ready 后重新下发订阅，重复无害）
        for symbol in sorted(self.active):
            self._subscribe_one(api, symbol)

        try:
            while self.running:
                # deadline 轮询：非交易时段服务器无行情帧，无 deadline 会永久阻塞
                api.wait_update(deadline=time.time() + 0.5)
                self._drain(api)
                self._forward()
        finally:
            try:
                api.close()
            except Exception:  # noqa: BLE001
                pass
            log("天勤行情连接已关闭")

    # -------------------------------------------------------------- 指令处理
    def _drain(self, api) -> None:
        while True:
            try:
                request = self.cmds.get_nowait()
            except queue.Empty:
                return
            command = str(request.get("cmd") or "")
            try:
                if command == "subscribe":
                    self._handle_subscribe(api, request)
                elif command == "unsubscribe_all":
                    self.active.clear()
                elif command == "symbol":
                    self._handle_symbol(api, request)
                elif command == "ticks":
                    self._handle_ticks(api, request)
            except Exception as exc:  # noqa: BLE001
                log(f"处理 {command} 指令异常：{exc}")
                log(traceback.format_exc())

    def _ensure_quote(self, api, symbol: str):
        cell = self.quotes.get(symbol)
        if cell is not None:
            return cell[0]
        try:
            quote = api.get_quote(symbol)
        except Exception as exc:  # noqa: BLE001
            emit({"type": "error", "message": f"订阅合约失败 {symbol}：{exc}"})
            return None
        self.quotes[symbol] = [quote, None]
        self.prev_total[symbol] = integer(getattr(quote, "volume", 0))
        self.prev_last[symbol] = num(getattr(quote, "last_price", 0))
        return quote

    def _subscribe_one(self, api, symbol: str) -> None:
        quote = self._ensure_quote(api, symbol)
        if quote is not None:
            self.active.add(symbol)
            self._emit_quote(symbol, quote)
            # 记下快照键，避免主循环立刻把同一笔行情再推一遍
            cell = self.quotes.get(symbol)
            if cell is not None:
                try:
                    cell[1] = snapshot_key(snapshot(quote))
                except Exception:  # noqa: BLE001
                    cell[1] = None
            log(f"已订阅 {symbol}")

    def _handle_subscribe(self, api, request: dict) -> None:
        for item in request.get("symbols") or []:
            symbol = infer_symbol(str(item))
            if not symbol or symbol in self.active:
                continue
            self._subscribe_one(api, symbol)

    def _handle_symbol(self, api, request: dict) -> None:
        symbol = infer_symbol(str(request.get("symbol") or "").strip())
        if not symbol:
            return
        payload = {
            "type": "symbol_info",
            "symbol": symbol,
            "name": symbol,
            "exchange": "",
            "product": "",
            "price_tick": 1.0,
            "price_decs": 0,
            "volume_multiple": 1.0,
            "commission": 0.0,
            "margin": 0.0,
            "expired": False,
        }
        try:
            info = api.query_symbol_info(symbol=symbol)
            row = info.iloc[0] if hasattr(info, "iloc") else info

            def field(key, default=None):
                try:
                    return row[key]
                except Exception:  # noqa: BLE001
                    return default

            payload["name"] = str(field("instrument_name", symbol) or symbol)
            payload["exchange"] = str(field("exchange_id", "") or "")
            payload["product"] = str(field("product_id", "") or "")
            payload["price_tick"] = num(field("price_tick", 1.0), 1.0)
            payload["price_decs"] = integer(field("price_decs", 0), 0)
            payload["volume_multiple"] = num(field("volume_multiple", 1.0), 1.0)
            payload["expired"] = bool(field("expired", False))
        except Exception as exc:  # noqa: BLE001
            log(f"获取合约信息失败 {symbol}：{exc}")

        quote = self._ensure_quote(api, symbol)
        if quote is not None:
            # 手续费/保证金随合约信息下发；只等很短一会儿，避免拖住后面的订阅
            deadline = time.time() + 1.5
            while time.time() < deadline and num(getattr(quote, "commission", 0)) <= 0:
                try:
                    api.wait_update(deadline=time.time() + 0.3)
                except Exception:  # noqa: BLE001
                    break
            payload["commission"] = num(getattr(quote, "commission", 0))
            payload["margin"] = num(getattr(quote, "margin", 0))
        emit(payload)

    # -------------------------------------------------------------- 实时行情
    def _emit_quote(self, symbol: str, quote) -> None:
        ask_prices, ask_volumes, bid_prices, bid_volumes = quote_arrays(quote)
        emit({
            "type": "quote",
            "symbol": symbol,
            "ts": to_ms(getattr(quote, "datetime", None)),
            "last": num(getattr(quote, "last_price", 0)),
            "pre_close": num(getattr(quote, "pre_close", 0)),
            "pre_settle": num(getattr(quote, "pre_settlement", 0)),
            "open": num(getattr(quote, "open", 0)),
            "high": num(getattr(quote, "highest", 0)),
            "low": num(getattr(quote, "lowest", 0)),
            "upper": num(getattr(quote, "upper_limit", 0)),
            "lower": num(getattr(quote, "lower_limit", 0)),
            "volume": integer(getattr(quote, "volume", 0)),
            "oi": integer(getattr(quote, "open_interest", 0)),
            "bids": [[bid_prices[i], bid_volumes[i]] for i in range(5)],
            "asks": [[ask_prices[i], ask_volumes[i]] for i in range(5)],
        })

    def _emit_tick(self, symbol: str, snap: dict) -> None:
        ts = snap["ts"]
        last = snap["last"]
        if ts <= 0 or last <= 0:
            return
        total = snap["volume"]
        previous = self.prev_total.get(symbol)
        delta = total - previous if previous is not None and total >= previous else 0
        self.prev_total[symbol] = total
        prev_last = self.prev_last.get(symbol, 0.0)
        side = side_of(last, snap["ask_prices"][0], snap["bid_prices"][0], prev_last)
        self.prev_last[symbol] = last

        row = build_row(ts, last, snap["bid_prices"][0], snap["ask_prices"][0], total,
                        snap["oi"], delta, side,
                        snap["ask_prices"], snap["ask_volumes"],
                        snap["bid_prices"], snap["bid_volumes"])
        emit({"type": "tick", "symbol": symbol, "row": row})

    def _forward(self) -> None:
        """当前快照与上一笔比较，有变化才推行情（五档全量参与判定）。"""
        for symbol in list(self.active):
            cell = self.quotes.get(symbol)
            if cell is None:
                continue
            quote = cell[0]
            try:
                snap = snapshot(quote)
            except Exception as exc:  # noqa: BLE001
                log(f"读取行情异常 {symbol}：{exc}")
                continue
            if snap["last"] <= 0:      # 行情未到（空快照）：不喂零值
                continue
            key = snapshot_key(snap)
            if cell[1] == key:
                continue
            cell[1] = key
            self._emit_quote(symbol, quote)
            self._emit_tick(symbol, snap)

    # -------------------------------------------------------------- 历史行情
    def _handle_ticks(self, api, request: dict) -> None:
        symbol = infer_symbol(str(request.get("symbol") or "").strip())
        if not symbol:
            return
        try:
            count = int(str(request.get("count") or "10000"))
        except ValueError:
            count = 10000
        count = max(1, min(count, 20000))

        try:
            serial = api.get_tick_serial(symbol, data_length=count)
        except Exception as exc:  # noqa: BLE001
            emit({"type": "ticks", "symbol": symbol, "rows": []})
            emit({"type": "error", "message": f"获取 tick 序列失败 {symbol}：{exc}"})
            return

        # 与参考实现一致：tick 序列分片下发，wait_update 逐片收；
        # 取满 count 或连续 3 秒无新增即停，总时长不超过 60 秒
        started = time.time()
        last_len = None
        stable_since = None
        while time.time() - started < 60.0:
            length = 0 if serial is None else len(serial)
            if length >= count:
                break
            if length > 0:
                if length == last_len:
                    stable_since = stable_since or time.time()
                    if time.time() - stable_since >= 3.0:
                        break
                else:
                    stable_since = None
            last_len = length
            try:
                api.wait_update(deadline=time.time() + 1.0)
            except Exception as exc:  # noqa: BLE001
                log(f"等待历史数据失败：{exc}")
                break

        rows = []
        previous_total = None
        previous_last = 0.0
        try:
            if serial is not None:
                for _, row in serial.iterrows():
                    ts = to_ms(row["datetime"])
                    last = num(row["last_price"])
                    if ts <= 0 or last <= 0:
                        continue
                    ask_prices = [num(row[f"ask_price{i}"]) for i in range(1, 6)]
                    ask_volumes = [integer(row[f"ask_volume{i}"]) for i in range(1, 6)]
                    bid_prices = [num(row[f"bid_price{i}"]) for i in range(1, 6)]
                    bid_volumes = [integer(row[f"bid_volume{i}"]) for i in range(1, 6)]
                    total = integer(row["volume"])
                    delta = (total - previous_total
                             if previous_total is not None and total >= previous_total else 0)
                    previous_total = total
                    side = side_of(last, ask_prices[0], bid_prices[0], previous_last)
                    previous_last = last
                    rows.append(build_row(ts, last, bid_prices[0], ask_prices[0], total,
                                          integer(row["open_interest"]), delta, side,
                                          ask_prices, ask_volumes, bid_prices, bid_volumes))
        except Exception as exc:  # noqa: BLE001
            log(f"解析历史 tick 失败：{exc}")
            log(traceback.format_exc())

        emit({"type": "ticks", "symbol": symbol, "rows": rows})
        log(f"{symbol} 历史 tick {len(rows)} 条")

    # ---------------------------------------------------------------- 调度
    def handle(self, request: dict) -> None:
        command = str(request.get("cmd") or "")
        if command == "login":
            self.cmd_login(request)
        elif command in ("symbol", "subscribe", "ticks", "unsubscribe_all"):
            self.cmds.put(request)
        elif command == "shutdown":
            self.running = False
        else:
            emit({"type": "error", "message": f"未知指令：{command}"})

    def stop(self) -> None:
        self.running = False
        if self.thread is not None and self.thread.is_alive():
            self.thread.join(2.0)


def reader_loop(lines: "queue.Queue[str]") -> None:
    for raw in sys.stdin:
        text = raw.strip()
        if text:
            lines.put(text)


def heartbeat_loop() -> None:
    """每 3 秒报一次存活，C++ 侧据此区分“正在加载”和“进程卡死”。"""
    while True:
        time.sleep(3.0)
        emit({"type": "heartbeat"})


def _selftest_account() -> tuple[str, str]:
    """自检用的账号：读发布包的 config/order-trader.json（bridge 的上级目录）。"""
    path = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
                        "config", "order-trader.json")
    try:
        with open(path, "r", encoding="utf-8") as handle:
            data = json.load(handle)
        return str(data.get("user") or ""), str(data.get("password") or "")
    except Exception:  # noqa: BLE001
        return "", ""


def selftest() -> int:
    """命令行环境自检：不经过 C++ 主程序，逐模块计时后再真实连一次行情。

    用户机器上如果行情加载不出来，运行发布包里的「检查环境.bat」即可生成
    logs/selftest.txt，直接看出卡在哪个包、卡了多久、账号能否连通。
    """
    print("Order Trader v1.0 行情环境自检")
    print(f"解释器：{sys.executable}")
    print(f"Python：{sys.version.split()[0]}  {sys.platform}  {64 if sys.maxsize > 2**32 else 32} 位")
    started = time.time()
    for module in ("numpy", "pandas", "scipy.stats", "aiohttp", "tqsdk"):
        step = time.time()
        print(f"  加载 {module:12s} … ", end="", flush=True)
        try:
            __import__(module)
        except Exception as exc:  # noqa: BLE001
            print(f"失败：{exc}")
            print(traceback.format_exc())
            print(f"\n自检未通过：{module} 加载失败（累计 {time.time() - started:.1f}s）")
            print("常见原因：安全软件隔离了该目录的文件，或解压不完整。"
                  "请把整个目录加入杀毒白名单后重新解压。")
            return 1
        print(f"{time.time() - step:6.2f}s")
    tqsdk = __import__("tqsdk")
    print(f"  tqsdk 版本 {tqsdk.__version__}  模块加载累计 {time.time() - started:.1f}s")
    print("  科学计算栈 " + " / ".join(
        f"{name} {getattr(sys.modules.get(name), '__version__', '?')}"
        for name in ("numpy", "pandas", "scipy")))

    user, password = _selftest_account()
    if not user or not password:
        print("未在 config/order-trader.json 找到账号，跳过行情连接测试")
        return 0

    print(f"  连接行情服务器（账号 {user}）… ", end="", flush=True)
    step = time.time()
    api = None
    try:
        # 与桥接一致：认证与连接全交给 tqsdk，不额外拼 HTTP 校验
        api = tqsdk.TqApi(auth=tqsdk.TqAuth(user, password), disable_print=True)
        print(f"{time.time() - step:.1f}s 成功")
    except Exception as exc:  # noqa: BLE001
        print(f"失败：{exc}")
        print(traceback.format_exc())
        print("\n自检未通过：行情服务器连接失败（账号密码错误或网络不通）")
        return 1

    try:
        symbol = infer_symbol("rb2701")
        print(f"  订阅 {symbol} … ", end="", flush=True)
        quote = api.get_quote(symbol)
        deadline = time.time() + 20.0
        while time.time() < deadline and not getattr(quote, "datetime", ""):
            api.wait_update(deadline=time.time() + 1.0)
        stamp = getattr(quote, "datetime", "")
        if stamp:
            print(f"{stamp}  最新价 {num(getattr(quote, 'last_price', 0)):.2f}  "
                  f"买一 {num(getattr(quote, 'bid_price1', 0)):.2f}×{integer(getattr(quote, 'bid_volume1', 0))}  "
                  f"卖一 {num(getattr(quote, 'ask_price1', 0)):.2f}×{integer(getattr(quote, 'ask_volume1', 0))}")
        else:
            print("已订阅，但 20 秒内没有行情帧（非交易时段正常）")
    except Exception as exc:  # noqa: BLE001
        print(f"失败：{exc}")
        print(traceback.format_exc())
        print("\n自检未通过：行情订阅失败")
        return 1
    finally:
        try:
            api.close()
        except Exception:  # noqa: BLE001
            pass

    print(f"\n自检通过，总用时 {time.time() - started:.1f}s。行情环境正常。")
    return 0


def main() -> int:
    if "--selftest" in sys.argv[1:]:
        # 自检直接打印到控制台，不写协议流（stdout 在上面已被指向 stderr）
        sys.stdout = _PROTOCOL_OUT
        return selftest()
    emit({"type": "ready", "source": "tqsdk"})
    # 后台预热 tqsdk，与界面启动、账号校验并行
    threading.Thread(target=prewarm_tqsdk, daemon=True).start()
    threading.Thread(target=heartbeat_loop, daemon=True).start()

    lines: "queue.Queue[str]" = queue.Queue()
    threading.Thread(target=reader_loop, args=(lines,), daemon=True).start()

    bridge = Bridge()
    while bridge.running:
        try:
            line = lines.get(timeout=0.1)
        except queue.Empty:
            continue
        try:
            request = json.loads(line)
        except json.JSONDecodeError:
            log(f"无法解析指令：{line[:200]}")
            continue
        if not isinstance(request, dict):
            continue
        try:
            bridge.handle(request)
        except Exception as exc:  # noqa: BLE001
            log(f"处理指令异常：{exc}")
            log(traceback.format_exc())
            emit({"type": "error", "message": str(exc)})

    bridge.stop()
    emit({"type": "bye"})
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        sys.exit(0)
    except Exception as exc:  # 兜底，保证 C++ 侧能看到错误原因
        log(f"桥接进程异常退出：{exc}")
        log(traceback.format_exc())
        emit({"type": "bye"})
        sys.exit(1)
