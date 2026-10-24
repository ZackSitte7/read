#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Order Trader v1.0 - tqsdk 行情桥接进程。

C++ 主程序通过 stdin/stdout 的 JSON Lines 与本进程通信。本进程只负责行情，
虚拟撮合完全在 C++ 侧完成。

  C++ -> Python
    {"cmd":"login","user":"alyzc26","password":"130100"}
    {"cmd":"symbol","symbol":"SHFE.rb2701"}
    {"cmd":"subscribe","symbols":["SHFE.rb2701"]}
    {"cmd":"ticks","symbol":"SHFE.rb2701","count":"10000"}
    {"cmd":"unsubscribe_all"}
    {"cmd":"shutdown"}

  Python -> C++
    {"type":"ready","source":"tqsdk"}
    {"type":"login_result","ok":true,"account":"alyzc26"}
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
import queue
import sys
import threading
import time
import traceback
from datetime import datetime

_OUT_LOCK = threading.Lock()

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


_EXCHANGE_PRODUCTS = {
    "SHFE": "rb hc cu al zn pb ni sn au ag ss sp fu bu ru nr wr".split(),
    "DCE": "a b c cs i j jm jd l m p pp v y eg eb pg lh bb fb rr".split(),
    "CZCE": "MA TA SR CF CY FG RM OI ZC SM SF AP CJ UR SA PF PK RS RI WH JR LR PM".split(),
    "CFFEX": "IF IH IC IM T TF TS TL".split(),
    "INE": "sc lu bc nr".split(),
    "GFEX": "si lc ps".split(),
}


def infer_symbol(instrument_id: str) -> str:
    """为缺少交易所前缀的合约代码补齐前缀，例如 rb2701 -> SHFE.rb2701。"""
    if "." in instrument_id:
        return instrument_id
    product = "".join(ch for ch in instrument_id if ch.isalpha()).upper()
    for exchange, products in _EXCHANGE_PRODUCTS.items():
        if product in (p.upper() for p in products):
            return f"{exchange}.{instrument_id}"
    return instrument_id


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


def quote_arrays(quote):
    ask_prices, ask_volumes, bid_prices, bid_volumes = [], [], [], []
    for i in range(1, 6):
        ask_prices.append(num(getattr(quote, f"ask_price{i}", 0)))
        ask_volumes.append(integer(getattr(quote, f"ask_volume{i}", 0)))
        bid_prices.append(num(getattr(quote, f"bid_price{i}", 0)))
        bid_volumes.append(integer(getattr(quote, f"bid_volume{i}", 0)))
    return ask_prices, ask_volumes, bid_prices, bid_volumes


