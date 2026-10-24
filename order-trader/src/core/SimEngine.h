// Order Trader v1.0 - 本地虚拟撮合（订单流模拟盘）
#pragma once

#include <map>
#include <string>
#include <vector>

#include "Types.h"

namespace ot {

struct SimOrder {
    long long id = 0;
    std::string symbol;
    bool buy = true;
    double price = 0.0;
    long long qty = 0;
    int64_t ts = 0;
};

struct SimFill {
    int64_t ts = 0;
    std::string symbol;
    bool buy = true;
    long long qty = 0;
    double price = 0.0;
    long long closeQty = 0;
    double pnl = 0.0;    // 平仓盈亏（不含手续费）
    double fee = 0.0;
};

struct SimPosition {
    long long qty = 0;
    double avg = 0.0;
    double realized = 0.0;
    double fees = 0.0;
    double last = 0.0;
    double bid1 = 0.0;
    double ask1 = 0.0;
};

struct EquitySample {
    int64_t ts = 0;
    double equity = 0.0;
};

class SimEngine {
public:
    void reset(double initialBalance);

    void setMeta(const SymbolMeta& meta);
    const SymbolMeta* meta(const std::string& symbol) const;
    void setFeeFallback(double feePerLot) { feeFallback_ = feePerLot; }

    long long placeOrder(const std::string& symbol, bool buy, double price, long long qty, int64_t ts);
    bool cancelOrder(long long id);
    int cancelAll();
    int cancelForSymbol(const std::string& symbol);

    /// 以最新 tick 驱动撮合：挂单价格与对手盘接触即成交
    void onTick(const Tick& tick);
    void markPrice(const std::string& symbol, double last);
    void sampleEquity(int64_t ts);

    long long positionQty(const std::string& symbol) const;
    double positionAvg(const std::string& symbol) const;
    double netPnl(const std::string& symbol) const;  // 持仓净盈亏（不含手续费）
    double symbolFees(const std::string& symbol) const;

    double initialBalance() const { return initial_; }
    double realized() const;
    double unrealized() const;
    double fees() const;
    double balance() const;  // 初始资金 + 平仓盈亏 - 手续费
    double equity() const;   // balance + 浮动盈亏

    const std::map<std::string, SimPosition>& positions() const { return positions_; }
    const std::vector<SimFill>& fills() const { return fills_; }
    const std::vector<SimOrder>& orders() const { return orders_; }
    const std::vector<EquitySample>& equityCurve() const { return curve_; }

    std::vector<SimOrder> workingOrders(const std::string& symbol) const;
    std::map<long long, long long> workingByPrice(const std::string& symbol) const;
    int orderCount() const { return static_cast<int>(orders_.size()); }

private:
    void applyFill(const SimOrder& order, double price, int64_t ts);

    double initial_ = 0.0;
    double feeFallback_ = 1.5;
    long long nextId_ = 1;
    std::map<std::string, SymbolMeta> metas_;
    std::map<std::string, SimPosition> positions_;
    std::vector<SimOrder> orders_;
    std::vector<SimFill> fills_;
    std::vector<EquitySample> curve_;
};

}  // namespace ot
