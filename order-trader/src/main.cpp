// Order Trader v1.0 - 程序入口
#include <windows.h>

#include "app/App.h"
#include "app/InfoPanel.h"
#include "ui/PriceLadder.h"
#include "ui/Theme.h"

int WINAPI WinMain(HINSTANCE inst, HINSTANCE, LPSTR, int) {
    SetProcessDPIAware();
    ot::Th::init();
    ot::PriceLadder::registerClass(inst);
    ot::InfoPanel::registerClass(inst);

    ot::App& app = ot::App::get();
    if (!app.init(inst)) {
        MessageBoxW(nullptr, L"Order Trader 初始化失败。", L"Order Trader", MB_ICONERROR);
        return 1;
    }
    return app.run();
}
