// Order Trader v1.0 - 核心数据模型
#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace ot {

constexpr int kDepthLevels = 5;
constexpr int kSliceMs = 500;  // 行情切片：500ms

struct DepthLevel {
    double price = 0.0;
    long long volume = 0;
};

struct Depth {
    DepthLevel bids[kDepthLevels];
    DepthLevel asks[kDepthLevels];
};

struct Quote {
    std::string symbol;
    std::string name;
    int64_t ts = 0;             // 毫秒时间戳
    double last = 0.0;
    double preClose = 0.0;
    double preSettle = 0.0;
    double open = 0.0;
    double high = 0.0;
    double low = 0.0;
    double upperLimit = 0.0;
    double lowerLimit = 0.0;
    long long volume = 0;
    long long openInterest = 0;
    Depth depth;
    bool valid = false;
};

/// 单个 tick（tqsdk tick 序列的一行）
struct Tick {
    std::string symbol;
    int64_t ts = 0;             // 毫秒
    double last = 0.0;
    double bid1 = 0.0;
    double ask1 = 0.0;
    long long volumeDelta = 0;  // 本 tick 成交量
    int side = 0;               // +1 主动买 / -1 主动卖 / 0 未知
    long long totalVolume = 0;
    long long openInterest = 0;
    Depth depth;
    bool hasDepth = false;
};

/// 某价位在一个 500ms 切片内的成交量（key = 价格 / 最小变动价位）
struct Footprint {
    long long key = 0;
    long long buy = 0;
    long long sell = 0;
    long long total() const { return buy + sell; }
};

struct Slice {
    std::string symbol;
    int64_t ts = 0;             // 切片起点（毫秒）
    double last = 0.0;
    Depth depth;
    long long volume = 0;
    long long buyVolume = 0;
    long long sellVolume = 0;
    std::vector<Footprint> cells;

    const Footprint* cellAt(long long key) const {
        for (const auto& c : cells) {
            if (c.key == key) return &c;
        }
        return nullptr;
    }
};

struct SymbolMeta {
    std::string symbol;
    std::string name;
    std::string exchange;
    std::string product;
    double priceTick = 1.0;
    int priceDecs = 0;
    double volumeMultiple = 1.0;
    double commission = 0.0;   // 元/手（含交易所与经纪商，tqsdk 接口返回）
    double margin = 0.0;
    bool expired = false;
    bool valid = false;

    long long priceKey(double price) const {
        const double tick = priceTick > 0 ? priceTick : 1.0;
        const double k = price / tick;
        return static_cast<long long>(k >= 0 ? (k + 0.5) : (k - 0.5));
    }
};

enum class RunMode { Live, Replay };

constexpr int kPageCount = 20;
constexpr int kQuickPages = 12;  // F1..F12

}  // namespace ot
