#include "PriceLadder.h"

#include <windowsx.h>

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <ctime>
#include <string>

#include "app/App.h"
#include "ui/Gdi.h"
#include "ui/Theme.h"
#include "util/Config.h"
#include "util/Symbol.h"

namespace ot {

namespace {

const wchar_t* kClass = L"OT_PriceLadder";
constexpr UINT kRemoveMsg = WM_APP + 41;

int sc(int v) { return Th::S(v); }

void computeColumns(int width, int stripW, int colX[6]) {
    const double ratios[5] = {0.10, 0.22, 0.28, 0.22, 0.18};
    const int inner = width - stripW;
    int acc = stripW;
    colX[0] = stripW;
    for (int i = 0; i < 5; ++i) {
        acc += static_cast<int>(inner * ratios[i]);
        colX[i + 1] = acc;
    }
    colX[5] = width;
}

std::wstring timeText(int64_t ms) {
    if (ms <= 0) return L"--:--:--";
    const std::time_t seconds = static_cast<std::time_t>(ms / 1000);
    std::tm local{};
    localtime_s(&local, &seconds);
    wchar_t buf[16];
    swprintf(buf, 16, L"%02d:%02d:%02d", local.tm_hour, local.tm_min, local.tm_sec);
    return buf;
}

int findLevel(const DepthLevel* levels, long long key, double tick) {
    for (int i = 0; i < kDepthLevels; ++i) {
        if (levels[i].price <= 0) continue;
        const double k = levels[i].price / tick;
        const long long lk = static_cast<long long>(k >= 0 ? k + 0.5 : k - 0.5);
        if (lk == key) return i;
    }
    return -1;
}

int editTop() { return sc(50) + sc(20) + sc(8); }

}  // namespace

void PriceLadder::registerClass(HINSTANCE inst) {
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW | CS_DBLCLKS;
    wc.lpfnWndProc = &PriceLadder::WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);
}

PriceLadder::PriceLadder(HWND owner, const std::string& symbol, int page)
    : owner_(owner), symbol_(symbol), page_(page) {
    create();
}

