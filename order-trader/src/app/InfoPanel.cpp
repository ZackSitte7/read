#include "InfoPanel.h"

#include <cmath>
#include <cstdio>
#include <ctime>
#include <string>
#include <vector>

#include "app/App.h"
#include "ui/Gdi.h"
#include "ui/Theme.h"

namespace ot {

namespace {

const wchar_t* kClass = L"OT_InfoPanel";
int sc(int v) { return Th::S(v); }

std::wstring clockText(int64_t ms) {
    if (ms <= 0) return L"--:--:--";
    const std::time_t seconds = static_cast<std::time_t>(ms / 1000);
    std::tm local{};
    localtime_s(&local, &seconds);
    wchar_t buf[16];
    swprintf(buf, 16, L"%02d:%02d:%02d", local.tm_hour, local.tm_min, local.tm_sec);
    return buf;
}

int textWidth(HDC dc, HFONT font, const std::wstring& text) {
    HGDIOBJ old = SelectObject(dc, font);
    SIZE size{};
    GetTextExtentPoint32W(dc, text.c_str(), static_cast<int>(text.size()), &size);
    SelectObject(dc, old);
    return size.cx;
}

}  // namespace

void InfoPanel::registerClass(HINSTANCE inst) {
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &InfoPanel::WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);
}

InfoPanel::InfoPanel(HWND parent) : parent_(parent) {
    hwnd_ = CreateWindowExW(0, kClass, L"", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS, 0, 0, sc(380),
                            sc(700), parent, nullptr, GetModuleHandleW(nullptr), this);
}

