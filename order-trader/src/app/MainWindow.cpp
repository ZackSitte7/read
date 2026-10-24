#include "MainWindow.h"

#include <windowsx.h>

#include <algorithm>
#include <cstdio>
#include <cstdlib>
#include <iterator>
#include <string>
#include <vector>

#include "App.h"
#include "InfoPanel.h"
#include "core/Types.h"
#include "ui/Gdi.h"
#include "ui/PriceLadder.h"
#include "ui/Theme.h"
#include "util/Config.h"
#include "util/Log.h"
#include "util/Win.h"

namespace ot {

namespace {

const wchar_t* kClass = L"OT_MainWindow";
constexpr UINT kBridgeMsg = WM_APP + 10;
constexpr UINT kAddSymbolMsg = WM_APP + 32;
constexpr UINT kRemoveMsg = WM_APP + 41;

constexpr int kHitLive = 1;
constexpr int kHitReplay = 2;
constexpr int kHitSpeedBase = 10;
constexpr int kHitAdd = 20;
constexpr int kHitCancel = 21;
constexpr int kHitReset = 22;
constexpr int kHitTabBase = 100;

int sc(int v) { return Th::S(v); }

int textWidth(HDC dc, HFONT font, const std::wstring& text) {
    HGDIOBJ old = SelectObject(dc, font);
    SIZE size{};
    GetTextExtentPoint32W(dc, text.c_str(), static_cast<int>(text.size()), &size);
    SelectObject(dc, old);
    return size.cx;
}

std::wstring clockText(int64_t ms) {
    if (ms <= 0) return L"--:--:--";
    const std::time_t seconds = static_cast<std::time_t>(ms / 1000);
    std::tm local{};
    localtime_s(&local, &seconds);
    wchar_t buf[16];
    swprintf(buf, 16, L"%02d:%02d:%02d", local.tm_hour, local.tm_min, local.tm_sec);
    return buf;
}

/// 带千分位的金额文本
std::wstring moneyText(double value, bool withSign = false) {
    wchar_t raw[64];
    swprintf(raw, 64, L"%.2f", value);
    std::wstring s(raw);
    bool negative = !s.empty() && s[0] == L'-';
    std::wstring body = negative ? s.substr(1) : s;
    const std::size_t dot = body.find(L'.');
    std::wstring integerPart = dot == std::wstring::npos ? body : body.substr(0, dot);
    const std::wstring fraction = dot == std::wstring::npos ? std::wstring() : body.substr(dot);
    for (int i = static_cast<int>(integerPart.size()) - 3; i > 0; i -= 3) {
        integerPart.insert(static_cast<std::size_t>(i), L",");
    }
    std::wstring out;
    if (negative) {
        out = L"-";
    } else if (withSign) {
        out = L"+";
    }
    out += integerPart + fraction;
    return out;
}

std::string trim(const std::string& text) {
    std::size_t begin = 0;
    std::size_t end = text.size();
    while (begin < end && (text[begin] == ' ' || text[begin] == '\t' || text[begin] == '\r')) ++begin;
    while (end > begin && (text[end - 1] == ' ' || text[end - 1] == '\t' || text[end - 1] == '\r')) --end;
    return text.substr(begin, end - begin);
}

WNDPROC g_editProc = nullptr;

LRESULT CALLBACK SymbolEditProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_KEYDOWN && wp == VK_RETURN) {
        PostMessageW(GetParent(hwnd), kAddSymbolMsg, 0, 0);
        return 0;
    }
    if (msg == WM_CHAR && (wp == VK_RETURN || wp == VK_ESCAPE)) return 0;
    return CallWindowProcW(g_editProc, hwnd, msg, wp, lp);
}

}  // namespace

MainWindow::MainWindow() = default;
MainWindow::~MainWindow() = default;

