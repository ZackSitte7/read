#include "Theme.h"

namespace ot {

int Th::dpi = 96;
HFONT Th::fontUi = nullptr;
HFONT Th::fontUiBold = nullptr;
HFONT Th::fontMono = nullptr;
HFONT Th::fontMonoBold = nullptr;
HFONT Th::fontSmall = nullptr;
HFONT Th::fontSmallBold = nullptr;
HFONT Th::fontTitle = nullptr;
HFONT Th::fontBig = nullptr;

int Th::S(int v) { return MulDiv(v, dpi, 96); }

int Th::F(double pt) { return -static_cast<int>(pt * dpi / 72.0 + 0.5); }

namespace {

HFONT makeFont(const wchar_t* face, int pixelHeight, int weight) {
    LOGFONTW lf;
    ZeroMemory(&lf, sizeof(lf));
    lf.lfHeight = pixelHeight;
    lf.lfWeight = weight;
    lf.lfCharSet = DEFAULT_CHARSET;
    lf.lfQuality = CLEARTYPE_QUALITY;
    lf.lfPitchAndFamily = FIXED_PITCH | FF_MODERN;
    lstrcpynW(lf.lfFaceName, face, LF_FACESIZE);
    HFONT font = CreateFontIndirectW(&lf);
    return font ? font : static_cast<HFONT>(GetStockObject(DEFAULT_GUI_FONT));
}

}  // namespace

void Th::init() {
    HDC dc = GetDC(nullptr);
    dpi = GetDeviceCaps(dc, LOGPIXELSY);
    ReleaseDC(nullptr, dc);
    if (dpi <= 0) dpi = 96;

    // 中文优先使用微软雅黑，缺失时回退到系统默认
    fontUi = makeFont(L"Microsoft YaHei UI", F(9.5), FW_NORMAL);
    fontUiBold = makeFont(L"Microsoft YaHei UI", F(9.5), FW_BOLD);
    fontMono = makeFont(L"Consolas", F(9.5), FW_NORMAL);
    fontMonoBold = makeFont(L"Consolas", F(9.5), FW_BOLD);
    fontSmall = makeFont(L"Microsoft YaHei UI", F(8.0), FW_NORMAL);
    fontSmallBold = makeFont(L"Microsoft YaHei UI", F(8.0), FW_BOLD);
    fontTitle = makeFont(L"Microsoft YaHei UI", F(12.0), FW_BOLD);
    fontBig = makeFont(L"Consolas", F(15.0), FW_BOLD);
}

}  // namespace ot