class Bridge:
    def __init__(self) -> None:
        self.api = None
        self.user = ""
        self.quotes: dict[str, object] = {}
        self.active: set[str] = set()
        self.prev_total: dict[str, int] = {}
        self.prev_last: dict[str, float] = {}
        self.running = True

    # ------------------------------------------------------------------ 登录
    def close_api(self) -> None:
        self.active.clear()
        self.quotes.clear()
        self.prev_total.clear()
        self.prev_last.clear()
        if self.api is not None:
            try:
                self.api.close()
            except Exception:
                pass
            self.api = None

    def cmd_login(self, request: dict) -> None:
        user = str(request.get("user") or "")
        password = str(request.get("password") or "")
        if not user or not password:
            emit({"type": "login_result", "ok": False, "message": "账号或密码为空"})
            return

        try:
            from tqsdk import TqApi, TqAuth
        except Exception as exc:
            # 内嵌运行环境缺少依赖时必须立刻回报，否则 C++ 侧只能看到超时
            log(traceback.format_exc())
            emit({"type": "login_result", "ok": False,
                  "message": f"加载 tqsdk 失败：{str(exc).strip() or type(exc).__name__}"})
            return

        log(f"Python {sys.version.split()[0]}，准备登录 tqsdk（首次连接需下载合约列表，可能需要 1 分钟）")
        self.close_api()
        log(f"正在连接 tqsdk：{user}")
        started = time.time()
        try:
            self.api = TqApi(auth=TqAuth(user, password), web_gui=False)
        except Exception as exc:
            self.api = None
            message = str(exc).strip() or "tqsdk 登录失败"
            log(f"tqsdk 登录失败：{message}")
            emit({"type": "login_result", "ok": False, "message": message})
            return

        self.user = user
        log(f"tqsdk 登录成功，用时 {time.time() - started:.2f}s")
        emit({"type": "login_result", "ok": True, "account": user})

    # -------------------------------------------------------------- 合约信息
    def ensure_quote(self, symbol: str, wait: bool = True):
        if self.api is None:
            return None
        quote = self.quotes.get(symbol)
        if quote is None:
            try:
                quote = self.api.get_quote(symbol)
            except Exception as exc:
                emit({"type": "error", "message": f"订阅合约失败 {symbol}：{exc}"})
                return None
            self.quotes[symbol] = quote
            self.prev_total[symbol] = integer(getattr(quote, "volume", 0))
            self.prev_last[symbol] = num(getattr(quote, "last_price", 0))

        if wait and num(getattr(quote, "commission", 0)) <= 0:
            deadline = time.time() + 5.0
            while time.time() < deadline and num(getattr(quote, "commission", 0)) <= 0:
                try:
                    self.api.wait_update(deadline=time.time() + 0.4)
                except Exception:
                    break
        return quote

    def cmd_symbol(self, request: dict) -> None:
        if self.api is None:
            emit({"type": "error", "message": "尚未登录 tqsdk"})
            return
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
            info = self.api.query_symbol_info(symbol=symbol)
            row = info.iloc[0] if hasattr(info, "iloc") else info

            def field(key, default=None):
                try:
                    return row[key]
                except Exception:
                    return default

            payload["name"] = str(field("instrument_name", symbol) or symbol)
            payload["exchange"] = str(field("exchange_id", "") or "")
            payload["product"] = str(field("product_id", "") or "")
            payload["price_tick"] = num(field("price_tick", 1.0), 1.0)
            payload["price_decs"] = integer(field("price_decs", 0), 0)
            payload["volume_multiple"] = num(field("volume_multiple", 1.0), 1.0)
            payload["expired"] = bool(field("expired", False))
        except Exception as exc:
            log(f"获取合约信息失败 {symbol}：{exc}")

        quote = self.ensure_quote(symbol)
        if quote is not None:
            payload["commission"] = num(getattr(quote, "commission", 0))
            payload["margin"] = num(getattr(quote, "margin", 0))

        emit(payload)

    # -------------------------------------------------------------- 实时行情
    def cmd_subscribe(self, request: dict) -> None:
        if self.api is None:
            emit({"type": "error", "message": "尚未登录 tqsdk"})
            return
        symbols = request.get("symbols") or []
        for item in symbols:
            symbol = infer_symbol(str(item))
            if not symbol:
                continue
            quote = self.ensure_quote(symbol, wait=False)
            if quote is None:
                continue
            self.active.add(symbol)
            self.prev_total[symbol] = integer(getattr(quote, "volume", 0))
            self.prev_last[symbol] = num(getattr(quote, "last_price", 0))
            self.emit_quote(symbol, quote)
            log(f"已订阅 {symbol}")

    def cmd_unsubscribe_all(self, _request: dict) -> None:
        self.active.clear()

    def emit_quote(self, symbol: str, quote) -> None:
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

    def emit_tick(self, symbol: str, quote) -> None:
        ts = to_ms(getattr(quote, "datetime", None))
        if ts <= 0:
            return
        last = num(getattr(quote, "last_price", 0))
        if last <= 0:
            return
        ask_prices, ask_volumes, bid_prices, bid_volumes = quote_arrays(quote)
        total = integer(getattr(quote, "volume", 0))
        previous = self.prev_total.get(symbol)
        delta = total - previous if previous is not None and total >= previous else 0
        self.prev_total[symbol] = total
        prev_last = self.prev_last.get(symbol, 0.0)
        side = side_of(last, ask_prices[0], bid_prices[0], prev_last)
        self.prev_last[symbol] = last

        row = build_row(ts, last, bid_prices[0], ask_prices[0], total,
                        integer(getattr(quote, "open_interest", 0)), delta, side,
                        ask_prices, ask_volumes, bid_prices, bid_volumes)
        emit({"type": "tick", "symbol": symbol, "row": row})

    def pump_quotes(self) -> None:
        if self.api is None:
            return
        for symbol in list(self.active):
            quote = self.quotes.get(symbol)
            if quote is None:
                continue
            try:
                changed = self.api.is_changing(quote)
            except Exception:
                continue
            if not changed:
                continue
            self.emit_quote(symbol, quote)
            self.emit_tick(symbol, quote)

    # -------------------------------------------------------------- 历史行情
    def cmd_ticks(self, request: dict) -> None:
        if self.api is None:
            emit({"type": "error", "message": "尚未登录 tqsdk"})
            return
        symbol = infer_symbol(str(request.get("symbol") or "").strip())
        try:
            count = int(str(request.get("count") or "10000"))
        except ValueError:
            count = 10000
        count = max(1, min(count, 20000))

        try:
            serial = self.api.get_tick_serial(symbol, data_length=count)
        except Exception as exc:
            emit({"type": "ticks", "symbol": symbol, "rows": []})
            emit({"type": "error", "message": f"获取 tick 序列失败 {symbol}：{exc}"})
            return

        deadline = time.time() + 30.0
        best = -1
        stall = 0
        while time.time() < deadline:
            length = 0 if serial is None else len(serial)
            if length >= count:
                break
            if length == best:
                stall += 1
                if stall >= 6:
                    break
            else:
                best = length
                stall = 0
            try:
                self.api.wait_update(deadline=time.time() + 1.0)
            except Exception as exc:
                log(f"等待历史数据失败：{exc}")
                break

        rows = []
        previous_total = None
        previous_last = 0.0
        try:
            iterable = serial.iterrows() if serial is not None else []
            for _, row in iterable:
                ts = to_ms(row["datetime"])
                last = num(row["last_price"])
                if ts <= 0 or last <= 0:
                    continue
                ask_prices = [num(row[f"ask_price{i}"]) for i in range(1, 6)]
                ask_volumes = [integer(row[f"ask_volume{i}"]) for i in range(1, 6)]
                bid_prices = [num(row[f"bid_price{i}"]) for i in range(1, 6)]
                bid_volumes = [integer(row[f"bid_volume{i}"]) for i in range(1, 6)]
                total = integer(row["volume"])
                delta = total - previous_total if previous_total is not None and total >= previous_total else 0
                previous_total = total
                side = side_of(last, ask_prices[0], bid_prices[0], previous_last)
                previous_last = last
                rows.append(build_row(ts, last, bid_prices[0], ask_prices[0], total,
                                      integer(row["open_interest"]), delta, side,
                                      ask_prices, ask_volumes, bid_prices, bid_volumes))
        except Exception as exc:
            log(f"解析历史 tick 失败：{exc}")
            log(traceback.format_exc())

        emit({"type": "ticks", "symbol": symbol, "rows": rows})
        log(f"{symbol} 历史 tick {len(rows)} 条")

    # ---------------------------------------------------------------- 调度
    def handle(self, request: dict) -> None:
        command = str(request.get("cmd") or "")
        if command == "login":
            self.cmd_login(request)
        elif command == "symbol":
            self.cmd_symbol(request)
        elif command == "subscribe":
            self.cmd_subscribe(request)
        elif command == "ticks":
            self.cmd_ticks(request)
        elif command == "unsubscribe_all":
            self.cmd_unsubscribe_all(request)
        elif command == "shutdown":
            self.running = False
        else:
            emit({"type": "error", "message": f"未知指令：{command}"})