bool MainWindow::create(HINSTANCE inst) {
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &MainWindow::WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);

    RECT work{};
    SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
    const int workW = work.right - work.left;
    const int workH = work.bottom - work.top;
    const int width = std::min(Th::S(1600), workW);
    const int height = std::min(Th::S(960), workH);
    const int x = work.left + (workW - width) / 2;
    const int y = work.top + (workH - height) / 2;

    hwnd_ = CreateWindowExW(0, kClass, L"Order Trader v1.0 — 订单流虚拟盘",
                            WS_OVERLAPPEDWINDOW | WS_CLIPCHILDREN | WS_HSCROLL, x, y, width, height,
                            nullptr, nullptr, inst, this);
    if (!hwnd_) return false;

    // 深色标题栏（Win10 1809+），失败时忽略
    if (HMODULE dwm = LoadLibraryW(L"dwmapi.dll")) {
        using SetAttrFn = HRESULT(WINAPI*)(HWND, DWORD, LPCVOID, DWORD);
        auto setAttr = reinterpret_cast<SetAttrFn>(
            reinterpret_cast<void*>(GetProcAddress(dwm, "DwmSetWindowAttribute")));
        if (setAttr) {
            BOOL dark = TRUE;
            setAttr(hwnd_, 20, &dark, sizeof(dark));
        }
    }
    return true;
}

void MainWindow::show() {
    ShowWindow(hwnd_, SW_SHOWMAXIMIZED);
    UpdateWindow(hwnd_);
    SetForegroundWindow(hwnd_);
    SetFocus(hwnd_);
}

void MainWindow::refresh() {
    if (!hwnd_) return;
    InvalidateRect(hwnd_, nullptr, FALSE);
    if (info_) info_->refresh();
}

void MainWindow::buildControls() {
    info_ = new InfoPanel(hwnd_);

    editBrush_ = CreateSolidBrush(RGB(23, 24, 27));
    fontEdit_ = CreateFontW(Th::F(10.0), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                            FIXED_PITCH | FF_MODERN, L"Consolas");
    editSymbol_ = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | ES_AUTOHSCROLL | ES_UPPERCASE,
                                  0, 0, 10, 10, hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
    SendMessageW(editSymbol_, WM_SETFONT, reinterpret_cast<WPARAM>(fontEdit_), TRUE);
    SendMessageW(editSymbol_, EM_SETLIMITTEXT, 32, 0);
    g_editProc = reinterpret_cast<WNDPROC>(
        SetWindowLongPtrW(editSymbol_, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&SymbolEditProc)));

    const std::wstring last = utf8ToWide(App::get().config().lastSymbol);
    SetWindowTextW(editSymbol_, last.c_str());

    ShowScrollBar(hwnd_, SB_HORZ, FALSE);
}

void MainWindow::layout() {
    if (!hwnd_ || inLayout_) return;
    inLayout_ = true;

    RECT rc;
    GetClientRect(hwnd_, &rc);
    const int width = rc.right;
    const int height = rc.bottom;

    topBar_ = RECT{0, 0, width, sc(34)};
    toolbar_ = RECT{0, topBar_.bottom, width, topBar_.bottom + sc(44)};
    tabs_ = RECT{0, toolbar_.bottom, width, toolbar_.bottom + sc(30)};
    status_ = RECT{0, height - sc(24), width, height};

    const int infoW = std::max(sc(280), std::min(sc(380), width / 4));
    content_ = RECT{0, tabs_.bottom, width - infoW, status_.top};
    if (info_) info_->moveTo(width - infoW, tabs_.bottom, infoW, status_.top - tabs_.bottom);

    const int by = toolbar_.top + (sc(44) - sc(26)) / 2;
    int x = sc(12);
    btnLive_ = RECT{x, by, x + sc(100), by + sc(26)};
    x += sc(104);
    btnReplay_ = RECT{x, by, x + sc(100), by + sc(26)};
    x += sc(116);
    x += sc(32);  // “倍速”标签
    for (int i = 0; i < 5; ++i) {
        speed_[i] = RECT{x, by, x + sc(40), by + sc(26)};
        x += sc(44);
    }
    x += sc(12);
    x += sc(30);  // “合约”标签
    editRect_ = RECT{x, by + sc(1), x + sc(150), by + sc(25)};
    x += sc(156);
    btnAdd_ = RECT{x, by, x + sc(56), by + sc(26)};
    x += sc(64);
    btnCancel_ = RECT{x, by, x + sc(84), by + sc(26)};
    x += sc(92);
    btnReset_ = RECT{x, by, x + sc(84), by + sc(26)};

    if (editSymbol_) {
        SetWindowPos(editSymbol_, nullptr, editRect_.left, editRect_.top,
                     editRect_.right - editRect_.left, editRect_.bottom - editRect_.top,
                     SWP_NOZORDER);
    }

    tabRects_.clear();
    const int pad = sc(8);
    const int tabW = std::max(sc(40), (width - pad * 2) / kPageCount);
    for (int i = 0; i < kPageCount; ++i) {
        tabRects_.push_back(RECT{pad + i * tabW, tabs_.top + sc(2), pad + (i + 1) * tabW,
                                 tabs_.bottom - sc(2)});
    }

    inLayout_ = false;
}

