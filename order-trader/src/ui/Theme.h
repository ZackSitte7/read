// Order Trader v1.0 - 主题（配色/字体/DPI）
#pragma once

#include "util/Win.h"

namespace ot {

struct Th {
    static int dpi;
    static int S(int v);
    static int F(double pt);

    static void init();

    static HFONT fontUi;
    static HFONT fontUiBold;
    static HFONT fontMono;
    static HFONT fontMonoBold;
    static HFONT fontSmall;
    static HFONT fontSmallBold;
    static HFONT fontTitle;
    static HFONT fontBig;

    static constexpr COLORREF bg = RGB(22, 23, 25);
    static constexpr COLORREF panel = RGB(31, 32, 36);
    static constexpr COLORREF panelAlt = RGB(37, 38, 43);
    static constexpr COLORREF header = RGB(44, 46, 51);
    static constexpr COLORREF border = RGB(60, 63, 69);
    static constexpr COLORREF gridLine = RGB(48, 50, 55);
    static constexpr COLORREF text = RGB(219, 221, 225);
    static constexpr COLORREF textDim = RGB(138, 143, 152);
    static constexpr COLORREF textFaint = RGB(96, 100, 108);

    static constexpr COLORREF bid = RGB(38, 92, 160);
    static constexpr COLORREF bidDeep = RGB(24, 58, 104);
    static constexpr COLORREF bidText = RGB(148, 196, 246);
    static constexpr COLORREF ask = RGB(158, 52, 52);
    static constexpr COLORREF askDeep = RGB(100, 33, 33);
    static constexpr COLORREF askText = RGB(248, 168, 158);
    static constexpr COLORREF neutralCell = RGB(45, 47, 52);

    static constexpr COLORREF up = RGB(64, 200, 132);
    static constexpr COLORREF down = RGB(238, 110, 100);
    static constexpr COLORREF accent = RGB(90, 160, 245);
    static constexpr COLORREF warn = RGB(240, 190, 80);
    static constexpr COLORREF orderBuy = RGB(96, 168, 255);
    static constexpr COLORREF orderSell = RGB(255, 130, 118);
};

}  // namespace ot
