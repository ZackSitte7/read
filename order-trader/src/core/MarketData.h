// Order Trader v1.0 - 500ms 切片与历史回放
#pragma once

#include <map>
#include <string>
#include <vector>

#include "Types.h"

namespace ot {

/// 把逐 tick 数据聚合成 500ms 切片（按价格统计主动买/主动卖成交量）
class Slicer {
public:
    void reset();
    /// 返回 true 表示完成了一个切片，out 为已完成切片
    bool push(const Tick& tick, const SymbolMeta& meta, Slice* out);
    bool flush(const std::string& symbol, Slice* out);

private:
    struct Bucket {
        bool active = false;
        Slice slice;
        std::map<long long, std::size_t> index;  // key -> cells 下标
    };
    std::map<std::string, Bucket> buckets_;
};

/// 历史行情回放：多合约按时间戳归并对齐，按可调倍速推进
class ReplayEngine {
public:
    void clear();
    void addTicks(const std::string& symbol, std::vector<Tick> ticks);
    void start(double speed, int64_t wallNow);
    void stop();
    bool active() const { return active_; }
    bool finished() const { return active_ && emitted_ >= total_; }

    /// 推进回放，返回按时间戳排序后的 tick 列表
    std::vector<Tick> pump(int64_t wallNow);
    void setSpeed(double speed) { speed_ = speed > 0 ? speed : 1.0; }

    std::size_t total() const { return total_; }
    std::size_t emitted() const { return emitted_; }
    double progress() const {
        return total_ == 0 ? 0.0 : static_cast<double>(emitted_) / static_cast<double>(total_);
    }

private:
    int64_t nextTimestamp() const;

    struct Source {
        std::string symbol;
        std::vector<Tick> ticks;
        std::size_t cursor = 0;
    };
    std::vector<Source> sources_;
    std::size_t total_ = 0;
    std::size_t emitted_ = 0;
    int64_t dataCursor_ = 0;
    int64_t lastWall_ = 0;
    bool active_ = false;
    double speed_ = 1.0;
};

}  // namespace ot
