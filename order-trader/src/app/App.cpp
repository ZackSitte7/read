#include "App.h"

#include <algorithm>
#include <cstdio>

#include "MainWindow.h"
#include "LoginWindow.h"
#include "util/Log.h"
#include "util/Win.h"

namespace ot {

namespace {

constexpr double kSpeeds[] = {0.5, 1.0, 2.0, 5.0, 10.0};

/// 品种 -> 交易所，用于把 rb2701 补全为 SHFE.rb2701
const std::vector<std::pair<std::string, std::string>>& exchangeProducts() {
    static const std::vector<std::pair<std::string, std::string>> table = {
        {"SHFE", "rb hc cu al zn pb ni sn au ag ss sp fu bu ru nr wr"},
        {"DCE", "a b c cs i j jm jd l m p pp v y eg eb pg lh bb fb rr"},
        {"CZCE", "MA TA SR CF CY FG RM OI ZC SM SF AP CJ UR SA PF PK RS RI WH JR LR PM"},
        {"CFFEX", "IF IH IC IM T TF TS TL"},
        {"INE", "sc lu bc nr"},
        {"GFEX", "si lc ps"},
    };
    return table;
}

std::string normalizeSymbol(const std::string& raw) {
    std::string symbol;
    for (char c : raw) {
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
        symbol.push_back(static_cast<char>(std::toupper(static_cast<unsigned char>(c))));
    }
    if (symbol.empty() || symbol.find('.') != std::string::npos) return symbol;

    std::string product;
    for (char c : symbol) {
        if (c >= 'A' && c <= 'Z') product.push_back(c);
    }
    for (const auto& pair : exchangeProducts()) {
        std::string list = pair.second;
        std::string token;
        for (std::size_t i = 0; i <= list.size(); ++i) {
            if (i == list.size() || list[i] == ' ') {
                if (token == product) return pair.first + "." + symbol;
                token.clear();
            } else {
                token.push_back(static_cast<char>(std::toupper(static_cast<unsigned char>(list[i]))));
            }
        }
    }
    return symbol;
}


std::string cmdLogin(const std::string& user, const std::string& password) {
    return jsonObject({
        {"cmd", Json::quote("login")},
        {"user", Json::quote(user)},
        {"password", Json::quote(password)},
    });
}

std::string cmdSymbol(const std::string& symbol) {
    return jsonObject({
        {"cmd", Json::quote("symbol")},
        {"symbol", Json::quote(symbol)},
    });
}

std::string cmdSubscribe(const std::vector<std::string>& symbols) {
    std::string arr = "[";
    for (std::size_t i = 0; i < symbols.size(); ++i) {
        if (i) arr.push_back(',');
        arr += Json::quote(symbols[i]);
    }
    arr.push_back(']');
    return jsonObject({
        {"cmd", Json::quote("subscribe")},
        {"symbols", arr},
    });
}

void readDepth(const Json& data, const char* key, DepthLevel* levels) {
    const Json* arr = data.find(key);
    if (!arr || !arr->isArray()) return;
    const auto& items = arr->items();
    for (std::size_t i = 0; i < items.size() && i < kDepthLevels; ++i) {
        const Json& level = items[i];
        if (level.isArray() && level.size() >= 2) {
            levels[i].price = level.items()[0].asDouble();
            levels[i].volume = level.items()[1].asInt();
        }
    }
}

Tick parseTickRow(const Json& row) {
    Tick tick;
    const auto& v = row.items();
    if (v.size() < 28) return tick;
    tick.ts = v[0].asInt();
    tick.last = v[1].asDouble();
    tick.bid1 = v[2].asDouble();
    tick.ask1 = v[3].asDouble();
    tick.totalVolume = v[4].asInt();
    tick.openInterest = v[5].asInt();
    tick.volumeDelta = v[6].asInt();
    tick.side = static_cast<int>(v[7].asInt());
    for (int i = 0; i < kDepthLevels; ++i) {
        tick.depth.asks[i].price = v[static_cast<std::size_t>(8 + i)].asDouble();
        tick.depth.asks[i].volume = v[static_cast<std::size_t>(13 + i)].asInt();
        tick.depth.bids[i].price = v[static_cast<std::size_t>(18 + i)].asDouble();
        tick.depth.bids[i].volume = v[static_cast<std::size_t>(23 + i)].asInt();
    }
    tick.hasDepth = true;
    return tick;
}

}  // namespace

App& App::get() {
    static App instance;
    return instance;
}

App::~App() = default;

bool App::init(HINSTANCE inst) {
    inst_ = inst;
    applog::init(exeDir() + L"\\logs");

    bool loaded = false;
    std::string error;
    if (configLoad(config_, &error)) {
        loaded = true;
    } else {
        configDefaults(config_);
    }
    if (config_.pages.size() != static_cast<std::size_t>(kPageCount)) {
        config_.pages.resize(kPageCount);
    }
    if (!loaded) configSave(config_, nullptr);

    sim_.reset(config_.initialBalance);
    sim_.setFeeFallback(config_.defaultCommission);
    session_.speed = speedValue();

    applog::write("Order Trader v1.0 启动");
    return true;
}

int App::run() {
    login_ = new LoginWindow();
    if (!login_->create(inst_)) {
        MessageBoxW(nullptr, L"无法创建登录窗口。", L"Order Trader", MB_ICONERROR);
        return 1;
    }
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }
    shutdown();
    return 0;
}

