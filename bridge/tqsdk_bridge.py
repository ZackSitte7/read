#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""TqSdk 行情/交易桥接进程。

桌面端通过 stdin/stdout 的 JSON Lines 与本进程通信，避免在 C# 中直接绑定
TqSdk 的 Python API。协议为单向请求 + 异步推送：

  C# -> Python
    {"id":1,"cmd":"connect","auth":{...},"trade":{...}}
    {"id":2,"cmd":"subscribe","symbols":{"rb2601":"SHFE.rb2601"}}
    {"id":3,"cmd":"insert_order","client_id":"...","symbol":"rb2601",
     "direction":"BUY","offset":"OPEN","volume":1,"price":3500,"type":"LIMIT"}
    {"id":4,"cmd":"cancel_order","order_id":"..."}
    {"id":5,"cmd":"query_positions"}
    {"id":6,"cmd":"query_account"}

  Python -> C#
    {"type":"ready","source":"tqsdk"|"sim"}
    {"type":"ack","id":3,"order_id":"..."}
    {"type":"error","id":3,"message":"..."}
    {"type":"quote","symbol":"rb2601", ...}
    {"type":"order","order_id":"...","symbol":"rb2601", ...}
    {"type":"trade","order_id":"...","symbol":"rb2601", ...}
    {"type":"positions","id":5,"items":[...]}
    {"type":"account","id":6,"balance":...,"available":...}
    {"type":"log","message":"..."}