void MainWindow::relayoutLadders() {
    if (!hwnd_) return;
    const AppConfig& cfg = App::get().config();
    const int pad = sc(8);
    const int gap = sc(8);
    const int areaW = content_.right - content_.left;
    const int areaH = content_.bottom - content_.top;
    if (areaW <= 0 || areaH <= 0) return;

    int ladderW = cfg.ladderWidth > 0 ? cfg.ladderWidth : sc(380);
    ladderW = std::max(sc(240), ladderW);
    int ladderH = cfg.ladderHeight > 0 ? cfg.ladderHeight : areaH - pad * 2;
    ladderH = std::max(sc(220), std::min(ladderH, areaH - pad * 2));

    const int count = static_cast<int>(ladders_.size());
    const int totalW = count == 0 ? 0 : count * ladderW + (count - 1) * gap + pad * 2;
    maxScroll_ = std::max(0, totalW - areaW);
    scrollX_ = std::max(0, std::min(scrollX_, maxScroll_));

    int x = content_.left + pad - scrollX_;
    const int bodyTop = content_.top + pad;
    for (auto& ladder : ladders_) {
        // 子窗口不能超出内容区，否则会压到右侧信息面板上
        const int left = std::max(x, static_cast<int>(content_.left));
        const int right = std::min(x + ladderW, static_cast<int>(content_.right));
        if (right - left < sc(120)) {
            ladder->setVisible(false);
        } else {
            ladder->setVisible(true);
            ladder->moveTo(left, bodyTop, right - left, ladderH);
        }
        x += ladderW + gap;
    }
    updateScrollBar();
}

void MainWindow::updateScrollBar() {
    if (!hwnd_) return;
    const int page = std::max(1, static_cast<int>(content_.right - content_.left));
    const bool need = maxScroll_ > 0;
    if (need != scrollVisible_) {
        scrollVisible_ = need;
        ShowScrollBar(hwnd_, SB_HORZ, need);
        return;
    }
    if (!need) return;

    SCROLLINFO si;
    ZeroMemory(&si, sizeof(si));
    si.cbSize = sizeof(si);
    si.fMask = SIF_RANGE | SIF_PAGE | SIF_POS;
    si.nMin = 0;
    si.nMax = maxScroll_ + page - 1;
    si.nPage = static_cast<UINT>(page);
    si.nPos = scrollX_;
    SetScrollInfo(hwnd_, SB_HORZ, &si, TRUE);
}

void MainWindow::rebuildLadders() {
    ladders_.clear();
    const Session& session = App::get().session();
    const AppConfig& cfg = App::get().config();
    if (session.page >= 0 && session.page < static_cast<int>(cfg.pages.size())) {
        for (const auto& symbol : cfg.pages[static_cast<std::size_t>(session.page)]) {
            auto ladder = std::make_unique<PriceLadder>(hwnd_, symbol, session.page);
            ladder->setVisible(true);
            ladders_.push_back(std::move(ladder));
        }
    }
    ladderCount_ = static_cast<int>(ladders_.size());
    relayoutLadders();
    refresh();
}

