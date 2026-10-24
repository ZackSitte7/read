#include "SimEngine.h"

#include <algorithm>
#include <cmath>

#include "util/Win.h"

namespace ot {

namespace {
constexpr double kEps = 1e-9;
}

void SimEngine::reset(double initialBalance) {
    initial_ = initialBalance;
    nextId_ = 1;
    positions_.clear();
    orders_.clear();
    fills_.clear();
    curve_.clear();
    const int64_t ts = static_cast<int64_t>(GetTickCount64());
    curve_.push_back({ts, initialBalance});
}

void SimEngine::setMeta(const SymbolMeta& meta) {
    metas_[meta.symbol] = meta;
}

const SymbolMeta* SimEngine::meta(const std::string& symbol) const {
    const auto it = metas_.find(symbol);
    return it == metas_.end() ? nullptr : &it->second;
}

long long SimEngine::placeOrder(const std::string& symbol, bool buy, double price, long long qty,
                                int64_t ts) {
    if (qty <= 0 || price <= 0) return 0;
    SimOrder order;
    order.id = nextId_++;
    order.symbol = symbol;
    order.buy = buy;
    order.price = price;
    order.qty = qty;
    order.ts = ts;

    // 若下单价已穿过对手价，立即以对手价成交
    const auto pit = positions_.find(symbol);
    if (pit != positions_.end() && pit->second.last > 0) {
        const SimPosition& pos = pit->second;
        if (buy && pos.ask1 > 0 && pos.ask1 <= price + kEps) {
            applyFill(order, std::min(price, pos.ask1), ts);
            return order.id;
        }
        if (!buy && pos.bid1 > 0 && pos.bid1 >= price - kEps) {
            applyFill(order, std::max(price, pos.bid1), ts);
            return order.id;
        }
    }
    orders_.push_back(order);
    return order.id;
}

bool SimEngine::cancelOrder(long long id) {
    for (auto it = orders_.begin(); it != orders_.end(); ++it) {
        if (it->id == id) {
            orders_.erase(it);
            return true;
        }
    }
    return false;
}

int SimEngine::cancelAll() {
    const int count = static_cast<int>(orders_.size());
    orders_.clear();
    return count;
}

int SimEngine::cancelForSymbol(const std::string& symbol) {
    int count = 0;
    for (auto it = orders_.begin(); it != orders_.end();) {
        if (it->symbol == symbol) {
            it = orders_.erase(it);
            ++count;
        } else {
            ++it;
        }
    }
    return count;
}

void SimEngine::onTick(const Tick& tick) {
    auto pit = positions_.find(tick.symbol);
    if (pit == positions_.end()) {
        SimPosition pos;
        pos.last = tick.last;
        positions_[tick.symbol] = pos;
        pit = positions_.find(tick.symbol);
    }
    if (tick.last > 0) pit->second.last = tick.last;
    if (tick.bid1 > 0) pit->second.bid1 = tick.bid1;
    if (tick.ask1 > 0) pit->second.ask1 = tick.ask1;

    if (tick.last <= 0) return;

    std::vector<std::pair<SimOrder, double>> filled;
    for (auto it = orders_.begin(); it != orders_.end();) {
        if (it->symbol != tick.symbol) {
            ++it;
            continue;
        }
        bool hit = false;
        double price = 0.0;
        if (it->buy) {
            // 卖价（红格）压到买单价位 → 买入成交
            const double ask = tick.ask1 > 0 ? tick.ask1 : tick.last;
            if (ask > 0 && ask <= it->price + kEps) {
                hit = true;
                price = std::min(it->price, ask);
            }
        } else {
            const double bid = tick.bid1 > 0 ? tick.bid1 : tick.last;
            if (bid > 0 && bid >= it->price - kEps) {
                hit = true;
                price = std::max(it->price, bid);
            }
        }
        if (hit) {
            filled.emplace_back(*it, price);
            it = orders_.erase(it);
        } else {
            ++it;
        }
    }
    for (const auto& item : filled) applyFill(item.first, item.second, tick.ts);
}

void SimEngine::applyFill(const SimOrder& order, double price, int64_t ts) {
    const double fillPrice = price;
    auto mit = metas_.find(order.symbol);
    SymbolMeta meta;
    if (mit != metas_.end()) meta = mit->second;
    else meta.symbol = order.symbol;
    const double mult = meta.volumeMultiple > 0 ? meta.volumeMultiple : 1.0;

    auto pit = positions_.find(order.symbol);
    if (pit == positions_.end()) {
        positions_[order.symbol] = SimPosition();
        pit = positions_.find(order.symbol);
    }
    SimPosition& pos = pit->second;

    long long remaining = order.qty;
    long long closeQty = 0;
    double pnl = 0.0;

    if (order.buy) {
        if (pos.qty < 0) {
            const long long close = std::min(remaining, -pos.qty);
            pnl += (pos.avg - fillPrice) * static_cast<double>(close) * mult;
            pos.qty += close;
            remaining -= close;
            closeQty += close;
        }
        if (remaining > 0) {
            const double base = pos.qty > 0 ? pos.avg * static_cast<double>(pos.qty) : 0.0;
            pos.avg = (base + fillPrice * static_cast<double>(remaining)) /
                      static_cast<double>(pos.qty + remaining);
            pos.qty += remaining;
        }
    } else {
        if (pos.qty > 0) {
            const long long close = std::min(remaining, pos.qty);
            pnl += (fillPrice - pos.avg) * static_cast<double>(close) * mult;
            pos.qty -= close;
            remaining -= close;
            closeQty += close;
        }
        if (remaining > 0) {
            const double base = pos.qty < 0 ? pos.avg * static_cast<double>(-pos.qty) : 0.0;
            pos.avg = (base + fillPrice * static_cast<double>(remaining)) /
                      static_cast<double>(-pos.qty + remaining);
            pos.qty -= remaining;
        }
    }

    if (pos.qty == 0) pos.avg = 0.0;
    pos.realized += pnl;

    const double feePerLot = meta.commission > 0 ? meta.commission : feeFallback_;
    const double fee = feePerLot * static_cast<double>(order.qty);
    pos.fees += fee;

    SimFill fill;
    fill.ts = ts;
    fill.symbol = order.symbol;
    fill.buy = order.buy;
    fill.qty = order.qty;
    fill.price = fillPrice;
    fill.closeQty = closeQty;
    fill.pnl = pnl;
    fill.fee = fee;
    fills_.push_back(fill);
    if (fills_.size() > 20000) fills_.erase(fills_.begin(), fills_.begin() + 10000);
}

void SimEngine::markPrice(const std::string& symbol, double last) {
    if (last <= 0) return;
    auto pit = positions_.find(symbol);
    if (pit == positions_.end()) return;
    pit->second.last = last;
}

long long SimEngine::positionQty(const std::string& symbol) const {
    const auto it = positions_.find(symbol);
    return it == positions_.end() ? 0 : it->second.qty;
}

double SimEngine::positionAvg(const std::string& symbol) const {
    const auto it = positions_.find(symbol);
    return it == positions_.end() ? 0.0 : it->second.avg;
}

double SimEngine::symbolFees(const std::string& symbol) const {
    const auto it = positions_.find(symbol);
    return it == positions_.end() ? 0.0 : it->second.fees;
}

double SimEngine::netPnl(const std::string& symbol) const {
    const auto it = positions_.find(symbol);
    if (it == positions_.end()) return 0.0;
    const SimPosition& pos = it->second;
    if (pos.qty == 0 || pos.last <= 0) return pos.realized;
    const auto mit = metas_.find(symbol);
    const double mult = (mit != metas_.end() && mit->second.volumeMultiple > 0)
                            ? mit->second.volumeMultiple
                            : 1.0;
    return pos.realized + (pos.last - pos.avg) * static_cast<double>(pos.qty) * mult;
}

double SimEngine::realized() const {
    double sum = 0.0;
    for (const auto& kv : positions_) sum += kv.second.realized;
    return sum;
}

double SimEngine::fees() const {
    double sum = 0.0;
    for (const auto& kv : positions_) sum += kv.second.fees;
    return sum;
}

double SimEngine::unrealized() const {
    double sum = 0.0;
    for (const auto& kv : positions_) {
        const auto mit = metas_.find(kv.first);
        const double mult = (mit != metas_.end() && mit->second.volumeMultiple > 0)
                                ? mit->second.volumeMultiple
                                : 1.0;
        if (kv.second.qty != 0 && kv.second.last > 0) {
            sum += (kv.second.last - kv.second.avg) * static_cast<double>(kv.second.qty) * mult;
        }
    }
    return sum;
}

double SimEngine::balance() const { return initial_ + realized() - fees(); }
double SimEngine::equity() const { return balance() + unrealized(); }

void SimEngine::sampleEquity(int64_t ts) {
    const double value = equity();
    if (!curve_.empty() && std::fabs(curve_.back().equity - value) < 0.005) {
        curve_.back().ts = ts;
        return;
    }
    curve_.push_back({ts, value});
    if (curve_.size() > 4000) curve_.erase(curve_.begin(), curve_.begin() + 1000);
}

std::vector<SimOrder> SimEngine::workingOrders(const std::string& symbol) const {
    std::vector<SimOrder> out;
    for (const auto& order : orders_) {
        if (order.symbol == symbol) out.push_back(order);
    }
    return out;
}

std::map<long long, long long> SimEngine::workingByPrice(const std::string& symbol) const {
    std::map<long long, long long> out;
    const auto mit = metas_.find(symbol);
    const double tick = (mit != metas_.end() && mit->second.priceTick > 0) ? mit->second.priceTick : 1.0;
    for (const auto& order : orders_) {
        if (order.symbol != symbol) continue;
        const long long key = static_cast<long long>(order.price / tick + (order.price >= 0 ? 0.5 : -0.5));
        out[key] += order.buy ? order.qty : -order.qty;
    }
    return out;
}

}  // namespace ot