使用 --sim 时不依赖 tqsdk，内置撮合器用于离线联调。
"""

from __future__ import annotations

import argparse
import json
import math
import queue
import random
import sys
import threading
import time
import traceback
from datetime import datetime

_OUT_LOCK = threading.Lock()

# TqSdk 自身会向 stdout 打印连接/账户日志。这里先保存协议输出流，
# 再把 sys.stdout 指向 stderr，保证 stdout 上只有 JSON Lines。
_PROTOCOL_OUT = sys.stdout
sys.stdout = sys.stderr


def emit(payload: dict) -> None:
    """线程安全地输出一行 JSON。"""
    with _OUT_LOCK:
        _PROTOCOL_OUT.write(json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n")
        _PROTOCOL_OUT.flush()


def log(message: str) -> None:
    emit({"type": "log", "message": str(message)})


def now_text() -> str:
    return datetime.now().strftime("%Y-%m-%d %H:%M:%S.%f")[:-3]


# ---------------------------------------------------------------------------
# 合约代码推断：rb2601 -> SHFE.rb2601
# ---------------------------------------------------------------------------

_EXCHANGE_PRODUCTS = {
    "SHFE": "rb hc cu al zn pb ni sn au ag ss sp fu bu ru nr wr".split(),
    "DCE": "a b c cs i j jm jd l m p pp v y eg eb pg lh bb fb rr".split(),
    "CZCE": "MA TA SR CF CY FG RM OI ZC SM SF AP CJ UR SA PF PK RS RI WH JR LR PM".split(),
    "CFFEX": "IF IH IC IM T TF TS TL".split(),
    "INE": "sc lu bc nr".split(),
    "GFEX": "si lc ps".split(),
}


def infer_symbol(instrument_id: str) -> str:
    """为缺少交易所前缀的合约代码补齐 TqSdk 前缀。"""
    if "." in instrument_id:
        return instrument_id

    product = "".join(ch for ch in instrument_id if ch.isalpha())
    for exchange, products in _EXCHANGE_PRODUCTS.items():
        for candidate in products:
            if product.upper() == candidate.upper():
                return f"{exchange}.{instrument_id}"
    return instrument_id


def round_tick(value: float, tick: float) -> float:
    if tick <= 0:
        return round(value, 4)
    return round(round(value / tick) * tick, 6)


# ---------------------------------------------------------------------------
# 离线撮合引擎：无需 tqsdk 也能跑通完整协议
# ---------------------------------------------------------------------------

_SIM_BASES = {
    "rb": (1650.0, 1.0),
    "hc": (3400.0, 1.0),
    "i": (780.0, 0.5),
    "j": (1900.0, 0.5),
    "jm": (1300.0, 0.5),
    "cu": (72000.0, 10.0),
    "al": (20500.0, 5.0),
    "zn": (23500.0, 5.0),
    "ni": (128000.0, 10.0),
    "au": (1010.0, 0.02),
    "ag": (7600.0, 1.0),
    "IF": (3860.0, 0.2),
    "IH": (2680.0, 0.2),
    "IC": (5600.0, 0.2),
    "IM": (6100.0, 0.2),
    "T": (105.0, 0.005),
}


class SimEngine:
    """随机游走行情 + 简单价格撮合，用于无网络环境联调。"""

    def __init__(self) -> None:
        self._quotes: dict[str, dict] = {}
        self._symbols: dict[str, str] = {}
        self._orders: dict[str, dict] = {}
        self._order_objects: dict[str, dict] = {}
        self._positions: dict[tuple[str, str], dict] = {}
        self._sequence = 0
        self._rng = random.Random(20261022)
        self._balance = 1_000_000.0
        self._commission = 0.0

    # -- 生命周期 ---------------------------------------------------------

    def connect(self, _auth: dict, _trade: dict) -> None:
        log("模拟引擎已启动（--sim），行情为随机游走。")

    def subscribe(self, symbols: dict[str, str]) -> None:
        for instrument_id, tq_symbol in symbols.items():
            self._symbols[instrument_id] = tq_symbol
            if instrument_id in self._quotes:
                continue
            product = "".join(ch for ch in instrument_id if ch.isalpha())
            base, tick = _SIM_BASES.get(product.upper(), (2200.0, 1.0))
            base *= 1 + self._rng.uniform(-0.01, 0.01)
            self._quotes[instrument_id] = {
                "last": round_tick(base, tick),
                "pre_close": round_tick(base, tick),
                "bid": round_tick(base - tick, tick),
                "ask": round_tick(base + tick, tick),
                "bid_volume": self._rng.randint(3, 60),
                "ask_volume": self._rng.randint(3, 60),
                "volume": self._rng.randint(1000, 90000),
                "open_interest": self._rng.randint(20000, 400000),
                "upper": round_tick(base * 1.1, tick),
                "lower": round_tick(base * 0.9, tick),
                "tick": tick,
            }

    def poll(self, timeout: float = 0.2) -> None:
        deadline = time.time() + timeout
        while time.time() < deadline:
            time.sleep(0.02)
        self._walk()
        self._match()
        self._emit_quotes()

    def shutdown(self) -> None:
        pass

    # -- 行情 -------------------------------------------------------------

    def _walk(self) -> None:
        for quote in self._quotes.values():
            tick = quote["tick"]
            drift = self._rng.choice((-1, -1, 0, 0, 0, 1, 1))
            price = quote["last"] + drift * tick
            price = max(quote["lower"], min(quote["upper"], price))
            quote["last"] = round_tick(price, tick)
            quote["bid"] = round_tick(price - tick, tick)
            quote["ask"] = round_tick(price + tick, tick)
            quote["bid_volume"] = max(1, quote["bid_volume"] + self._rng.randint(-5, 5))
            quote["ask_volume"] = max(1, quote["ask_volume"] + self._rng.randint(-5, 5))
            quote["volume"] += self._rng.randint(0, 30)
            quote["open_interest"] += self._rng.randint(-20, 20)

    def _emit_quotes(self) -> None:
        stamp = now_text()
        for instrument_id, quote in self._quotes.items():
            tick = quote["tick"]
            bids = [
                [round_tick(quote["bid"] - i * tick, tick), quote["bid_volume"] + i * 3]
                for i in range(5)
            ]
            asks = [
                [round_tick(quote["ask"] + i * tick, tick), quote["ask_volume"] + i * 3]
                for i in range(5)
            ]
            emit(
                {
                    "type": "quote",
                    "symbol": instrument_id,
                    "last": quote["last"],
                    "bid": quote["bid"],
                    "ask": quote["ask"],
                    "bid_volume": quote["bid_volume"],
                    "ask_volume": quote["ask_volume"],
                    "bids": bids,
                    "asks": asks,
                    "volume": quote["volume"],
                    "open_interest": quote["open_interest"],
                    "pre_close": quote["pre_close"],
                    "upper": quote["upper"],
                    "lower": quote["lower"],
                    "datetime": stamp,
                }
            )

    # -- 交易 -------------------------------------------------------------

    def insert_order(self, command: dict) -> str:
        self._sequence += 1
        order_id = f"SIM{self._sequence:06d}"
        instrument_id = command["symbol"]
        direction = str(command.get("direction", "BUY")).upper()
        offset = str(command.get("offset", "OPEN")).upper()
        volume = int(command.get("volume", 1))
        order_type = str(command.get("type", "LIMIT")).upper()
        quote = self._quotes.get(instrument_id)
        price = float(command.get("price") or 0)
        if order_type == "MARKET" and quote is not None:
            price = quote["ask"] if direction == "BUY" else quote["bid"]
        if price <= 0:
            raise ValueError(f"合约 {instrument_id} 无有效价格，无法下单")

        order = {
            "order_id": order_id,
            "symbol": instrument_id,
            "direction": direction,
            "offset": offset,
            "order_type": order_type,
            "limit_price": price,
            "volume_orign": volume,
            "volume_left": volume,
            "trade_price": 0.0,
            "status": "ALIVE",
            "last_msg": "已提交",
            "insert_time": now_text(),
        }
        self._orders[order_id] = order
        self._order_objects[order_id] = order
        self._push_order(order)

        if order_type == "MARKET":
            self._fill(order, volume, price)
        elif order_type == "FOK":
            quote = self._quotes.get(instrument_id)
            crossed = quote is not None and (
                (direction == "BUY" and price >= quote["ask"])
                or (direction == "SELL" and price <= quote["bid"])
            )
            if crossed:
                self._fill(order, volume, quote["ask"] if direction == "BUY" else quote["bid"])
            else:
                order["status"] = "FINISHED"
                order["last_msg"] = "FOK 未成交，已撤销"
                self._push_order(order)
        elif order_type == "FAK":
            quote = self._quotes.get(instrument_id)
            crossed = quote is not None and (
                (direction == "BUY" and price >= quote["ask"])
                or (direction == "SELL" and price <= quote["bid"])
            )
            if crossed:
                self._fill(order, volume, quote["ask"] if direction == "BUY" else quote["bid"])
            else:
                order["status"] = "FINISHED"
                order["last_msg"] = "FAK 未成交，已撤销"
                self._push_order(order)
        else:
            self._match_order(order)
        return order_id

    def cancel_order(self, order_id: str) -> None:
        order = self._orders.get(order_id)
        if order is None:
            raise ValueError(f"未找到委托 {order_id}")
        if order["status"] != "ALIVE":
            raise ValueError(f"委托 {order_id} 已结束，无法撤单")
        order["status"] = "FINISHED"
        order["last_msg"] = "已撤单"
        self._push_order(order)

    def _match(self) -> None:
        for order in list(self._orders.values()):
            if order["status"] == "ALIVE":
                self._match_order(order)

    def _match_order(self, order: dict) -> None:
        quote = self._quotes.get(order["symbol"])
        if quote is None:
            return
        if order["direction"] == "BUY" and order["limit_price"] >= quote["ask"]:
            self._fill(order, order["volume_left"], quote["ask"])
        elif order["direction"] == "SELL" and order["limit_price"] <= quote["bid"]:
            self._fill(order, order["volume_left"], quote["bid"])

    def _fill(self, order: dict, volume: int, price: float) -> None:
        if volume <= 0:
            return
        order["volume_left"] -= volume
        order["trade_price"] = price
        if order["volume_left"] <= 0:
            order["status"] = "FINISHED"
            order["last_msg"] = "全部成交"
        else:
            order["last_msg"] = "部分成交"
        self._push_order(order)
        self._apply_position(order, volume, price)
        emit(
            {
                "type": "trade",
                "order_id": order["order_id"],
                "symbol": order["symbol"],
                "direction": order["direction"],
                "offset": order["offset"],
                "volume": volume,
                "price": price,
                "time": now_text(),
            }
        )
        self._commission += 0.5 * volume
        self._push_account()

    def _apply_position(self, order: dict, volume: int, price: float) -> None:
        direction = "LONG" if order["direction"] == "BUY" else "SHORT"
        key = (order["symbol"], direction)
        position = self._positions.setdefault(key, {"volume": 0, "avg": 0.0})

        if order["offset"].startswith("CLOSE"):
            sign = -1
        else:
            sign = 1

        new_volume = position["volume"] + sign * volume
        if new_volume <= 0:
            position["volume"] = 0
            position["avg"] = 0.0
            return

        if sign > 0:
            total = position["avg"] * position["volume"] + price * volume
            position["avg"] = total / new_volume
        position["volume"] = new_volume

    def _push_order(self, order: dict) -> None:
        emit({"type": "order", **order, "update_time": now_text()})

    def _push_account(self) -> None:
        emit({"type": "account", **self.account_snapshot()})

    def account_snapshot(self) -> dict:
        float_profit = 0.0
        margin = 0.0
        for (symbol, direction), position in self._positions.items():
            quote = self._quotes.get(symbol)
            if quote is None or position["volume"] <= 0:
                continue
            if direction == "LONG":
                float_profit += (quote["last"] - position["avg"]) * position["volume"]
            else:
                float_profit += (position["avg"] - quote["last"]) * position["volume"]
            margin += quote["last"] * position["volume"] * 0.1
        return {
            "balance": round(self._balance + float_profit - self._commission, 2),
            "available": round(self._balance + float_profit - self._commission - margin, 2),
            "margin": round(margin, 2),
            "float_profit": round(float_profit, 2),
            "close_profit": 0.0,
            "commission": round(self._commission, 2),
            "risk_ratio": round(margin / max(1.0, self._balance), 4),
        }

    def positions(self) -> list[dict]:
        items = []
        for (symbol, direction), position in self._positions.items():
            if position["volume"] <= 0:
                continue
            quote = self._quotes.get(symbol)
            items.append(
                {
                    "symbol": symbol,
                    "direction": direction,
                    "volume": position["volume"],
                    "avg_price": round(position["avg"], 6),
                    "last_price": quote["last"] if quote else 0.0,
                }
            )
        return items


# ---------------------------------------------------------------------------
# 真实 TqSdk 引擎
# ---------------------------------------------------------------------------


class TqEngine:
    def __init__(self, auth: dict, trade: dict) -> None:
        try:
            from tqsdk import TqAccount, TqApi, TqAuth, TqKq
        except ImportError as exc:
            raise RuntimeError(
                "未安装 tqsdk。请在命令行执行：python -m pip install -U tqsdk"
            ) from exc

        account = None
        mode = str(trade.get("mode", "none")).lower()
        if mode == "kq":
            account = TqKq()
        elif mode == "account":
            account = TqAccount(
                trade.get("broker", ""),
                trade.get("account") or auth.get("user", ""),
                trade.get("password") or auth.get("password", ""),
            )

        kwargs = {"auth": TqAuth(auth.get("user", ""), auth.get("password", ""))}
        if account is not None:
            kwargs["account"] = account
        self._api = TqApi(**kwargs)
        self._quotes: dict[str, object] = {}
        self._symbols: dict[str, str] = {}
        self._last_quote: dict[str, tuple] = {}
        self._orders: dict[str, object] = {}
        self._order_state: dict[str, tuple] = {}
        self._last_account = 0.0
        log(f"TqSdk 已连接（交易模式：{mode}）。")

    def connect(self, _auth: dict, _trade: dict) -> None:
        pass

    def subscribe(self, symbols: dict[str, str]) -> None:
        failed = []
        for instrument_id, tq_symbol in symbols.items():
            try:
                self._quotes[instrument_id] = self._api.get_quote(tq_symbol)
                self._symbols[instrument_id] = tq_symbol
            except Exception as exc:  # 单个合约不存在不应中断整批订阅
                failed.append(f"{instrument_id}: {exc}")
        if failed:
            log("以下合约订阅失败：" + "；".join(failed))

    def poll(self, timeout: float = 0.2) -> None:
        updated = self._api.wait_update(deadline=time.time() + max(0.05, timeout))
        if not updated:
            return

        stamp = now_text()
        for instrument_id, quote in self._quotes.items():
            grade = (
                getattr(quote, "last_price", None),
                getattr(quote, "bid_price1", None),
                getattr(quote, "ask_price1", None),
                getattr(quote, "bid_volume1", None),
                getattr(quote, "ask_volume1", None),
                getattr(quote, "volume", None),
                getattr(quote, "open_interest", None),
            )
            if self._last_quote.get(instrument_id) == grade:
                continue
            self._last_quote[instrument_id] = grade
            bids = [
                [
                    _number(getattr(quote, f"bid_price{i}", 0)),
                    int(_number(getattr(quote, f"bid_volume{i}", 0))),
                ]
                for i in range(1, 6)
            ]
            asks = [
                [
                    _number(getattr(quote, f"ask_price{i}", 0)),
                    int(_number(getattr(quote, f"ask_volume{i}", 0))),
                ]
                for i in range(1, 6)
            ]
            emit(
                {
                    "type": "quote",
                    "symbol": instrument_id,
                    "last": _number(getattr(quote, "last_price", 0)),
                    "bid": _number(getattr(quote, "bid_price1", 0)),
                    "ask": _number(getattr(quote, "ask_price1", 0)),
                    "bid_volume": int(_number(getattr(quote, "bid_volume1", 0))),
                    "ask_volume": int(_number(getattr(quote, "ask_volume1", 0))),
                    "bids": bids,
                    "asks": asks,
                    "volume": int(_number(getattr(quote, "volume", 0))),
                    "open_interest": _number(getattr(quote, "open_interest", 0)),
                    "pre_close": _number(getattr(quote, "pre_close", 0)),
                    "upper": _number(getattr(quote, "upper_limit", 0)),
                    "lower": _number(getattr(quote, "lower_limit", 0)),
                    "datetime": stamp,
                }
            )

        for order in self._api.get_order().values():
            state = (
                order.status,
                order.volume_left,
                order.trade_price,
                order.last_msg,
            )
            if self._order_state.get(order.order_id) == state:
                continue
            self._order_state[order.order_id] = state
            self._orders[order.order_id] = order
            emit(
                {
                    "type": "order",
                    "order_id": order.order_id,
                    "symbol": self._symbols_inverse(order.exchange_id, order.instrument_id),
                    "direction": order.direction,
                    "offset": order.offset,
                    "limit_price": _number(order.limit_price),
                    "volume_orign": int(_number(order.volume_orign)),
                    "volume_left": int(_number(order.volume_left)),
                    "trade_price": _number(order.trade_price),
                    "status": order.status,
                    "last_msg": order.last_msg,
                    "insert_time": str(order.insert_date_time),
                    "update_time": stamp,
                }
            )

        if time.time() - self._last_account > 1.0:
            self._last_account = time.time()
            account = self._api.get_account()
            emit(
                {
                    "type": "account",
                    "balance": _number(account.balance),
                    "available": _number(account.available),
                    "margin": _number(account.margin),
                    "float_profit": _number(account.float_profit),
                    "close_profit": _number(account.close_profit),
                    "commission": _number(account.commission),
                    "risk_ratio": _number(account.risk_ratio),
                }
            )

    def _symbols_inverse(self, exchange_id: str, instrument_id: str) -> str:
        full = f"{exchange_id}.{instrument_id}" if exchange_id else instrument_id
        for instrument, symbol in self._symbols.items():
            if symbol.upper() == full.upper():
                return instrument
        return instrument_id

    def insert_order(self, command: dict) -> str:
        symbol = self._symbols.get(command["symbol"], command["symbol"])
        order_type = str(command.get("type", "LIMIT")).upper()
        limit_price = None
        if order_type != "MARKET":
            limit_price = float(command["price"])
        kwargs = {
            "symbol": symbol,
            "direction": str(command.get("direction", "BUY")).upper(),
            "offset": str(command.get("offset", "OPEN")).upper(),
            "volume": int(command.get("volume", 1)),
            "limit_price": limit_price,
        }
        if order_type in ("FAK", "FOK"):
            kwargs["advanced"] = order_type
        order = self._api.insert_order(**kwargs)
        self._orders[order.order_id] = order
        return order.order_id

    def cancel_order(self, order_id: str) -> None:
        order = self._orders.get(order_id)
        if order is None:
            raise ValueError(f"未找到委托 {order_id}")
        self._api.cancel_order(order)

    def account_snapshot(self) -> dict:
        account = self._api.get_account()
        return {
            "balance": _number(account.balance),
            "available": _number(account.available),
            "margin": _number(account.margin),
            "float_profit": _number(account.float_profit),
            "close_profit": _number(account.close_profit),
            "commission": _number(account.commission),
            "risk_ratio": _number(account.risk_ratio),
        }

    def positions(self) -> list[dict]:
        items = []
        for position in self._api.get_position().values():
            symbol = self._symbols_inverse(position.exchange_id, position.instrument_id)
            if int(_number(position.pos_long)) > 0:
                items.append(
                    {
                        "symbol": symbol,
                        "direction": "LONG",
                        "volume": int(_number(position.pos_long)),
                        "avg_price": _number(position.open_price_long),
                        "last_price": _number(position.last_price),
                    }
                )
            if int(_number(position.pos_short)) > 0:
                items.append(
                    {
                        "symbol": symbol,
                        "direction": "SHORT",
                        "volume": int(_number(position.pos_short)),
                        "avg_price": _number(position.open_price_short),
                        "last_price": _number(position.last_price),
                    }
                )
        return items

    def shutdown(self) -> None:
        try:
            self._api.close()
        except Exception:
            pass


def _number(value) -> float:
    try:
        number = float(value)
        if math.isnan(number) or math.isinf(number):
            return 0.0
        return number
    except (TypeError, ValueError):
        return 0.0


# ---------------------------------------------------------------------------
# 主循环
# ---------------------------------------------------------------------------


def main() -> int:
    parser = argparse.ArgumentParser(description="TqSdk bridge")
    parser.add_argument("--sim", action="store_true", help="使用内置模拟引擎，不连接 tqsdk")
    args = parser.parse_args()

    commands: "queue.Queue[dict]" = queue.Queue()
    state = {"running": True, "engine": None, "source": "sim" if args.sim else "tqsdk"}

    def reader() -> None:
        for line in sys.stdin:
            line = line.strip()
            if not line:
                continue
            try:
                commands.put(json.loads(line))
            except json.JSONDecodeError as exc:
                log(f"命令解析失败：{exc}")
        state["running"] = False

    threading.Thread(target=reader, name="stdin-reader", daemon=True).start()

    emit({"type": "log", "message": "桥接进程已启动，等待连接参数。"})

    try:
        while state["running"]:
            timeout = 0.2
            try:
                command = commands.get(timeout=timeout)
            except queue.Empty:
                command = None

            if command is not None:
                if not handle_command(command, state):
                    break

            engine = state["engine"]
            if engine is not None:
                engine.poll(timeout)
    except KeyboardInterrupt:
        pass
    except Exception as exc:
        traceback.print_exc(file=sys.stderr)
        emit({"type": "fatal", "message": str(exc)})
        return 1
    finally:
        engine = state["engine"]
        if engine is not None:
            engine.shutdown()

    return 0


def handle_command(command: dict, state: dict) -> bool:
    cmd = str(command.get("cmd", "")).lower()
    request_id = command.get("id")

    if cmd == "shutdown":
        return False

    if cmd == "connect":
        try:
            if state["engine"] is not None:
                raise ValueError("重复的 connect 命令")
            if state["source"] == "sim":
                engine = SimEngine()
            else:
                engine = TqEngine(command.get("auth", {}), command.get("trade", {}))
            state["engine"] = engine
            engine.connect(command.get("auth", {}), command.get("trade", {}))
            emit({"type": "ready", "id": request_id, "source": state["source"]})
        except Exception as exc:
            emit({"type": "error", "id": request_id, "message": f"{exc}"})
            log(f"连接失败：{exc}")
            return False
        return True

    engine = state["engine"]
    if engine is None:
        emit({"type": "error", "id": request_id, "message": "尚未 connect"})
        return True

    try:
        if cmd == "subscribe":
            symbols = {
                str(key): str(value)
                for key, value in (command.get("symbols") or {}).items()
            }
            engine.subscribe(symbols)
            emit({"type": "ack", "id": request_id})
        elif cmd == "insert_order":
            order_id = engine.insert_order(command)
            emit({"type": "ack", "id": request_id, "order_id": order_id})
        elif cmd == "cancel_order":
            engine.cancel_order(str(command.get("order_id", "")))
            emit({"type": "ack", "id": request_id})
        elif cmd == "query_positions":
            emit({"type": "positions", "id": request_id, "items": engine.positions()})
        elif cmd == "query_account":
            emit({"type": "account", "id": request_id, **engine.account_snapshot()})
        else:
            emit({"type": "error", "id": request_id, "message": f"未知命令 {cmd}"})
    except Exception as exc:
        emit({"type": "error", "id": request_id, "message": f"{exc}"})
        log(f"{cmd} 失败：{exc}")

    return True


if __name__ == "__main__":
    sys.exit(main())