void MainWindow::onPageChanged() {
    if (!hwnd_) return;
    rebuildLadders();
}

void MainWindow::recenterLadders() {
    for (auto& ladder : ladders_) ladder->resetCenter();
    App::get().setStatus("已复位所有价格梯子中心");
}

void MainWindow::applyLadderSize() {
    relayoutLadders();
    refresh();
}

int MainWindow::hitTest(POINT pt) const {
    auto inside = [&pt](const RECT& r) { return PtInRect(&r, pt) != FALSE; };
    if (inside(btnLive_)) return kHitLive;
    if (inside(btnReplay_)) return kHitReplay;
    for (int i = 0; i < 5; ++i) {
        if (inside(speed_[i])) return kHitSpeedBase + i;
    }
    if (inside(btnAdd_)) return kHitAdd;
    if (inside(btnCancel_)) return kHitCancel;
    if (inside(btnReset_)) return kHitReset;
    for (std::size_t i = 0; i < tabRects_.size(); ++i) {
        if (inside(tabRects_[i])) return kHitTabBase + static_cast<int>(i);
    }
    return 0;
}

void MainWindow::addSymbolFromEdit() {
    wchar_t buf[64] = {0};
    if (editSymbol_) GetWindowTextW(editSymbol_, buf, static_cast<int>(std::size(buf)));
    const std::string symbol = trim(wideToUtf8(buf));
    if (symbol.empty()) {
        App::get().setStatus("请输入合约代码，例如 SHFE.rb2701");
        return;
    }
    if (!App::get().hasSymbol(symbol)) {
        App::get().config().lastSymbol = symbol;
    }
    App::get().addSymbol(symbol, true);
    if (editSymbol_) SendMessageW(editSymbol_, EM_SETSEL, 0, -1);
    SetFocus(hwnd_);
}

void MainWindow::removeLadder(HWND ladderHwnd) {
    std::string symbol;
    int page = 0;
    bool found = false;
    for (const auto& ladder : ladders_) {
        if (ladder->hwnd() == ladderHwnd) {
            symbol = ladder->symbol();
            page = ladder->page();
            found = true;
            break;
        }
    }
    if (!found) return;
    App::get().removeSymbolFromPage(symbol, page);
}