void App::shutdown() {
    saveConfig();
    bridge_.stop();
    replay_.stop();
    applog::write("Order Trader 退出");
}

HWND App::loginHwnd() const { return login_ ? login_->hwnd() : nullptr; }
HWND App::mainHwnd() const { return main_ ? main_->hwnd() : nullptr; }

std::wstring App::bridgeScript() const { return exeDir() + L"\\bridge\\tqsdk_bridge.py"; }

std::wstring App::findPython() const {
    if (!config_.pythonPath.empty()) {
        const std::wstring configured = utf8ToWide(config_.pythonPath);
        if (GetFileAttributesW(configured.c_str()) != INVALID_FILE_ATTRIBUTES) return configured;
    }
    const std::wstring bundled = exeDir() + L"\\python\\python.exe";
    if (GetFileAttributesW(bundled.c_str()) != INVALID_FILE_ATTRIBUTES) return bundled;

    wchar_t found[MAX_PATH * 2] = {0};
    if (SearchPathW(nullptr, L"python.exe", nullptr, static_cast<DWORD>(std::size(found)), found,
                    nullptr) > 0) {
        return found;
    }
    if (SearchPathW(nullptr, L"python3.exe", nullptr, static_cast<DWORD>(std::size(found)), found,
                    nullptr) > 0) {
        return found;
    }
    return std::wstring();
}

void App::pumpBridge() { bridge_.dispatch([this](const BridgeMessage& msg) { handleBridge(msg); }); }

void App::requestLogin(const std::string& user, const std::string& password) {
    if (pendingLogin_) return;

    const std::wstring script = bridgeScript();
    if (GetFileAttributesW(script.c_str()) == INVALID_FILE_ATTRIBUTES) {
        onLoginFinished(false, "未找到 bridge/tqsdk_bridge.py，请确认发布包完整。");
        return;
    }

    if (!bridge_.running()) {
        const std::wstring python = findPython();
        if (python.empty()) {
            onLoginFinished(false,
                            "未找到 Python 运行环境。\n"
                            "请安装 Python 3 并执行 pip install tqsdk，\n"
                            "或将便携版 Python 放到程序目录的 python\\ 下。");
            return;
        }
        bridge_.setNotifyWindow(loginHwnd());
        if (login_) login_->showProgress("正在启动行情桥接...");
        std::string error;
        if (!bridge_.start(python, script, exeDir(), &error)) {
            onLoginFinished(false, "启动行情桥接失败：" + error);
            return;
        }
        bridgeStartedAt_ = nowMs();
        applog::write("bridge started: " + wideToUtf8(python));
    }

    config_.user = user;
    if (config_.rememberPassword) config_.password = password;
    pendingUser_ = user;
    pendingLogin_ = true;
    loginSentAt_ = nowMs();
    loginStartedAt_ = loginSentAt_;
    bridge_.send(cmdLogin(user, password));
    setStatus("正在验证 tqsdk 账户...");
}

