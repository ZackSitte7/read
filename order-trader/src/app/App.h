// Order Trader v1.0 - 应用状态与编排
#pragma once

#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>

#include "bridge/BridgeClient.h"
#include "core/MarketData.h"
#include "core/SimEngine.h"
#include "core/Types.h"
#include "util/Config.h"

namespace ot {

class LoginWindow;
class MainWindow;

struct Session {
    std::string user;
    bool loggedIn = false;
    bool bridgeReady = false;
    RunMode mode = RunMode::Live;
    double speed = 1.0;
    int page = 0;

    std::map<std::string, SymbolMeta> metas;
    std::map<std::string, Quote> quotes;
    std::map<std::string, Slice> slices;
    std::map<std::string, int64_t> lastTickTs;

    std::map<std::string, std::vector<Tick>> replayData;
    std::set<std::string> replayWaiting;
    bool replayLoading = false;
    int64_t replayLoadStart = 0;

    std::string status = "就绪";
    std::string error;
    int64_t lastDataMs = 0;
    int64_t loginAt = 0;
};

class App {
public:
    static App& get();
    ~App();

    bool init(HINSTANCE inst);
    int run();
    void shutdown();

    void pumpBridge();
    void requestLogin(const std::string& user, const std::string& password);
    void onLoginFinished(bool ok, const std::string& message);

    void setPage(int page);
    void addSymbol(const std::string& symbol, bool toCurrentPage);
    void removeSymbol(const std::string& symbol);
    void removeSymbolFromPage(const std::string& symbol, int page);
    bool hasSymbol(const std::string& symbol) const;
    std::vector<std::string> currentSymbols() const;
    std::vector<std::string> allSymbols() const;

    void setMode(RunMode mode);
    double speedValue() const;
    void setSpeedIndex(int index);
    void startReplay();
    void stopReplay();

    void placeOrder(const std::string& symbol, bool buy, double price, long long qty);
    void cancelAll();
    void resetAccount();
    void requestRecenter();
    void setLadderSize(int width, int height);
    void saveConfig();
    void setStatus(const std::string& text);
    void invalidateAll();

    void update();  // 定时器驱动

    Session& session() { return session_; }
    SimEngine& sim() { return sim_; }
    AppConfig& config() { return config_; }
    BridgeClient& bridge() { return bridge_; }
    Slicer& slicer() { return slicer_; }
    ReplayEngine& replay() { return replay_; }

    HWND loginHwnd() const;
    HWND mainHwnd() const;
    HINSTANCE instance() const { return inst_; }
    void attachLogin(LoginWindow* window) { login_ = window; }
    void attachMain(MainWindow* window) { main_ = window; }
    void detachLogin() { login_ = nullptr; }
    void detachMain() { main_ = nullptr; }

    void onQuote(const Quote& quote);
    void onTick(const Tick& tick);
    void onReplayTicks(const std::string& symbol, std::vector<Tick> ticks);

    std::wstring bridgeScript() const;
    std::wstring findPython() const;

    const SymbolMeta& metaOf(const std::string& symbol) const;
    const Quote* quoteOf(const std::string& symbol) const;
    const Slice* sliceOf(const std::string& symbol) const;

private:
    App() = default;
    void handleBridge(const BridgeMessage& msg);
    void subscribeSymbols(const std::vector<std::string>& symbols);
    void unsubscribeAll();
    void finalizeReplayLoad();
    void requestSymbolMeta(const std::string& symbol);

    AppConfig config_;
    Session session_;
    SimEngine sim_;
    Slicer slicer_;
    ReplayEngine replay_;
    BridgeClient bridge_;
    HINSTANCE inst_ = nullptr;
    LoginWindow* login_ = nullptr;
    MainWindow* main_ = nullptr;
    bool pendingLogin_ = false;
    std::string pendingUser_;
    int64_t loginSentAt_ = 0;
    int64_t loginStartedAt_ = 0;
    int64_t lastEquitySample_ = 0;
    int64_t lastReplay_ = 0;
};

}  // namespace ot
