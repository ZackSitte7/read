// Order Trader v1.0 - 右侧账户/持仓/逐笔盈亏面板
#pragma once

#include "util/Win.h"

namespace ot {

class InfoPanel {
public:
    static void registerClass(HINSTANCE inst);

    explicit InfoPanel(HWND parent);
    ~InfoPanel();

    HWND hwnd() const { return hwnd_; }
    void moveTo(int x, int y, int w, int h);
    void refresh();

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);
    void paint(HDC dc);
    int paintEquity(HDC dc, int top, int width);
    int paintPositions(HDC dc, int top, int width, int maxBottom);
    int paintFills(HDC dc, int top, int width, int maxBottom);

    HWND hwnd_ = nullptr;
    HWND parent_ = nullptr;
};

}  // namespace ot