void App::onLoginFinished(bool ok, const std::string& message) {
    pendingLogin_ = false;
    if (!ok) {
        session_.loggedIn = false;
        setStatus("登录失败");
        applog::write("login failed: " + message);
        if (login_) login_->showError(message);
        return;
    }

    session_.loggedIn = true;
    session_.user = pendingUser_.empty() ? config_.user : pendingUser_;
    session_.loginAt = nowMs();
    session_.status = "登录成功";
    applog::write("login ok: " + session_.user);

    if (!main_) {
        main_ = new MainWindow();
        if (!main_->create(inst_)) {
            MessageBoxW(nullptr, L"无法创建主窗口。", L"Order Trader", MB_ICONERROR);
            PostQuitMessage(1);
            return;
        }
    }
    main_->show();
    if (login_) login_->closeSelf();

    // 合约元数据 + 订阅
    const auto symbols = allSymbols();
    for (const auto& sym : symbols) requestSymbolMeta(sym);
    if (session_.mode == RunMode::Live) {
        bridge_.send(cmdSubscribe(symbols));
    } else {
        startReplay();
    }
}

void App::handleBridge(const BridgeMessage& msg) {
    const Json& data = msg.data;
    if (msg.type == "ready") {
        session_.bridgeReady = true;
        bridgeStartedAt_ = 0;
        applog::write("bridge ready");
        if (pendingLogin_ && login_) login_->showProgress("行情桥接已就绪，正在登录 tqsdk...");
        return;
    }
    if (msg.type == "login_result") {
        const bool ok = data.find("ok") && data.find("ok")->asBool(false);
        std::string message = data.find("message") ? data.find("message")->str() : std::string();
        if (ok) {
            if (data.find("account")) {
                const std::string account = data.find("account")->str();
                if (!account.empty()) config_.user = account;
            }
            onLoginFinished(true, "ok");
        } else {
            onLoginFinished(false, message.empty() ? "账号或密码错误" : message);
        }
        return;
    }
    if (msg.type == "symbol_info") {
        SymbolMeta meta;
        meta.symbol = data.find("symbol") ? data.find("symbol")->str() : std::string();
        if (meta.symbol.empty()) return;
        meta.name = data.find("name") ? data.find("name")->str() : meta.symbol;
        meta.exchange = data.find("exchange") ? data.find("exchange")->str() : std::string();
        meta.product = data.find("product") ? data.find("product")->str() : std::string();
        meta.priceTick = data.find("price_tick") ? data.find("price_tick")->asDouble(1.0) : 1.0;
        meta.priceDecs = data.find("price_decs") ? static_cast<int>(data.find("price_decs")->asInt()) : 0;
        meta.volumeMultiple =
            data.find("volume_multiple") ? data.find("volume_multiple")->asDouble(1.0) : 1.0;
        meta.commission = data.find("commission") ? data.find("commission")->asDouble(0.0) : 0.0;
        meta.margin = data.find("margin") ? data.find("margin")->asDouble(0.0) : 0.0;
        meta.expired = data.find("expired") ? data.find("expired")->asBool(false) : false;
        meta.valid = true;
        if (meta.priceTick <= 0) meta.priceTick = 1.0;
        session_.metas[meta.symbol] = meta;
        sim_.setMeta(meta);
        return;
    }
    if (msg.type == "quote") {
        Quote quote;
        quote.symbol = data.find("symbol") ? data.find("symbol")->str() : std::string();
        if (quote.symbol.empty()) return;
        quote.ts = data.find("ts") ? data.find("ts")->asInt() : 0;
        quote.last = data.find("last") ? data.find("last")->asDouble() : 0.0;
        quote.preClose = data.find("pre_close") ? data.find("pre_close")->asDouble() : 0.0;
        quote.preSettle = data.find("pre_settle") ? data.find("pre_settle")->asDouble() : 0.0;
        quote.open = data.find("open") ? data.find("open")->asDouble() : 0.0;
        quote.high = data.find("high") ? data.find("high")->asDouble() : 0.0;
        quote.low = data.find("low") ? data.find("low")->asDouble() : 0.0;
        quote.upperLimit = data.find("upper") ? data.find("upper")->asDouble() : 0.0;
        quote.lowerLimit = data.find("lower") ? data.find("lower")->asDouble() : 0.0;
        quote.volume = data.find("volume") ? data.find("volume")->asInt() : 0;
        quote.openInterest = data.find("oi") ? data.find("oi")->asInt() : 0;
        readDepth(data, "bids", quote.depth.bids);
        readDepth(data, "asks", quote.depth.asks);
        quote.valid = true;
        onQuote(quote);
        return;
    }
    if (msg.type == "tick") {
        const Json* row = data.find("row");
        if (!row || !row->isArray()) return;
        Tick tick = parseTickRow(*row);
        tick.symbol = data.find("symbol") ? data.find("symbol")->str() : std::string();
        if (tick.symbol.empty() || tick.ts == 0) return;
        onTick(tick);
        return;
    }
    if (msg.type == "ticks") {
        const std::string symbol = data.find("symbol") ? data.find("symbol")->str() : std::string();
        const Json* rows = data.find("rows");
        std::vector<Tick> ticks;
        if (rows && rows->isArray()) {
            ticks.reserve(rows->size());
            for (const Json& row : rows->items()) {
                if (!row.isArray() || row.size() < 28) continue;
                Tick tick = parseTickRow(row);
                tick.symbol = symbol;
                if (tick.ts > 0) ticks.push_back(tick);
            }
        }
        onReplayTicks(symbol, std::move(ticks));
        return;
    }
    if (msg.type == "log" || msg.type == "textlog") {
        std::string text = data.find("message") ? data.find("message")->str() : data.str();
        if (!text.empty()) {
            applog::write("[bridge] " + text);
            setStatus(text);
            if (pendingLogin_) {
                // 桥接仍有输出，说明 tqsdk 正在连接/下载数据，不计入超时
                loginSentAt_ = nowMs();
                if (login_) login_->showProgress(text);
            }
        }
        return;
    }
    if (msg.type == "error") {
        const std::string text = data.find("message") ? data.find("message")->str() : "未知错误";
        setStatus("桥接错误：" + text);
        applog::write("bridge error: " + text);
        if (pendingLogin_) onLoginFinished(false, text);
        return;
    }
    if (msg.type == "bye") {
        session_.bridgeReady = false;
        setStatus("行情桥接进程已退出");
        if (pendingLogin_) onLoginFinished(false, "行情桥接进程异常退出，请检查 Python 与 tqsdk 安装。");
        return;
    }
}