InfoPanel::~InfoPanel() {
    if (hwnd_) {
        SetWindowLongPtrW(hwnd_, GWLP_USERDATA, 0);
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
}

void InfoPanel::moveTo(int x, int y, int w, int h) {
    if (hwnd_) SetWindowPos(hwnd_, nullptr, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
}

void InfoPanel::refresh() {
    if (hwnd_) InvalidateRect(hwnd_, nullptr, FALSE);
}

void InfoPanel::paint(HDC dc) {
    RECT rc;
    GetClientRect(hwnd_, &rc);
    fillSolid(dc, rc, Th::panel);
    drawHLine(dc, 0, rc.right, 0, Th::border);
    drawVLine(dc, 0, 0, rc.bottom, Th::border);

    const SimEngine& sim = App::get().sim();
    const Session& session = App::get().session();

    int y = sc(6);
    RECT title{sc(10), y, rc.right - sc(10), y + sc(22)};
    drawTextIn(dc, L"账户概览", title, Th::text, Th::fontUiBold, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    y += sc(26);

    const double balance = sim.balance();
    const double equity = sim.equity();
    const double dayPnl = equity - sim.initialBalance();
    const double fees = sim.fees();

    auto stat = [&](int x, int width, const wchar_t* label, const std::wstring& value,
                    COLORREF color) {
        RECT lr{x, y, x + width, y + sc(14)};
        drawTextIn(dc, label, lr, Th::textFaint, Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        RECT vr{x, y + sc(14), x + width, y + sc(36)};
        drawTextIn(dc, value, vr, color, Th::fontMonoBold,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    };
    const int half = (rc.right - sc(20)) / 2;
    stat(sc(10), half, L"账户余额", formatMoney(balance), Th::text);
    stat(sc(10) + half, half, L"权益", formatMoney(equity), Th::text);
    y += sc(42);
    stat(sc(10), half, L"当日盈亏", formatMoneyShort(dayPnl), dayPnl >= 0 ? Th::up : Th::down);
    stat(sc(10) + half, half, L"手续费", formatMoney(fees), Th::warn);
    y += sc(44);

    y = paintEquity(dc, y, rc.right);
    y = paintPositions(dc, y, rc.right, rc.bottom - sc(4));
    paintFills(dc, y, rc.right, rc.bottom - sc(4));

    // 底部状态
    std::wstring footer = formatMoneyShort(sim.realized());
    footer += L" 已实现 · " + formatInt(static_cast<long long>(sim.fills().size())) + L" 笔成交";
    RECT fr{sc(10), rc.bottom - sc(22), rc.right - sc(10), rc.bottom - sc(4)};
    drawTextIn(dc, footer, fr, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    (void)session;
}

int InfoPanel::paintEquity(HDC dc, int top, int width) {
    RECT title{sc(10), top, width - sc(10), top + sc(20)};
    drawTextIn(dc, L"收益曲线", title, Th::textDim, Th::fontSmallBold,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    top += sc(22);

    RECT chart{sc(10), top, width - sc(10), top + sc(118)};
    fillSolid(dc, chart, RGB(25, 26, 30));
    drawHLine(dc, chart.left, chart.right, chart.top, Th::border);
    drawHLine(dc, chart.left, chart.right, chart.bottom - 1, Th::border);
    drawVLine(dc, chart.left, chart.top, chart.bottom, Th::border);
    drawVLine(dc, chart.right - 1, chart.top, chart.bottom, Th::border);

    const SimEngine& sim = App::get().sim();
    const auto& curve = sim.equityCurve();
    const double initial = sim.initialBalance();
    if (curve.size() < 2) {
        drawTextIn(dc, L"暂无收益数据", chart, Th::textFaint, Th::fontSmall,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        return top + sc(126);
    }

    double minV = initial;
    double maxV = initial;
    for (const auto& sample : curve) {
        if (sample.equity < minV) minV = sample.equity;
        if (sample.equity > maxV) maxV = sample.equity;
    }
    if (maxV - minV < 1.0) {
        minV -= 1.0;
        maxV += 1.0;
    }
    const double pad = (maxV - minV) * 0.08;
    minV -= pad;
    maxV += pad;

    // 网格
    for (int i = 1; i < 4; ++i) {
        const int gy = chart.top + (chart.bottom - chart.top) * i / 4;
        drawHLine(dc, chart.left + 1, chart.right - 1, gy, Th::gridLine);
    }
    const int baseline = chart.bottom - 1 - static_cast<int>(
        (initial - minV) / (maxV - minV) * (chart.bottom - chart.top - 2));
    for (int x = chart.left + 1; x < chart.right - 1; x += 6) {
        SetPixel(dc, x, baseline, RGB(90, 95, 105));
        SetPixel(dc, x + 1, baseline, RGB(90, 95, 105));
    }

    std::vector<POINT> points;
    points.reserve(curve.size());
    const int spanX = chart.right - chart.left - 3;
    const int spanY = chart.bottom - chart.top - 3;
    const double count = static_cast<double>(curve.size() - 1);
    for (std::size_t i = 0; i < curve.size(); ++i) {
        POINT p{};
        p.x = chart.left + 1 + static_cast<int>(spanX * (static_cast<double>(i) / count));
        p.y = chart.top + 1 + static_cast<int>(spanY * (1.0 - (curve[i].equity - minV) / (maxV - minV)));
        points.push_back(p);
    }
    const bool gain = curve.back().equity >= initial;
    HPEN pen = CreatePen(PS_SOLID, Th::S(2) > 1 ? 2 : 1, gain ? Th::up : Th::down);
    HGDIOBJ oldPen = SelectObject(dc, pen);
    Polyline(dc, points.data(), static_cast<int>(points.size()));
    SelectObject(dc, oldPen);
    DeleteObject(pen);

    wchar_t hi[48];
    swprintf(hi, 48, L"%.2f", maxV);
    drawTextIn(dc, hi, RECT{chart.left + sc(4), chart.top + sc(2), chart.right - sc(4), chart.top + sc(18)},
               Th::textFaint, Th::fontSmall, DT_RIGHT | DT_TOP | DT_SINGLELINE);
    wchar_t lo[48];
    swprintf(lo, 48, L"%.2f", minV);
    drawTextIn(dc, lo,
               RECT{chart.left + sc(4), chart.bottom - sc(18), chart.right - sc(4), chart.bottom - sc(2)},
               Th::textFaint, Th::fontSmall, DT_RIGHT | DT_BOTTOM | DT_SINGLELINE);

    return top + sc(126);
}

int InfoPanel::paintPositions(HDC dc, int top, int width, int maxBottom) {
    RECT title{sc(10), top, width - sc(10), top + sc(20)};
    drawTextIn(dc, L"持仓", title, Th::textDim, Th::fontSmallBold, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    top += sc(22);

    const int rowH = sc(19);
    const int colSymbol = sc(10);
    const int colQty = sc(110);
    const int colAvg = sc(170);
    const int colLast = sc(240);
    const int colPnl = width - sc(10);

    RECT head{sc(10), top, width - sc(10), top + rowH};
    fillSolid(dc, head, Th::header);
    drawTextIn(dc, L"合约", RECT{colSymbol, top, colQty, top + rowH}, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"持仓", RECT{colQty, top, colAvg, top + rowH}, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"均价", RECT{colAvg, top, colLast, top + rowH}, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"现价", RECT{colLast, top, colPnl, top + rowH}, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"净盈亏", RECT{colPnl - sc(80), top, colPnl, top + rowH}, Th::textFaint,
               Th::fontSmall, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    top += rowH;

    const SimEngine& sim = App::get().sim();
    int shown = 0;
    for (const auto& kv : sim.positions()) {
        if (kv.second.qty == 0) continue;
        if (top + rowH > maxBottom) break;
        const int decs = App::get().metaOf(kv.first).valid ? App::get().metaOf(kv.first).priceDecs : 0;
        const double pnl = sim.netPnl(kv.first);
        RECT r{colSymbol, top, width - sc(10), top + rowH};
        fillSolid(dc, r, (shown % 2 == 0) ? Th::panel : Th::panelAlt);
        drawTextIn(dc, utf8ToWide(kv.first), RECT{colSymbol, top, colQty, top + rowH},
                   kv.second.qty >= 0 ? Th::bidText : Th::askText, Th::fontSmall,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        drawTextIn(dc, formatInt(kv.second.qty), RECT{colQty, top, colAvg, top + rowH},
                   kv.second.qty >= 0 ? Th::up : Th::down, Th::fontMono,
                   DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        drawTextIn(dc, formatPrice(kv.second.avg, decs), RECT{colAvg, top, colLast, top + rowH},
                   Th::textDim, Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        drawTextIn(dc, formatPrice(kv.second.last, decs), RECT{colLast, top, colPnl - sc(80), top + rowH},
                   Th::textDim, Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        drawTextIn(dc, formatMoneyShort(pnl), RECT{colPnl - sc(78), top, colPnl, top + rowH},
                   pnl >= 0 ? Th::up : Th::down, Th::fontMono,
                   DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        top += rowH;
        ++shown;
    }
    if (shown == 0) {
        drawTextIn(dc, L"当前无持仓", RECT{sc(10), top, width - sc(10), top + rowH}, Th::textFaint,
                   Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        top += rowH;
    }
    return top + sc(10);
}

int InfoPanel::paintFills(HDC dc, int top, int width, int maxBottom) {
    RECT title{sc(10), top, width - sc(10), top + sc(20)};
    drawTextIn(dc, L"逐笔盈亏", title, Th::textDim, Th::fontSmallBold,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    top += sc(22);

    const int rowH = sc(19);
    RECT head{sc(10), top, width - sc(10), top + rowH};
    fillSolid(dc, head, Th::header);
    drawTextIn(dc, L"时间", RECT{sc(10), top, sc(68), top + rowH}, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"合约", RECT{sc(68), top, sc(160), top + rowH}, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"买/卖", RECT{sc(160), top, sc(204), top + rowH}, Th::textFaint, Th::fontSmall,
               DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"手数", RECT{sc(204), top, sc(240), top + rowH}, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"价格", RECT{sc(240), top, width - sc(88), top + rowH}, Th::textFaint,
               Th::fontSmall, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    drawTextIn(dc, L"平仓盈亏", RECT{width - sc(88), top, width - sc(10), top + rowH}, Th::textFaint,
               Th::fontSmall, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    top += rowH;

    const SimEngine& sim = App::get().sim();
    const auto& fills = sim.fills();
    int shown = 0;
    for (int i = static_cast<int>(fills.size()) - 1; i >= 0; --i) {
        if (top + rowH > maxBottom) break;
        const SimFill& fill = fills[static_cast<std::size_t>(i)];
        RECT r{sc(10), top, width - sc(10), top + rowH};
        fillSolid(dc, r, (shown % 2 == 0) ? Th::panel : Th::panelAlt);
        drawTextIn(dc, clockText(fill.ts), RECT{sc(10), top, sc(68), top + rowH}, Th::textFaint,
                   Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        drawTextIn(dc, utf8ToWide(fill.symbol), RECT{sc(68), top, sc(160), top + rowH}, Th::textDim,
                   Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        drawTextIn(dc, fill.buy ? L"买" : L"卖", RECT{sc(160), top, sc(204), top + rowH},
                   fill.buy ? Th::bidText : Th::askText, Th::fontSmall,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        drawTextIn(dc, formatInt(fill.qty), RECT{sc(204), top, sc(240), top + rowH}, Th::textDim,
                   Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        const int decs = App::get().metaOf(fill.symbol).valid ? App::get().metaOf(fill.symbol).priceDecs : 0;
        drawTextIn(dc, formatPrice(fill.price, decs), RECT{sc(240), top, width - sc(88), top + rowH},
                   Th::textDim, Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        if (fill.closeQty > 0) {
            drawTextIn(dc, formatMoneyShort(fill.pnl),
                       RECT{width - sc(88), top, width - sc(10), top + rowH},
                       fill.pnl >= 0 ? Th::up : Th::down, Th::fontMono,
                       DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        }
        top += rowH;
        ++shown;
    }
    if (shown == 0) {
        drawTextIn(dc, L"暂无成交", RECT{sc(10), top, width - sc(10), top + rowH}, Th::textFaint,
                   Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    }
    return top;
}

LRESULT CALLBACK InfoPanel::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_NCCREATE) {
        CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        InfoPanel* self = static_cast<InfoPanel*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
    }
    InfoPanel* self = reinterpret_cast<InfoPanel*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!self) return DefWindowProcW(hwnd, msg, wp, lp);
    return self->handleMessage(msg, wp, lp);
}

LRESULT InfoPanel::handleMessage(UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_PAINT: {
            BufferedPaint bp(hwnd_);
            paint(bp.dc());
            return 0;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_NCDESTROY: {
            HWND closing = hwnd_;
            hwnd_ = nullptr;
            return DefWindowProcW(closing, msg, wp, lp);
        }
        default:
            break;
    }
    return DefWindowProcW(hwnd_, msg, wp, lp);
}

}  // namespace ot