void MainWindow::paintButton(HDC dc, const RECT& rc, const std::wstring& text, bool active,
                             bool primary) {
    COLORREF fill = Th::panelAlt;
    COLORREF borderColor = Th::border;
    COLORREF fg = Th::textDim;
    if (active) {
        fill = RGB(54, 96, 156);
        borderColor = Th::accent;
        fg = RGB(240, 245, 252);
    } else if (primary) {
        fill = RGB(42, 72, 118);
        borderColor = RGB(74, 116, 172);
        fg = RGB(214, 228, 246);
    }
    fillSolid(dc, rc, fill);
    HBRUSH frame = CreateSolidBrush(borderColor);
    FrameRect(dc, &rc, frame);
    DeleteObject(frame);
    drawTextIn(dc, text, rc, fg, Th::fontSmallBold, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
}

void MainWindow::paintMetric(HDC dc, int right, int centerY, const wchar_t* label,
                             const std::wstring& value, COLORREF color, int* newRight) {
    const int valueW = textWidth(dc, Th::fontMono, value);
    const int labelW = textWidth(dc, Th::fontSmall, label);
    const int total = labelW + sc(6) + valueW;
    const int left = right - total;
    RECT lr{left, centerY - sc(9), left + labelW, centerY + sc(9)};
    drawTextIn(dc, label, lr, Th::textFaint, Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT vr{left + labelW + sc(6), centerY - sc(9), right, centerY + sc(9)};
    drawTextIn(dc, value, vr, color, Th::fontMonoBold, DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    if (newRight) *newRight = left - sc(16);
}

void MainWindow::paintTopBar(HDC dc, const RECT& rc) {
    fillSolid(dc, rc, Th::panel);
    drawHLine(dc, 0, rc.right, rc.bottom - 1, Th::border);

    RECT brand{sc(12), rc.top, sc(196), rc.bottom};
    drawTextIn(dc, L"ORDER TRADER", brand, Th::accent, Th::fontTitle,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT version{sc(180), rc.top, sc(228), rc.bottom};
    drawTextIn(dc, L"v1.0", version, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    const Session& session = App::get().session();
    const AppConfig& cfg = App::get().config();
    const std::string user = session.user.empty() ? cfg.user : session.user;

    const int cy = (rc.top + rc.bottom) / 2;
    const int pillLeft = sc(244);
    RECT dot{pillLeft, cy - sc(3), pillLeft + sc(6), cy + sc(3)};
    fillSolid(dc, dot, session.bridgeReady ? Th::up : Th::down);
    RECT userRect{pillLeft + sc(10), rc.top, pillLeft + sc(140), rc.bottom};
    drawTextIn(dc, utf8ToWide(user), userRect, Th::text, Th::fontMono,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT stateRect{pillLeft + sc(140), rc.top, pillLeft + sc(228), rc.bottom};
    drawTextIn(dc, session.bridgeReady ? L"tqsdk 已连接" : L"等待连接",
               stateRect, session.bridgeReady ? Th::textFaint : Th::warn, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    const SimEngine& sim = App::get().sim();
    const double equity = sim.equity();
    const double dayPnl = equity - sim.initialBalance();
    int right = rc.right - sc(12);
    paintMetric(dc, right, cy, L"余额", moneyText(sim.balance()), Th::text, &right);
    paintMetric(dc, right, cy, L"当日盈亏", moneyText(dayPnl, true),
                dayPnl >= 0 ? Th::up : Th::down, &right);
    paintMetric(dc, right, cy, L"手续费", moneyText(sim.fees()), Th::warn, &right);
    paintMetric(dc, right, cy, L"权益", moneyText(equity), Th::accent, &right);
}

void MainWindow::paintToolbar(HDC dc, const RECT& rc) {
    fillSolid(dc, rc, Th::panel);
    drawHLine(dc, 0, rc.right, rc.bottom - 1, Th::border);

    const Session& session = App::get().session();
    paintButton(dc, btnLive_, L"实时模拟盘", session.mode == RunMode::Live, false);
    paintButton(dc, btnReplay_, L"行情回放", session.mode == RunMode::Replay, false);

    RECT speedLabel{btnReplay_.right + sc(4), rc.top, speed_[0].left - sc(4), rc.bottom};
    drawTextIn(dc, L"倍速", speedLabel, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    const wchar_t* speedText[5] = {L"0.5x", L"1x", L"2x", L"5x", L"10x"};
    for (int i = 0; i < 5; ++i) {
        paintButton(dc, speed_[i], speedText[i], App::get().config().replaySpeedIndex == i, false);
    }

    RECT symbolLabel{speed_[4].right + sc(4), rc.top, editRect_.left - sc(4), rc.bottom};
    drawTextIn(dc, L"合约", symbolLabel, Th::textFaint, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
    paintButton(dc, btnAdd_, L"添加", false, true);
    paintButton(dc, btnCancel_, L"撤销全部", false, false);
    paintButton(dc, btnReset_, L"重置账户", false, false);

    RECT hint{btnReset_.right + sc(16), rc.top, rc.right - sc(12), rc.bottom};
    drawTextIn(dc, L"空格 撤单 · ESC 复位中心 · F1-F12 切页 · 拖动梯子边缘统一缩放",
               hint, Th::textFaint, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
}

void MainWindow::paintTabs(HDC dc, const RECT& rc) {
    fillSolid(dc, rc, Th::bg);
    drawHLine(dc, 0, rc.right, rc.top, Th::border);
    drawHLine(dc, 0, rc.right, rc.bottom - 1, Th::border);

    const Session& session = App::get().session();
    const AppConfig& cfg = App::get().config();
    for (std::size_t i = 0; i < tabRects_.size(); ++i) {
        const RECT& tab = tabRects_[i];
        const bool active = static_cast<int>(i) == session.page;
        fillSolid(dc, tab, active ? Th::panelAlt : Th::bg);
        if (active) {
            RECT accentBar{tab.left, tab.top, tab.right, tab.top + sc(2)};
            fillSolid(dc, accentBar, Th::accent);
        }
        drawVLine(dc, tab.left, tab.top, tab.bottom, Th::gridLine);

        wchar_t label[8];
        swprintf(label, 8, L"%d", static_cast<int>(i) + 1);
        RECT lr{tab.left + sc(2), tab.top, tab.right - sc(2), tab.bottom};
        drawTextIn(dc, label, lr, active ? Th::text : Th::textDim, Th::fontSmallBold,
                   DT_CENTER | DT_VCENTER | DT_SINGLELINE);

        const std::size_t count =
            i < cfg.pages.size() ? cfg.pages[i].size() : static_cast<std::size_t>(0);
        if (count > 0) {
            const std::wstring badge = std::to_wstring(count);
            const int badgeW = textWidth(dc, Th::fontSmall, badge);
            RECT br{tab.right - sc(4) - badgeW, tab.top + sc(1), tab.right - sc(4),
                    tab.top + sc(1) + sc(14)};
            drawTextIn(dc, badge, br, active ? Th::accent : Th::textFaint, Th::fontSmall,
                       DT_RIGHT | DT_VCENTER | DT_SINGLELINE);
        }
    }
}

void MainWindow::paintEmptyPage(HDC dc, const RECT& rc) {
    fillSolid(dc, rc, Th::bg);
    const int boxW = std::min(sc(560), static_cast<int>(rc.right - rc.left) - sc(40));
    const int left = rc.left + (rc.right - rc.left - boxW) / 2;
    const int top = rc.top + (rc.bottom - rc.top) / 2 - sc(70);

    RECT l1{left, top, left + boxW, top + sc(26)};
    drawTextIn(dc, L"当前页面还没有 DOM 梯子", l1, Th::textDim, Th::fontUiBold,
               DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    RECT l2{left, top + sc(30), left + boxW, top + sc(54)};
    drawTextIn(dc, L"在上方输入合约代码并点击「添加」，例如 SHFE.rb2701 / DCE.m2601",
               l2, Th::textFaint, Th::fontSmall, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    RECT l3{left, top + sc(54), left + boxW, top + sc(78)};
    drawTextIn(dc, L"每个页面可放置多个梯子；拖动梯子右/下边缘可统一调整全部尺寸",
               l3, Th::textFaint, Th::fontSmall, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
    RECT l4{left, top + sc(78), left + boxW, top + sc(102)};
    drawTextIn(dc, L"鼠标左键挂 1 手，右键挂 2 手；数量可在梯子左侧修改",
               l4, Th::textFaint, Th::fontSmall, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
}

void MainWindow::paintStatus(HDC dc, const RECT& rc) {
    fillSolid(dc, rc, Th::header);
    drawHLine(dc, 0, rc.right, rc.top, Th::border);

    const Session& session = App::get().session();
    std::wstring status = utf8ToWide(session.status);
    COLORREF statusColor = Th::textDim;
    if (status.find(L"失败") != std::wstring::npos || status.find(L"错误") != std::wstring::npos ||
        status.find(L"超时") != std::wstring::npos) {
        statusColor = Th::down;
    } else if (status.find(L"成功") != std::wstring::npos ||
               status.find(L"开始") != std::wstring::npos) {
        statusColor = Th::up;
    }
    RECT left{sc(12), rc.top, rc.right / 2, rc.bottom};
    drawTextIn(dc, status, left, statusColor, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

    std::wstring right;
    right += session.mode == RunMode::Live ? L"实时模拟盘" : L"行情回放";
    wchar_t speed[16];
    swprintf(speed, 16, L" · %.1fx", App::get().speedValue());
    right += speed;
    right += L" · 第 " + std::to_wstring(session.page + 1) + L" 页";
    if (App::get().replay().active()) {
        wchar_t progress[32];
        swprintf(progress, 32, L" · 回放 %.1f%%", App::get().replay().progress() * 100.0);
        right += progress;
    }
    if (session.lastDataMs > 0) right += L" · 行情 " + clockText(session.lastDataMs);
    right += session.bridgeReady ? L" · 桥接就绪" : L" · 桥接未连接";

    RECT rr{rc.right / 2, rc.top, rc.right - sc(12), rc.bottom};
    drawTextIn(dc, right, rr, session.bridgeReady ? Th::textDim : Th::warn, Th::fontSmall,
               DT_RIGHT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
}

void MainWindow::paint(HDC dc) {
    RECT rc;
    GetClientRect(hwnd_, &rc);
    fillSolid(dc, rc, Th::bg);

    paintTopBar(dc, topBar_);
    paintToolbar(dc, toolbar_);
    paintTabs(dc, tabs_);
    paintStatus(dc, status_);

    RECT content = content_;
    if (ladderCount_ == 0) {
        paintEmptyPage(dc, content);
    } else {
        fillSolid(dc, content, Th::bg);
    }
    drawVLine(dc, content.right, content.top, content.bottom, Th::border);
}

LRESULT CALLBACK MainWindow::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_NCCREATE) {
        CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        MainWindow* self = static_cast<MainWindow*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
    }
    MainWindow* self = reinterpret_cast<MainWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!self) return DefWindowProcW(hwnd, msg, wp, lp);
    return self->handleMessage(msg, wp, lp);
}

LRESULT MainWindow::handleMessage(UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_CREATE: {
            buildControls();
            layout();
            rebuildLadders();
            SetTimer(hwnd_, 1, 200, nullptr);
            App::get().bridge().setNotifyWindow(hwnd_);
            applog::write("主窗口已创建");
            return 0;
        }
        case WM_SIZE:
            layout();
            relayoutLadders();
            return 0;
        case WM_PAINT: {
            BufferedPaint bp(hwnd_);
            paint(bp.dc());
            return 0;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_TIMER: {
            App::get().update();
            refresh();
            for (auto& ladder : ladders_) ladder->refresh();
            return 0;
        }
        case kBridgeMsg:
            App::get().pumpBridge();
            return 0;
        case kAddSymbolMsg:
            addSymbolFromEdit();
            return 0;
        case kRemoveMsg:
            removeLadder(reinterpret_cast<HWND>(lp));
            return 0;
        case WM_HSCROLL: {
            const int page = std::max(1, static_cast<int>(content_.right - content_.left));
            SCROLLINFO si;
            ZeroMemory(&si, sizeof(si));
            si.cbSize = sizeof(si);
            si.fMask = SIF_ALL;
            GetScrollInfo(hwnd_, SB_HORZ, &si);
            switch (LOWORD(wp)) {
                case SB_LINELEFT:
                    scrollX_ -= sc(60);
                    break;
                case SB_LINERIGHT:
                    scrollX_ += sc(60);
                    break;
                case SB_PAGELEFT:
                    scrollX_ -= page;
                    break;
                case SB_PAGERIGHT:
                    scrollX_ += page;
                    break;
                case SB_THUMBTRACK:
                case SB_THUMBPOSITION:
                    scrollX_ = si.nTrackPos;
                    break;
                default:
                    return 0;
            }
            relayoutLadders();
            return 0;
        }
        case WM_MOUSEWHEEL: {
            POINT pt{GET_X_LPARAM(lp), GET_Y_LPARAM(lp)};
            ScreenToClient(hwnd_, &pt);
            if (maxScroll_ > 0 && PtInRect(&content_, pt)) {
                const int delta = GET_WHEEL_DELTA_WPARAM(wp);
                scrollX_ += delta > 0 ? -sc(60) : sc(60);
                relayoutLadders();
                return 0;
            }
            return 0;
        }
        case WM_KEYDOWN: {
            if (wp >= VK_F1 && wp <= VK_F12) {
                App::get().setPage(static_cast<int>(wp - VK_F1));
                return 0;
            }
            if (wp == VK_SPACE) {
                App::get().cancelAll();
                return 0;
            }
            if (wp == VK_ESCAPE) {
                recenterLadders();
                return 0;
            }
            break;
        }
        case WM_LBUTTONUP: {
            POINT pt{GET_X_LPARAM(lp), GET_Y_LPARAM(lp)};
            const int hit = hitTest(pt);
            if (hit == kHitLive) {
                App::get().setMode(RunMode::Live);
            } else if (hit == kHitReplay) {
                App::get().setMode(RunMode::Replay);
            } else if (hit >= kHitSpeedBase && hit < kHitSpeedBase + 5) {
                App::get().setSpeedIndex(hit - kHitSpeedBase);
            } else if (hit == kHitAdd) {
                addSymbolFromEdit();
            } else if (hit == kHitCancel) {
                App::get().cancelAll();
            } else if (hit == kHitReset) {
                if (MessageBoxW(hwnd_, L"确定要重置虚拟账户吗？\n持仓、挂单与收益曲线都将清空。",
                                L"重置账户", MB_ICONQUESTION | MB_YESNO) == IDYES) {
                    App::get().resetAccount();
                }
            } else if (hit >= kHitTabBase) {
                App::get().setPage(hit - kHitTabBase);
            }
            SetFocus(hwnd_);
            return 0;
        }
        case WM_LBUTTONDOWN:
            SetFocus(hwnd_);
            return 0;
        case WM_CTLCOLOREDIT: {
            HDC dc = reinterpret_cast<HDC>(wp);
            SetTextColor(dc, Th::text);
            SetBkColor(dc, RGB(23, 24, 27));
            return reinterpret_cast<LRESULT>(editBrush_);
        }
        case WM_COMMAND: {
            if (reinterpret_cast<HWND>(lp) == editSymbol_ && HIWORD(wp) == EN_SETFOCUS) {
                InvalidateRect(hwnd_, &editRect_, FALSE);
            }
            return 0;
        }
        case WM_GETMINMAXINFO: {
            MINMAXINFO* info = reinterpret_cast<MINMAXINFO*>(lp);
            RECT work{};
            SystemParametersInfoW(SPI_GETWORKAREA, 0, &work, 0);
            const int workW = work.right - work.left;
            const int workH = work.bottom - work.top;
            info->ptMinTrackSize.x = std::min(sc(1100), workW);
            info->ptMinTrackSize.y = std::min(sc(700), workH);
            return 0;
        }
        case WM_CLOSE:
            DestroyWindow(hwnd_);
            return 0;
        case WM_NCDESTROY: {
            HWND closing = hwnd_;
            KillTimer(closing, 1);
            if (editSymbol_) {
                if (IsWindow(editSymbol_) && g_editProc) {
                    SetWindowLongPtrW(editSymbol_, GWLP_WNDPROC,
                                      reinterpret_cast<LONG_PTR>(g_editProc));
                }
                editSymbol_ = nullptr;
            }
            if (editBrush_) {
                DeleteObject(editBrush_);
                editBrush_ = nullptr;
            }
            if (fontEdit_) {
                DeleteObject(fontEdit_);
                fontEdit_ = nullptr;
            }
            if (info_) {
                delete info_;
                info_ = nullptr;
            }
            ladders_.clear();
            hwnd_ = nullptr;
            App::get().detachMain();
            LRESULT result = DefWindowProcW(closing, msg, wp, lp);
            PostQuitMessage(0);
            delete this;
            return result;
        }
        default:
            break;
    }
    return DefWindowProcW(hwnd_, msg, wp, lp);
}

}  // namespace ot