PriceLadder::~PriceLadder() {
    destroyChildren();
    if (hwnd_) {
        SetWindowLongPtrW(hwnd_, GWLP_USERDATA, 0);
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
}

void PriceLadder::create() {
    const AppConfig& cfg = App::get().config();
    const int width = cfg.ladderWidth > 0 ? cfg.ladderWidth : sc(420);
    const int height = cfg.ladderHeight > 0 ? cfg.ladderHeight : sc(820);

    RECT work{};
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    const int x = work.left + sc(80);
    const int y = work.top + sc(80);

    const std::wstring title = utf8ToWide(displaySymbol(symbol_));
    hwnd_ = CreateWindowExW(WS_EX_APPWINDOW, kClass, title.c_str(),
                            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN, x, y, width, height, nullptr,
                            nullptr, GetModuleHandleW(nullptr), this);
    applyDarkTitleBar(hwnd_);
}

void PriceLadder::destroyChildren() {
    if (editLeft_) {
        DestroyWindow(editLeft_);
        editLeft_ = nullptr;
    }
    if (editRight_) {
        DestroyWindow(editRight_);
        editRight_ = nullptr;
    }
    if (editFont_) {
        DeleteObject(editFont_);
        editFont_ = nullptr;
    }
    if (editBrush_) {
        DeleteObject(editBrush_);
        editBrush_ = nullptr;
    }
}

void PriceLadder::setVisible(bool visible) {
    if (!hwnd_) return;
    ShowWindow(hwnd_, visible ? SW_SHOWNA : SW_HIDE);
}

void PriceLadder::activate() {
    if (!hwnd_) return;
    ShowWindow(hwnd_, SW_SHOWNORMAL);
    SetForegroundWindow(hwnd_);
}

bool PriceLadder::isVisible() const {
    return hwnd_ && IsWindowVisible(hwnd_) != FALSE;
}

void PriceLadder::moveTo(int x, int y, int w, int h) {
    if (!hwnd_) return;
    SetWindowPos(hwnd_, nullptr, x, y, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    layoutEdits();
}

void PriceLadder::resizeTo(int w, int h) {
    if (!hwnd_) return;
    RECT rc;
    GetWindowRect(hwnd_, &rc);
    SetWindowPos(hwnd_, nullptr, rc.left, rc.top, w, h, SWP_NOZORDER | SWP_NOACTIVATE);
    layoutEdits();
}

void PriceLadder::refresh() {
    if (hwnd_) InvalidateRect(hwnd_, nullptr, FALSE);
}

void PriceLadder::requestRemove() {
    if (!owner_) return;
    std::string* payload = new std::string(symbol_ + "\n" + std::to_string(page_));
    ShowWindow(hwnd_, SW_HIDE);
    PostMessageW(owner_, kRemoveMsg, 0, reinterpret_cast<LPARAM>(payload));
}

void PriceLadder::resetCenter() {
    manualScroll_ = false;
    following_ = true;
    syncAnchor();
    refresh();
}

void PriceLadder::syncAnchor() {
    const Quote* quote = App::get().quoteOf(symbol_);
    if (!quote || quote->last <= 0) return;
    const SymbolMeta& meta = App::get().metaOf(symbol_);
    lastKey_ = meta.priceKey(quote->last);
    if (!manualScroll_) anchorKey_ = lastKey_;
}

long long PriceLadder::anchorKey() const {
    const Quote* quote = App::get().quoteOf(symbol_);
    const SymbolMeta& meta = App::get().metaOf(symbol_);
    if (quote && quote->last > 0 && (anchorKey_ == 0 || !manualScroll_)) {
        return meta.priceKey(quote->last);
    }
    return anchorKey_;
}

long long PriceLadder::keyAtRow(int row, int rows) const {
    return anchorKey() + static_cast<long long>(rows / 2 - row);
}

double PriceLadder::tickSize() const {
    const SymbolMeta& meta = App::get().metaOf(symbol_);
    return meta.priceTick > 0 ? meta.priceTick : 1.0;
}

int PriceLadder::priceDecs() const {
    const SymbolMeta& meta = App::get().metaOf(symbol_);
    if (meta.valid) return meta.priceDecs;
    const double tick = tickSize();
    if (tick >= 1.0) return 0;
    return static_cast<int>(std::ceil(-std::log10(tick) - 1e-9));
}

double PriceLadder::priceOfKey(long long key) const { return static_cast<double>(key) * tickSize(); }

int PriceLadder::rowAtY(int y, int bodyTop, int rowH, int rows, long long* key) const {
    if (y < bodyTop || rowH <= 0) return -1;
    const int row = (y - bodyTop) / rowH;
    if (row < 0 || row >= rows) return -1;
    if (key) *key = keyAtRow(row, rows);
    return row;
}

int PriceLadder::leftQty() const {
    wchar_t buf[16] = {0};
    if (editLeft_) GetWindowTextW(editLeft_, buf, 16);
    const int value = _wtoi(buf);
    return value > 0 ? value : 1;
}

int PriceLadder::rightQty() const {
    wchar_t buf[16] = {0};
    if (editRight_) GetWindowTextW(editRight_, buf, 16);
    const int value = _wtoi(buf);
    return value > 0 ? value : 2;
}

void PriceLadder::scrollBy(int rows) {
    manualScroll_ = true;
    following_ = false;
    if (anchorKey_ == 0) {
        const Quote* quote = App::get().quoteOf(symbol_);
        if (quote) anchorKey_ = App::get().metaOf(symbol_).priceKey(quote->last);
    }
    anchorKey_ += rows;
    refresh();
}

void PriceLadder::handleClick(int x, int y, bool rightButton) {
    RECT rc;
    GetClientRect(hwnd_, &rc);
    const int headerH = sc(50);
    const int colHeaderH = sc(20);
    const int footerH = sc(22);
    const int stripW = sc(70);
    if (y < headerH) return;

    const int bodyTop = headerH + colHeaderH;
    const int bodyH = (rc.bottom - rc.top) - bodyTop - footerH;
    const int rowH = sc(18);
    const int rows = rowCount(bodyH, rowH);

    long long key = 0;
    if (rowAtY(y, bodyTop, rowH, rows, &key) < 0) return;
    if (key <= 0) return;

    int colX[6];
    computeColumns(rc.right - rc.left, stripW, colX);
    int column = -1;
    for (int i = 0; i < 5; ++i) {
        if (x >= colX[i] && x < colX[i + 1]) {
            column = i;
            break;
        }
    }
    if (column != 1 && column != 3) return;

    const double price = priceOfKey(key);
    const long long qty = rightButton ? rightQty() : leftQty();
    App::get().placeOrder(symbol_, column == 1, price, qty);
}

void PriceLadder::layoutEdits() {
    if (!hwnd_ || !editLeft_ || !editRight_) return;
    const int stripW = sc(70);
    const int y0 = editTop();
    SetWindowPos(editLeft_, nullptr, sc(8), y0 + sc(15), stripW - sc(16), sc(20), SWP_NOZORDER);
    SetWindowPos(editRight_, nullptr, sc(8), y0 + sc(57), stripW - sc(16), sc(20), SWP_NOZORDER);
}

void PriceLadder::beginResize(bool horizontal) {
    resizing_ = true;
    resizeWidth_ = horizontal;
    GetCursorPos(&resizeOrigin_);
    GetWindowRect(hwnd_, &startRect_);
    startWidth_ = startRect_.right - startRect_.left;
    startHeight_ = startRect_.bottom - startRect_.top;
    SetCapture(hwnd_);
}

void PriceLadder::updateResize() {
    if (!resizing_) return;
    POINT p;
    GetCursorPos(&p);
    const int dx = p.x - resizeOrigin_.x;
    const int dy = p.y - resizeOrigin_.y;
    // 横向拖动只改宽度、纵向拖动只改高度，另一维保持当前值，避免把默认尺寸固化成最小值
    const int newW = resizeWidth_ ? startWidth_ + dx : startWidth_;
    const int newH = resizeWidth_ ? startHeight_ : startHeight_ + dy;
    App::get().setLadderSize(newW, newH);
}

void PriceLadder::endResize() {
    if (!resizing_) return;
    resizing_ = false;
    ReleaseCapture();
    App::get().saveConfig();
}

void PriceLadder::paint(HDC dc) {
    RECT rc;
    GetClientRect(hwnd_, &rc);
    const int width = rc.right;
    const int height = rc.bottom;
    fillSolid(dc, rc, Th::panel);

    const int headerH = sc(50);
    const int colHeaderH = sc(20);
    const int footerH = sc(22);
    const int stripW = sc(70);
    const int rowH = sc(18);

    const int bodyTop = headerH + colHeaderH;
    const int bodyH = height - bodyTop - footerH;
    const int rows = rowCount(bodyH, rowH);

    syncAnchor();

    int colX[6];
    computeColumns(width, stripW, colX);

    paintHeader(dc, rc, headerH, stripW);

    // 列标题
    RECT colHeader{0, headerH, width, headerH + colHeaderH};
    fillSolid(dc, colHeader, Th::header);
    const wchar_t* titles[5] = {L"挂单", L"买量", L"价格", L"卖量", L"成交"};
    for (int i = 0; i < 5; ++i) {
        RECT cell{colX[i] + 2, headerH, colX[i + 1] - 2, headerH + colHeaderH};
        drawTextIn(dc, titles[i], cell, Th::textDim, Th::fontSmall,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
    // 左侧信息条标题
    RECT stripHeader{0, headerH, stripW, headerH + colHeaderH};
    fillSolid(dc, stripHeader, Th::header);
    drawTextIn(dc, L"数量 / 盈亏", RECT{sc(4), headerH, stripW - sc(2), headerH + colHeaderH},
               Th::textFaint, Th::fontSmall, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    paintRows(dc, rc, bodyTop, rowH, rows, stripW, colX);
    paintFooter(dc, rc, bodyTop + rows * rowH);

    // 左侧信息条背景（在价格行之后绘制，避免被行底色覆盖）
    RECT strip{0, headerH, stripW, height - footerH};
    fillSolid(dc, strip, Th::panelAlt);
    drawVLine(dc, stripW - 1, headerH, height - footerH, Th::border);

    // 左侧：左键/右键数量标签 + 持仓 + 净盈亏
    {
        const int y0 = editTop();
        RECT l1{sc(4), y0, stripW - sc(4), y0 + sc(14)};
        drawTextIn(dc, L"左键手数", l1, Th::textFaint, Th::fontSmall,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        RECT l2{sc(4), y0 + sc(42), stripW - sc(4), y0 + sc(56)};
        drawTextIn(dc, L"右键手数", l2, Th::textFaint, Th::fontSmall,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE);

        const long long pos = App::get().sim().positionQty(symbol_);
        const double pnl = App::get().sim().netPnl(symbol_);
        const int y1 = y0 + sc(86);
        RECT pl{sc(4), y1, stripW - sc(4), y1 + sc(16)};
        drawTextIn(dc, L"持仓", pl, Th::textFaint, Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        RECT pv{sc(4), y1 + sc(16), stripW - sc(4), y1 + sc(38)};
        drawTextIn(dc, formatInt(pos), pv, pos > 0 ? Th::up : (pos < 0 ? Th::down : Th::textDim),
                   Th::fontMonoBold, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

        const int y2 = y1 + sc(48);
        RECT nl{sc(4), y2, stripW - sc(4), y2 + sc(16)};
        drawTextIn(dc, L"净盈亏", nl, Th::textFaint, Th::fontSmall,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE);
        RECT nv{sc(4), y2 + sc(16), stripW - sc(4), y2 + sc(38)};
        drawTextIn(dc, formatMoneyShort(pnl), nv, pnl >= 0 ? Th::up : Th::down, Th::fontMono,
                   DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
    }

    // 边框
    drawHLine(dc, 0, width, 0, Th::border);
    drawHLine(dc, 0, width, height - 1, Th::border);
    drawVLine(dc, width - 1, 0, height, Th::border);
}

void PriceLadder::paintHeader(HDC dc, const RECT& client, int headerH, int stripW) {
    RECT header{0, 0, client.right, headerH};
    fillSolid(dc, header, Th::header);

    const SymbolMeta& meta = App::get().metaOf(symbol_);
    const Quote* quote = App::get().quoteOf(symbol_);

    std::wstring title = utf8ToWide(displaySymbol(symbol_));
    RECT tr{sc(8), sc(4), client.right - sc(76), sc(28)};
    drawTextIn(dc, title, tr, Th::text, Th::fontMonoBold, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    const std::wstring name = meta.valid ? utf8ToWide(meta.name) : L"";
    RECT nr{sc(8), sc(26), client.right - sc(76), sc(46)};
    drawTextIn(dc, name.empty() ? L"未订阅" : name, nr, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

    double changePct = 0.0;
    bool hasChange = false;
    if (quote && quote->last > 0) {
        const double base = quote->preClose > 0 ? quote->preClose : quote->preSettle;
        if (base > 0) {
            changePct = (quote->last - base) / base * 100.0;
            hasChange = true;
        }
    }
    wchar_t pctBuf[32] = {0};
    if (hasChange) swprintf(pctBuf, 32, L"%+.2f%%", changePct);
    RECT pr{client.right - sc(84), sc(4), client.right - sc(6), sc(28)};
    drawTextIn(dc, hasChange ? pctBuf : L"--", pr,
               hasChange ? (changePct >= 0 ? Th::up : Th::down) : Th::textFaint, Th::fontSmallBold,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);

    RECT timeR{client.right - sc(84), sc(26), client.right - sc(6), sc(46)};
    drawTextIn(dc, quote ? timeText(quote->ts) : L"--:--:--", timeR, Th::textDim, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);

    drawHLine(dc, 0, client.right, headerH - 1, Th::border);
    drawVLine(dc, stripW - 1, 0, headerH, Th::border);
}

void PriceLadder::paintRows(HDC dc, const RECT& client, int top, int rowH, int rows, int stripW,
                            const int* colX) {
    const SymbolMeta& meta = App::get().metaOf(symbol_);
    const Quote* quote = App::get().quoteOf(symbol_);
    const Slice* slice = App::get().sliceOf(symbol_);
    const double tick = tickSize();
    const int decs = priceDecs();
    const auto working = App::get().sim().workingByPrice(symbol_);

    const long long lastKey = (quote && quote->last > 0) ? meta.priceKey(quote->last) : 0;

    for (int row = 0; row < rows; ++row) {
        const int y = top + row * rowH;
        const long long key = keyAtRow(row, rows);
        if (key <= 0) continue;
        RECT rowRect{0, y, client.right, y + rowH};

        // 交替底色
        fillSolid(dc, rowRect, (row % 2 == 0) ? Th::panel : Th::panelAlt);

        const int askLevel = quote ? findLevel(quote->depth.asks, key, tick) : -1;
        const int bidLevel = quote ? findLevel(quote->depth.bids, key, tick) : -1;

        // 价格列
        RECT priceRect{colX[2], y, colX[3], y + rowH};
        COLORREF priceBg = Th::neutralCell;
        COLORREF priceFg = Th::textDim;
        if (askLevel >= 0) {
            priceBg = blendColor(Th::askDeep, Th::ask, 1.0 - askLevel * 0.16);
            priceFg = Th::askText;
        } else if (bidLevel >= 0) {
            priceBg = blendColor(Th::bidDeep, Th::bid, 1.0 - bidLevel * 0.16);
            priceFg = Th::bidText;
        }
        fillSolid(dc, priceRect, priceBg);
        drawTextIn(dc, formatPrice(priceOfKey(key), decs),
                   RECT{priceRect.left, y, priceRect.right, y + rowH}, priceFg, Th::fontMonoBold,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);

        if (key == lastKey) {
            drawHLine(dc, 0, client.right, y, Th::up);
            drawHLine(dc, 0, client.right, y + rowH - 1, Th::up);
            RECT marker{colX[2], y + 2, colX[2] + sc(3), y + rowH - 2};
            fillSolid(dc, marker, Th::up);
        }

        // 买量列
        if (bidLevel >= 0 && quote) {
            RECT cell{colX[1], y, colX[2], y + rowH};
            fillSolid(dc, cell, blendColor(Th::bidDeep, Th::bid, 1.0 - bidLevel * 0.16));
            drawTextIn(dc, formatInt(quote->depth.bids[bidLevel].volume),
                       RECT{cell.left + sc(2), y, cell.right - sc(5), y + rowH}, RGB(232, 240, 250),
                       Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        }

        // 卖量列
        if (askLevel >= 0 && quote) {
            RECT cell{colX[3], y, colX[4], y + rowH};
            fillSolid(dc, cell, blendColor(Th::askDeep, Th::ask, 1.0 - askLevel * 0.16));
            drawTextIn(dc, formatInt(quote->depth.asks[askLevel].volume),
                       RECT{cell.left + sc(5), y, cell.right - sc(2), y + rowH}, RGB(252, 238, 235),
                       Th::fontMono, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        }

        // 挂单列
        const auto wit = working.find(key);
        if (wit != working.end() && wit->second != 0) {
            RECT cell{colX[0], y, colX[1], y + rowH};
            fillSolid(dc, cell, blendColor(Th::panelAlt, wit->second > 0 ? Th::bidDeep : Th::askDeep, 0.75));
            drawTextIn(dc, formatInt(wit->second > 0 ? wit->second : -wit->second),
                       RECT{cell.left + sc(2), y, cell.right - sc(4), y + rowH},
                       wit->second > 0 ? Th::orderBuy : Th::orderSell, Th::fontMonoBold,
                       DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        }

        // 成交量列（500ms 切片足迹）
        if (slice) {
            const Footprint* cell = slice->cellAt(key);
            if (cell && cell->total() > 0) {
                RECT area{colX[4] + sc(2), y + sc(3), colX[5] - sc(2), y + rowH - sc(3)};
                const int total = static_cast<int>(cell->total());
                int buyW = 0;
                if (total > 0) {
                    buyW = static_cast<int>((area.right - area.left) * cell->buy / total);
                }
                RECT buyRect{area.left, area.top, area.left + buyW, area.bottom};
                RECT sellRect{area.left + buyW, area.top, area.right, area.bottom};
                if (cell->buy > 0) fillSolid(dc, buyRect, RGB(32, 84, 148));
                if (cell->sell > 0) fillSolid(dc, sellRect, RGB(140, 44, 44));
                drawTextIn(dc, formatInt(cell->total()),
                           RECT{area.left + sc(1), y, area.right - sc(2), y + rowH}, RGB(226, 232, 240),
                           Th::fontSmall, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
            }
        }

        // 分隔线
        drawHLine(dc, stripW, client.right, y + rowH - 1, Th::gridLine);
    }

    if (!quote || !quote->valid) {
        RECT hint{colX[2], top + sc(10), colX[3], top + rowH * 4};
        drawTextIn(dc, L"等待行情...", hint, Th::textFaint, Th::fontSmall,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    }
}

void PriceLadder::paintFooter(HDC dc, const RECT& client, int top) {
    RECT footer{0, top, client.right, client.bottom};
    fillSolid(dc, footer, Th::header);
    drawHLine(dc, 0, client.right, top, Th::border);

    const Quote* quote = App::get().quoteOf(symbol_);
    std::wstring text = L"成交量 ";
    text += quote ? formatInt(quote->volume) : L"-";
    text += L"   持仓 ";
    text += quote ? formatInt(quote->openInterest) : L"-";
    text += L"   现价 ";
    text += quote ? formatPrice(quote->last, priceDecs()) : L"-";

    drawTextIn(dc, text, RECT{sc(8), top, client.right - sc(8), client.bottom}, Th::textDim,
               Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
}

LRESULT CALLBACK PriceLadder::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_NCCREATE) {
        CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        PriceLadder* self = static_cast<PriceLadder*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
    }
    PriceLadder* self = reinterpret_cast<PriceLadder*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!self) return DefWindowProcW(hwnd, msg, wp, lp);
    return self->handleMessage(msg, wp, lp);
}

LRESULT PriceLadder::handleMessage(UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_CREATE: {
            editFont_ = CreateFontW(Th::F(9.0), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
                                    DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS,
                                    CLEARTYPE_QUALITY, FIXED_PITCH | FF_MODERN, L"Consolas");
            editBrush_ = CreateSolidBrush(RGB(23, 24, 27));
            editLeft_ = CreateWindowExW(0, L"EDIT", L"1",
                                        WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_NUMBER | ES_CENTER,
                                        0, 0, 10, 10, hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
            editRight_ = CreateWindowExW(0, L"EDIT", L"2",
                                         WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_NUMBER | ES_CENTER,
                                         0, 0, 10, 10, hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
            for (HWND edit : {editLeft_, editRight_}) {
                SendMessageW(edit, WM_SETFONT, reinterpret_cast<WPARAM>(editFont_), TRUE);
                SendMessageW(edit, EM_SETLIMITTEXT, 5, 0);
            }
            layoutEdits();
            return 0;
        }
        case WM_SIZE:
            layoutEdits();
            return 0;
        case WM_PAINT: {
            BufferedPaint bp(hwnd_);
            paint(bp.dc());
            return 0;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_CTLCOLOREDIT: {
            HDC dc = reinterpret_cast<HDC>(wp);
            SetTextColor(dc, Th::text);
            SetBkColor(dc, RGB(23, 24, 27));
            return reinterpret_cast<LRESULT>(editBrush_);
        }
        case WM_LBUTTONDOWN: {
            const int x = GET_X_LPARAM(lp);
            const int y = GET_Y_LPARAM(lp);
            RECT rc;
            GetClientRect(hwnd_, &rc);
            if (x >= rc.right - sc(6)) {
                beginResize(true);
                return 0;
            }
            if (y >= rc.bottom - sc(6)) {
                beginResize(false);
                return 0;
            }
            SetFocus(hwnd_);
            handleClick(x, y, false);
            return 0;
        }
        case WM_RBUTTONUP: {
            const int y = GET_Y_LPARAM(lp);
            RECT rc;
            GetClientRect(hwnd_, &rc);
            if (y < sc(50)) {
                HMENU menu = CreatePopupMenu();
                AppendMenuW(menu, MF_STRING, 1, L"移除该合约");
                POINT pt;
                GetCursorPos(&pt);
                const int cmd = static_cast<int>(
                    TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, pt.x, pt.y, 0, hwnd_, nullptr));
                DestroyMenu(menu);
                if (cmd == 1) requestRemove();
                return 0;
            }
            SetFocus(hwnd_);
            handleClick(GET_X_LPARAM(lp), y, true);
            return 0;
        }
        case WM_MOUSEMOVE:
            if (resizing_) {
                updateResize();
                return 0;
            }
            return 0;
        case WM_LBUTTONUP:
            if (resizing_) {
                endResize();
                return 0;
            }
            return 0;
        case WM_CAPTURECHANGED:
            resizing_ = false;
            return 0;
        case WM_SETCURSOR: {
            POINT p;
            GetCursorPos(&p);
            ScreenToClient(hwnd_, &p);
            RECT rc;
            GetClientRect(hwnd_, &rc);
            if (p.x >= rc.right - sc(6)) {
                SetCursor(LoadCursorW(nullptr, IDC_SIZEWE));
                return TRUE;
            }
            if (p.y >= rc.bottom - sc(6)) {
                SetCursor(LoadCursorW(nullptr, IDC_SIZENS));
                return TRUE;
            }
            return DefWindowProcW(hwnd_, msg, wp, lp);
        }
        case WM_MOUSEWHEEL: {
            const int delta = GET_WHEEL_DELTA_WPARAM(wp);
            scrollBy(delta > 0 ? 3 : -3);
            return 0;
        }
        case WM_KEYDOWN:
            if (owner_) PostMessageW(owner_, WM_KEYDOWN, wp, lp);
            return 0;
        case WM_CLOSE:
            // 关窗只隐藏，合约仍留在当前页面列表中，可从主界面重新打开
            ShowWindow(hwnd_, SW_HIDE);
            App::get().notifyLadderClosed(symbol_);
            return 0;
        case WM_NCDESTROY: {
            HWND closing = hwnd_;
            destroyChildren();
            hwnd_ = nullptr;
            LRESULT result = DefWindowProcW(closing, msg, wp, lp);
            return result;
        }
        default:
            break;
    }
    return DefWindowProcW(hwnd_, msg, wp, lp);
}

}  // namespace ot
