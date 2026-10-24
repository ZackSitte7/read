#include "MarketData.h"

#include <algorithm>
#include <functional>

namespace ot {

void Slicer::reset() { buckets_.clear(); }

bool Slicer::push(const Tick& tick, const SymbolMeta& meta, Slice* out) {
    const int64_t bucketStart = tick.ts - tick.ts % kSliceMs;
    Bucket& bucket = buckets_[tick.symbol];
    bool completed = false;

    if (!bucket.active) {
        bucket.active = true;
        bucket.slice = Slice();
        bucket.slice.symbol = tick.symbol;
        bucket.slice.ts = bucketStart;
        bucket.index.clear();
    } else if (bucketStart > bucket.slice.ts) {
        if (out) *out = bucket.slice;
        completed = true;
        bucket.slice = Slice();
        bucket.slice.symbol = tick.symbol;
        bucket.slice.ts = bucketStart;
        bucket.index.clear();
    }

    Slice& slice = bucket.slice;
    if (tick.last > 0) slice.last = tick.last;
    if (tick.hasDepth) slice.depth = tick.depth;
    if (tick.volumeDelta > 0) {
        slice.volume += tick.volumeDelta;
        if (tick.side > 0) {
            slice.buyVolume += tick.volumeDelta;
        } else if (tick.side < 0) {
            slice.sellVolume += tick.volumeDelta;
        } else {
            const long long half = tick.volumeDelta / 2;
            slice.buyVolume += half;
            slice.sellVolume += tick.volumeDelta - half;
        }
        const long long key = meta.priceKey(tick.last);
        auto it = bucket.index.find(key);
        if (it == bucket.index.end()) {
            Footprint cell;
            cell.key = key;
            bucket.index[key] = slice.cells.size();
            slice.cells.push_back(cell);
            it = bucket.index.find(key);
        }
        Footprint& cell = slice.cells[it->second];
        if (tick.side > 0) {
            cell.buy += tick.volumeDelta;
        } else if (tick.side < 0) {
            cell.sell += tick.volumeDelta;
        } else {
            const long long half = tick.volumeDelta / 2;
            cell.buy += half;
            cell.sell += tick.volumeDelta - half;
        }
    }
    return completed;
}

bool Slicer::flush(const std::string& symbol, Slice* out) {
    auto it = buckets_.find(symbol);
    if (it == buckets_.end() || !it->second.active) return false;
    if (it->second.slice.volume <= 0) {
        it->second.active = false;
        return false;
    }
    if (out) *out = it->second.slice;
    it->second.active = false;
    it->second.index.clear();
    return true;
}

void ReplayEngine::clear() {
    sources_.clear();
    total_ = 0;
    emitted_ = 0;
    dataCursor_ = 0;
    lastWall_ = 0;
    active_ = false;
}

void ReplayEngine::addTicks(const std::string& symbol, std::vector<Tick> ticks) {
    if (ticks.empty()) return;
    std::stable_sort(ticks.begin(), ticks.end(),
                     [](const Tick& a, const Tick& b) { return a.ts < b.ts; });
    Source src;
    src.symbol = symbol;
    src.ticks = std::move(ticks);
    total_ += src.ticks.size();
    sources_.push_back(std::move(src));
}

void ReplayEngine::start(double speed, int64_t wallNow) {
    speed_ = speed > 0 ? speed : 1.0;
    lastWall_ = wallNow;
    dataCursor_ = 0;
    emitted_ = 0;
    for (auto& src : sources_) src.cursor = 0;
    active_ = !sources_.empty();
    if (active_) dataCursor_ = nextTimestamp();
}

void ReplayEngine::stop() { active_ = false; }

int64_t ReplayEngine::nextTimestamp() const {
    int64_t best = 0;
    bool found = false;
    for (const auto& src : sources_) {
        if (src.cursor >= src.ticks.size()) continue;
        const int64_t ts = src.ticks[src.cursor].ts;
        if (!found || ts < best) {
            best = ts;
            found = true;
        }
    }
    return found ? best : 0;
}

std::vector<Tick> ReplayEngine::pump(int64_t wallNow) {
    std::vector<Tick> out;
    if (!active_) return out;

    const int64_t next = nextTimestamp();
    if (next == 0) {
        active_ = false;
        return out;
    }

    const int64_t elapsed = wallNow - lastWall_;
    lastWall_ = wallNow;

    // 相邻行情间隔过大（休市/隔夜）直接跳空，避免回放长时间空等
    const int64_t gap = next - dataCursor_;
    if (dataCursor_ == 0 || gap > 3000) {
        dataCursor_ = next;
    } else {
        dataCursor_ += static_cast<int64_t>(static_cast<double>(elapsed) * speed_);
    }

    const std::size_t limit = 4000;
    std::size_t guard = 0;
    while (out.size() < limit && guard++ < limit * 4) {
        int64_t best = 0;
        bool found = false;
        for (const auto& src : sources_) {
            if (src.cursor >= src.ticks.size()) continue;
            const int64_t ts = src.ticks[src.cursor].ts;
            if (!found || ts < best) {
                best = ts;
                found = true;
            }
        }
        if (!found) break;
        if (best > dataCursor_) break;
        for (auto& src : sources_) {
            while (src.cursor < src.ticks.size() && src.ticks[src.cursor].ts <= dataCursor_) {
                out.push_back(src.ticks[src.cursor]);
                ++src.cursor;
                ++emitted_;
            }
        }
    }

    if (emitted_ >= total_) active_ = false;
    return out;
}

}  // namespace ot