void App::onQuote(const Quote& quote) {
    Quote& target = session_.quotes[quote.symbol];
    const bool wasValid = target.valid;
    const double previousLast = target.last;
    target = quote;
    (void)wasValid;
    (void)previousLast;
    session_.lastDataMs = nowMs();
    sim_.markPrice(quote.symbol, quote.last);
}

void App::onTick(const Tick& tick) {
    session_.lastTickTs[tick.symbol] = tick.ts;
    session_.lastDataMs = nowMs();

    const SymbolMeta& meta = metaOf(tick.symbol);
    Slice completed;
    if (slicer_.push(tick, meta, &completed)) {
        session_.slices[completed.symbol] = completed;
    }

    sim_.onTick(tick);

    Quote& quote = session_.quotes[tick.symbol];
    if (quote.symbol.empty()) quote.symbol = tick.symbol;
    if (tick.last > 0) quote.last = tick.last;
    if (tick.totalVolume > 0) quote.volume = tick.totalVolume;
    if (tick.openInterest > 0) quote.openInterest = tick.openInterest;
    if (tick.hasDepth) quote.depth = tick.depth;
    quote.ts = tick.ts;
    quote.valid = true;
}

void App::onReplayTicks(const std::string& symbol, std::vector<Tick> ticks) {
    if (symbol.empty()) return;
    session_.replayData[symbol] = std::move(ticks);
    session_.replayWaiting.erase(symbol);
    if (session_.replayLoading && session_.replayWaiting.empty()) finalizeReplayLoad();
}

