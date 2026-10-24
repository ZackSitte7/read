// Order Trader v1.0 - 配置读写
#pragma once

#include <string>
#include <vector>

namespace ot {

constexpr int kConfigPageCount = 20;

struct AppConfig {
    std::string user = "alyzc26";
    std::string password = "130100";
    std::string pythonPath;              // 为空则自动探测
    double initialBalance = 10000000.0;  // 虚拟盘初始资金
    double defaultCommission = 1.5;      // 接口未返回手续费时的兜底（元/手）
    int ladderWidth = 0;                 // 0 = 按 DPI 取默认宽度
    int ladderHeight = 0;                // 0 = 跟随页面高度
    int replaySpeedIndex = 1;            // 0:0.5x 1:1x 2:2x 3:5x 4:10x
    std::vector<std::vector<std::string>> pages;  // 20 页
    std::string lastSymbol = "SHFE.rb2701";
    bool rememberPassword = true;
};

void configDefaults(AppConfig& cfg);
bool configLoad(AppConfig& cfg, std::string* error);
bool configSave(const AppConfig& cfg, std::string* error);
std::wstring configPath();

}  // namespace ot