def reader_loop(lines: "queue.Queue[str]") -> None:
    for raw in sys.stdin:
        text = raw.strip()
        if text:
            lines.put(text)


def main() -> int:
    emit({"type": "ready", "source": "tqsdk"})

    lines: "queue.Queue[str]" = queue.Queue()
    threading.Thread(target=reader_loop, args=(lines,), daemon=True).start()

    bridge = Bridge()
    failures = 0
    while bridge.running:
        while bridge.running:
            try:
                line = lines.get_nowait()
            except queue.Empty:
                break
            try:
                request = json.loads(line)
            except json.JSONDecodeError:
                log(f"无法解析指令：{line[:200]}")
                continue
            if not isinstance(request, dict):
                continue
            try:
                bridge.handle(request)
            except Exception as exc:
                log(f"处理指令异常：{exc}")
                log(traceback.format_exc())
                emit({"type": "error", "message": str(exc)})

        if not bridge.running:
            break
        if bridge.api is None:
            time.sleep(0.05)
            continue

        try:
            bridge.api.wait_update(deadline=time.time() + 0.2)
            bridge.pump_quotes()
            failures = 0
        except Exception as exc:
            failures += 1
            log(f"wait_update 异常（{failures}）：{exc}")
            if failures >= 5:
                emit({"type": "bye"})
                bridge.close_api()
                return 0
            time.sleep(0.5)

    bridge.close_api()
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