void App::finalizeReplayLoad() {
    session_.replayLoading = false;
    replay_.clear();
    std::size_t symbols = 0;
    for (const auto& kv : session_.replayData) {
        if (kv.second.empty()) continue;
        replay_.addTicks(kv.first, kv.second);
        ++symbols;
    }
    if (replay_.total() == 0) {
        setStatus("回放失败：未获取到 tick 数据，请确认合约代码");
        return;
    }
    // 回放以空仓开始
    sim_.reset(config_.initialBalance);
    slicer_.reset();
    session_.slices.clear();
    replay_.start(speedValue(), nowMs());
    char buf[160];
    std::snprintf(buf, sizeof(buf), "回放开始：%zu 个合约 / %zu 条 tick / %.1fx", symbols,
                  replay_.total(), speedValue());
    setStatus(buf);
}

void App::setMode(RunMode mode) {
    if (session_.mode == mode) return;
    session_.mode = mode;
    if (mode == RunMode::Replay) {
        startReplay();
    } else {
        stopReplay();
        session_.replayData.clear();
        bridge_.send(cmdSubscribe(allSymbols()));
        setStatus("已切换到实时模拟盘");
    }
    invalidateAll();
}

void App::startReplay() {
    if (!session_.loggedIn) return;
    session_.mode = RunMode::Replay;
    replay_.stop();
    unsubscribeAll();
    session_.replayData.clear();
    session_.replayWaiting.clear();
    session_.replayLoading = true;
    session_.replayLoadStart = nowMs();

    const auto symbols = allSymbols();
    if (symbols.empty()) {
        session_.replayLoading = false;
        setStatus("当前没有合约，请先添加合约");
        return;
    }
    for (const auto& sym : symbols) {
        session_.replayWaiting.insert(sym);
        bridge_.send(jsonObject({
            {"cmd", Json::quote("ticks")},
            {"symbol", Json::quote(sym)},
            {"count", "10000"},
        }));
    }
    setStatus("正在获取最近一万条 tick 数据...");
    invalidateAll();
}

void App::stopReplay() {
    replay_.stop();
    session_.replayLoading = false;
    session_.replayWaiting.clear();
}

void App::unsubscribeAll() { bridge_.send(jsonObject({{"cmd", Json::quote("unsubscribe_all")}})); }

void App::subscribeSymbols(const std::vector<std::string>& symbols) {
    if (symbols.empty()) return;
    bridge_.send(cmdSubscribe(symbols));
}

void App::requestSymbolMeta(const std::string& symbol) { bridge_.send(cmdSymbol(symbol)); }

void App::setSpeedIndex(int index) {
    if (index < 0) index = 0;
    if (index > 4) index = 4;
    config_.replaySpeedIndex = index;
    session_.speed = speedValue();
    replay_.setSpeed(session_.speed);
    invalidateAll();
}

double App::speedValue() const {
    const int index = std::max(0, std::min(4, config_.replaySpeedIndex));
    return kSpeeds[index];
}

void App::setPage(int page) {
    if (page < 0 || page >= kPageCount) return;
    session_.page = page;
    if (main_) main_->onPageChanged();
    invalidateAll();
}

void App::addSymbol(const std::string& rawSymbol, bool toCurrentPage) {
    const std::string symbol = normalizeSymbol(rawSymbol);
    if (symbol.empty()) return;
    config_.lastSymbol = symbol;
    const int page = toCurrentPage ? session_.page : 0;
    auto& list = config_.pages[static_cast<std::size_t>(page)];
    if (std::find(list.begin(), list.end(), symbol) == list.end()) {
        list.push_back(symbol);
        saveConfig();
    }
    requestSymbolMeta(symbol);
    if (session_.mode == RunMode::Live && session_.loggedIn) {
        subscribeSymbols({symbol});
    }
    if (main_) {
        main_->onPageChanged();
    }
    setStatus("已添加合约 " + symbol);
    invalidateAll();
}

void App::removeSymbol(const std::string& symbol) {
    for (auto& page : config_.pages) {
        page.erase(std::remove(page.begin(), page.end(), symbol), page.end());
    }
    if (main_) main_->onPageChanged();
    saveConfig();
    invalidateAll();
}

void App::removeSymbolFromPage(const std::string& symbol, int page) {
    if (page < 0 || page >= static_cast<int>(config_.pages.size())) return;
    auto& list = config_.pages[static_cast<std::size_t>(page)];
    list.erase(std::remove(list.begin(), list.end(), symbol), list.end());
    if (main_) main_->onPageChanged();
    saveConfig();
    invalidateAll();
}

