#include "Config.h"

#include <cstdio>
#include <string>

#include "Json.h"
#include "Win.h"

namespace ot {

void configDefaults(AppConfig& cfg) {
    cfg.pages.assign(kConfigPageCount, std::vector<std::string>());
    cfg.pages[0].push_back("SHFE.rb2701");
}

std::wstring configPath() {
    return exeDir() + L"\\config\\order-trader.json";
}

namespace {
std::wstring fallbackPath() {
    wchar_t buf[512] = {0};
    const DWORD len = GetEnvironmentVariableW(L"APPDATA", buf, static_cast<DWORD>(std::size(buf)));
    if (len == 0 || len >= std::size(buf)) return L"order-trader.json";
    return std::wstring(buf) + L"\\OrderTrader\\order-trader.json";
}

bool readFile(const std::wstring& path, std::string* out) {
    FILE* f = _wfopen(path.c_str(), L"rb");
    if (!f) return false;
    std::string data;
    char buf[8192];
    std::size_t n = 0;
    while ((n = std::fread(buf, 1, sizeof(buf), f)) > 0) data.append(buf, n);
    std::fclose(f);
    *out = std::move(data);
    return true;
}
}  // namespace

bool configLoad(AppConfig& cfg, std::string* error) {
    std::string text;
    std::wstring path = configPath();
    if (!readFile(path, &text)) {
        path = fallbackPath();
        if (!readFile(path, &text)) {
            if (error) *error = "config not found";
            return false;
        }
    }
    std::string parseError;
    const Json root = Json::parse(text, &parseError);
    if (!root.isObject()) {
        if (error) *error = "config parse failed: " + parseError;
        return false;
    }
    cfg.user = root.find("user") ? root.find("user")->str(cfg.user) : cfg.user;
    cfg.password = root.find("password") ? root.find("password")->str(cfg.password) : cfg.password;
    cfg.pythonPath = root.find("pythonPath") ? root.find("pythonPath")->str() : cfg.pythonPath;
    if (const Json* v = root.find("initialBalance")) cfg.initialBalance = v->asDouble(cfg.initialBalance);
    if (const Json* v = root.find("defaultCommission")) cfg.defaultCommission = v->asDouble(cfg.defaultCommission);
    if (const Json* v = root.find("ladderWidth")) cfg.ladderWidth = static_cast<int>(v->asInt(cfg.ladderWidth));
    if (const Json* v = root.find("ladderHeight")) cfg.ladderHeight = static_cast<int>(v->asInt(cfg.ladderHeight));
    if (const Json* v = root.find("replaySpeedIndex")) cfg.replaySpeedIndex = static_cast<int>(v->asInt(cfg.replaySpeedIndex));
    if (const Json* v = root.find("lastSymbol")) cfg.lastSymbol = v->str(cfg.lastSymbol);
    if (const Json* v = root.find("rememberPassword")) cfg.rememberPassword = v->asBool(true);

    if (const Json* pages = root.find("pages")) {
        if (pages->isArray()) {
            cfg.pages.assign(kConfigPageCount, std::vector<std::string>());
            const int count = static_cast<int>(pages->size());
            for (int i = 0; i < count && i < kConfigPageCount; ++i) {
                const Json& page = pages->items()[static_cast<std::size_t>(i)];
                if (!page.isArray()) continue;
                for (const Json& item : page.items()) {
                    const std::string sym = item.str();
                    if (!sym.empty()) cfg.pages[static_cast<std::size_t>(i)].push_back(sym);
                }
            }
        }
    }
    if (cfg.pages.size() != static_cast<std::size_t>(kConfigPageCount)) {
        cfg.pages.resize(kConfigPageCount);
    }
    return true;
}

bool configSave(const AppConfig& cfg, std::string* error) {
    std::string out = "{\n";
    out += "  \"version\": \"1.0\",\n";
    out += "  \"user\": " + Json::quote(cfg.user) + ",\n";
    out += "  \"password\": " + Json::quote(cfg.rememberPassword ? cfg.password : std::string()) + ",\n";
    out += "  \"pythonPath\": " + Json::quote(cfg.pythonPath) + ",\n";
    out += "  \"initialBalance\": " + Json::num(cfg.initialBalance) + ",\n";
    out += "  \"defaultCommission\": " + Json::num(cfg.defaultCommission) + ",\n";
    out += "  \"ladderWidth\": " + Json::num(cfg.ladderWidth) + ",\n";
    out += "  \"ladderHeight\": " + Json::num(cfg.ladderHeight) + ",\n";
    out += "  \"replaySpeedIndex\": " + Json::num(cfg.replaySpeedIndex) + ",\n";
    out += "  \"lastSymbol\": " + Json::quote(cfg.lastSymbol) + ",\n";
    out += "  \"rememberPassword\": " + std::string(cfg.rememberPassword ? "true" : "false") + ",\n";
    out += "  \"pages\": [\n";
    for (std::size_t i = 0; i < cfg.pages.size(); ++i) {
        out += "    [";
        for (std::size_t j = 0; j < cfg.pages[i].size(); ++j) {
            if (j) out += ", ";
            out += Json::quote(cfg.pages[i][j]);
        }
        out += "]";
        if (i + 1 < cfg.pages.size()) out += ",";
        out += "\n";
    }
    out += "  ]\n}\n";

    std::wstring path = configPath();
    CreateDirectoryW((exeDir() + L"\\config").c_str(), nullptr);
    FILE* f = _wfopen(path.c_str(), L"wb");
    if (!f) {
        path = fallbackPath();
        const std::size_t slash = path.find_last_of(L'\\');
        if (slash != std::wstring::npos) CreateDirectoryW(path.substr(0, slash).c_str(), nullptr);
        f = _wfopen(path.c_str(), L"wb");
    }
    if (!f) {
        if (error) *error = "config write failed";
        return false;
    }
    std::fwrite(out.data(), 1, out.size(), f);
    std::fclose(f);
    return true;
}

}  // namespace ot