bool App::hasSymbol(const std::string& symbol) const {
    for (const auto& page : config_.pages) {
        if (std::find(page.begin(), page.end(), symbol) != page.end()) return true;
    }
    return false;
}

std::vector<std::string> App::currentSymbols() const {
    if (session_.page < 0 || session_.page >= static_cast<int>(config_.pages.size())) return {};
    return config_.pages[static_cast<std::size_t>(session_.page)];
}

std::vector<std::string> App::allSymbols() const {
    std::vector<std::string> out;
    for (const auto& page : config_.pages) {
        for (const auto& sym : page) {
            if (std::find(out.begin(), out.end(), sym) == out.end()) out.push_back(sym);
        }
    }
    return out;
}

void App::placeOrder(const std::string& symbol, bool buy, double price, long long qty) {
    if (qty <= 0 || price <= 0) return;
    sim_.placeOrder(symbol, buy, price, qty, nowMs());
    char buf[128];
    std::snprintf(buf, sizeof(buf), "%s %s %lld 手 @ %.4g", symbol.c_str(), buy ? "买入" : "卖出",
                  qty, price);
    setStatus(buf);
    invalidateAll();
}

void App::cancelAll() {
    const int count = sim_.cancelAll();
    char buf[96];
    std::snprintf(buf, sizeof(buf), "已撤销 %d 笔挂单", count);
    setStatus(buf);
    invalidateAll();
}

void App::resetAccount() {
    sim_.reset(config_.initialBalance);
    setStatus("虚拟账户已重置");
    invalidateAll();
}

void App::requestRecenter() {
    if (main_) main_->recenterLadders();
}

void App::setLadderSize(int width, int height) {
    config_.ladderWidth = std::max(260, width);
    config_.ladderHeight = std::max(220, height);
    if (main_) main_->applyLadderSize();
    invalidateAll();
}

void App::saveConfig() { configSave(config_, nullptr); }

void App::setStatus(const std::string& text) {
    session_.status = text;
    if (main_) main_->refresh();
}

void App::invalidateAll() {
    if (main_) main_->refresh();
}

void App::update() {
    const int64_t now = nowMs();
    // 桥接进程启动后 30 秒仍未握手，说明 Python 本身没能跑起来
    if (bridgeStartedAt_ > 0 && !session_.bridgeReady && now - bridgeStartedAt_ > 30000) {
        bridgeStartedAt_ = 0;
        if (pendingLogin_) {
            onLoginFinished(false,
                            "行情桥接启动超时：Python 未能正常运行，请查看 logs 目录日志");
        }
        return;
    }
    // 连接进度日志会不断刷新 loginSentAt_；这里再加 240 秒硬上限，防止心跳把超时无限推迟
    if (pendingLogin_ && (now - loginSentAt_ > 150000 || now - loginStartedAt_ > 240000)) {
        onLoginFinished(false, "登录超时：tqsdk 长时间无响应，请检查网络后重试（详见 logs 目录日志）");
        return;
    }
    if (session_.replayLoading && now - session_.replayLoadStart > 45000) {
        finalizeReplayLoad();
    }
    if (replay_.active()) {
        std::vector<Tick> ticks = replay_.pump(now);
        for (const auto& tick : ticks) onTick(tick);
        if (!replay_.active()) setStatus("回放结束");
    }
    if (now - lastEquitySample_ >= 1000) {
        lastEquitySample_ = now;
        sim_.sampleEquity(now);
    }
}

const SymbolMeta& App::metaOf(const std::string& symbol) const {
    static const SymbolMeta kDefault;
    const auto it = session_.metas.find(symbol);
    return it == session_.metas.end() ? kDefault : it->second;
}

const Quote* App::quoteOf(const std::string& symbol) const {
    const auto it = session_.quotes.find(symbol);
    return it == session_.quotes.end() ? nullptr : &it->second;
}

const Slice* App::sliceOf(const std::string& symbol) const {
    const auto it = session_.slices.find(symbol);
    return it == session_.slices.end() ? nullptr : &it->second;
}

}  // namespace ot
