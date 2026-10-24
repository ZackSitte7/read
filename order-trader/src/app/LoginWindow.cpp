#include "LoginWindow.h"

#include <commctrl.h>
#include <windowsx.h>

#include <iterator>
#include "App.h"
#include "ui/Gdi.h"
#include "ui/Theme.h"
#include "util/Log.h"

namespace ot {

namespace {

constexpr int kIdUser = 100;
constexpr int kIdPass = 101;
constexpr int kIdLogin = 102;
constexpr UINT kBridgeMsg = WM_APP + 10;
constexpr UINT kSubmitMsg = WM_APP + 30;

const wchar_t* kClass = L"OT_LoginWindow";

WNDPROC g_oldEditProc = nullptr;

LRESULT CALLBACK EditSubclass(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_KEYDOWN && wp == VK_RETURN) {
        PostMessageW(GetParent(hwnd), kSubmitMsg, 0, 0);
        return 0;
    }
    if (msg == WM_CHAR && wp == VK_RETURN) return 0;
    return CallWindowProcW(g_oldEditProc, hwnd, msg, wp, lp);
}

}  // namespace

bool LoginWindow::create(HINSTANCE inst) {
    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &LoginWindow::WndProc;
    wc.hInstance = inst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);

    const int w = Th::S(560);
    const int h = Th::S(432);
    const int x = (GetSystemMetrics(SM_CXSCREEN) - w) / 2;
    const int y = (GetSystemMetrics(SM_CYSCREEN) - h) / 2;

    hwnd_ = CreateWindowExW(WS_EX_APPWINDOW, kClass, L"Order Trader v1.0", WS_POPUP, x, y, w, h,
                            nullptr, nullptr, inst, this);
    if (!hwnd_) return false;
    ShowWindow(hwnd_, SW_SHOW);
    UpdateWindow(hwnd_);
    return true;
}

void LoginWindow::closeSelf() {
    if (hwnd_) DestroyWindow(hwnd_);
}

void LoginWindow::showError(const std::string& message) {
    busy_ = false;
    if (buttonLogin_) EnableWindow(buttonLogin_, TRUE);
    setStatus(utf8ToWide(message), Th::down);
    if (hwnd_) {
        SetForegroundWindow(hwnd_);
        InvalidateRect(hwnd_, nullptr, FALSE);
    }
}

void LoginWindow::setStatus(const std::wstring& text, COLORREF color) {
    status_ = text;
    statusColor_ = color;
    if (hwnd_) {
        RECT rc{0, Th::S(340), Th::S(560), Th::S(380)};
        InvalidateRect(hwnd_, &rc, FALSE);
    }
}

void LoginWindow::buildControls() {
    const AppConfig& cfg = App::get().config();
    editBrush_ = CreateSolidBrush(RGB(23, 24, 27));
    fontEdit_ = CreateFontW(Th::F(10.0), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                            OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                            FIXED_PITCH | FF_MODERN, L"Consolas");

    editUser_ = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
                                0, 0, 10, 10, hwnd_, reinterpret_cast<HMENU>(kIdUser),
                                App::get().instance(), nullptr);
    editPass_ = CreateWindowExW(0, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | ES_PASSWORD,
                                0, 0, 10, 10, hwnd_, reinterpret_cast<HMENU>(kIdPass),
                                App::get().instance(), nullptr);
    buttonLogin_ = CreateWindowExW(0, L"BUTTON", L"登 录",
                                   WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_OWNERDRAW, 0, 0, 10, 10,
                                   hwnd_, reinterpret_cast<HMENU>(kIdLogin), App::get().instance(),
                                   nullptr);

    for (HWND edit : {editUser_, editPass_}) {
        SendMessageW(edit, WM_SETFONT, reinterpret_cast<WPARAM>(fontEdit_), TRUE);
        SendMessageW(edit, EM_SETLIMITTEXT, 64, 0);
        g_oldEditProc = reinterpret_cast<WNDPROC>(
            SetWindowLongPtrW(edit, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&EditSubclass)));
    }

    SetWindowTextW(editUser_, utf8ToWide(cfg.user).c_str());
    SetWindowTextW(editPass_, cfg.rememberPassword ? utf8ToWide(cfg.password).c_str() : L"");
    SetFocus(editPass_);
    SendMessageW(editPass_, EM_SETSEL, 0, -1);
}

void LoginWindow::layoutControls() {
    if (!hwnd_) return;
    RECT rc;
    GetClientRect(hwnd_, &rc);
    const int left = Th::S(48);
    const int width = rc.right - left * 2;
    const int editH = Th::S(34);

    SetWindowPos(editUser_, nullptr, left, Th::S(172), width, editH, SWP_NOZORDER);
    SetWindowPos(editPass_, nullptr, left, Th::S(238), width, editH, SWP_NOZORDER);
    SetWindowPos(buttonLogin_, nullptr, left, Th::S(292), width, Th::S(44), SWP_NOZORDER);
}

void LoginWindow::submit() {
    if (busy_ || !editUser_ || !editPass_) return;
    wchar_t user[128] = {0};
    wchar_t pass[128] = {0};
    GetWindowTextW(editUser_, user, static_cast<int>(std::size(user)));
    GetWindowTextW(editPass_, pass, static_cast<int>(std::size(pass)));
    const std::wstring wuser(user);
    const std::wstring wpass(pass);
    if (wuser.empty()) {
        showError("请输入账号");
        SetFocus(editUser_);
        return;
    }
    if (wpass.empty()) {
        showError("请输入密码");
        SetFocus(editPass_);
        return;
    }
    busy_ = true;
    EnableWindow(buttonLogin_, FALSE);
    setStatus(L"正在验证 tqsdk 账户...", Th::textDim);
    InvalidateRect(hwnd_, nullptr, FALSE);
    App::get().requestLogin(wideToUtf8(wuser), wideToUtf8(wpass));
}

void LoginWindow::paint(HDC dc) {
    RECT rc;
    GetClientRect(hwnd_, &rc);
    fillSolid(dc, rc, Th::bg);

    // 顶部标题栏
    RECT title{0, 0, rc.right, Th::S(36)};
    fillSolid(dc, title, Th::panel);

    RECT logo{Th::S(48), Th::S(56), rc.right - Th::S(48), Th::S(104)};
    drawTextIn(dc, L"ORDER TRADER", logo, Th::accent, Th::fontTitle, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    drawHLine(dc, Th::S(48), rc.right - Th::S(48), Th::S(126), Th::border);

    RECT l1{Th::S(48), Th::S(148), rc.right - Th::S(48), Th::S(170)};
    drawTextIn(dc, L"tqsdk 账号", l1, Th::textDim, Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);
    RECT l2{Th::S(48), Th::S(214), rc.right - Th::S(48), Th::S(236)};
    drawTextIn(dc, L"密码", l2, Th::textDim, Th::fontSmall, DT_LEFT | DT_VCENTER | DT_SINGLELINE);

    // 输入框边框与提示
    const bool userFocus = GetFocus() == editUser_;
    const bool passFocus = GetFocus() == editPass_;
    RECT r1{Th::S(47), Th::S(171), rc.right - Th::S(47), Th::S(207)};
    RECT r2{Th::S(47), Th::S(237), rc.right - Th::S(47), Th::S(273)};
    HBRUSH frame1 = CreateSolidBrush(userFocus ? Th::accent : Th::border);
    HBRUSH frame2 = CreateSolidBrush(passFocus ? Th::accent : Th::border);
    FrameRect(dc, &r1, frame1);
    FrameRect(dc, &r2, frame2);
    DeleteObject(frame1);
    DeleteObject(frame2);

    RECT statusRect{Th::S(48), Th::S(344), rc.right - Th::S(48), Th::S(372)};
    drawTextIn(dc, status_, statusRect, statusColor_, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);

    // 关闭按钮
    RECT closeRect{rc.right - Th::S(40), 0, rc.right, Th::S(36)};
    drawTextIn(dc, L"✕", closeRect, Th::textDim, Th::fontUi, DT_CENTER | DT_VCENTER | DT_SINGLELINE);

    RECT winTitle{Th::S(16), 0, rc.right - Th::S(48), Th::S(36)};
    drawTextIn(dc, L"Order Trader v1.0", winTitle, Th::textDim, Th::fontSmall,
               DT_LEFT | DT_VCENTER | DT_SINGLELINE);
}

LRESULT CALLBACK LoginWindow::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_NCCREATE) {
        CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        LoginWindow* self = static_cast<LoginWindow*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
    }
    LoginWindow* self = reinterpret_cast<LoginWindow*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!self) return DefWindowProcW(hwnd, msg, wp, lp);
    return self->handleMessage(msg, wp, lp);
}

LRESULT LoginWindow::handleMessage(UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_CREATE: {
            buildControls();
            layoutControls();
            SetTimer(hwnd_, 1, 200, nullptr);
            return 0;
        }
        case WM_SIZE:
            layoutControls();
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
            if (!busy_) {
                InvalidateRect(hwnd_, nullptr, FALSE);
            } else {
                RECT rc{Th::S(48), Th::S(344), Th::S(512), Th::S(372)};
                InvalidateRect(hwnd_, &rc, FALSE);
            }
            return 0;
        }
        case kBridgeMsg:
            App::get().pumpBridge();
            return 0;
        case kSubmitMsg:
            submit();
            return 0;
        case WM_COMMAND: {
            const int id = LOWORD(wp);
            if (id == kIdLogin && HIWORD(wp) == BN_CLICKED) {
                submit();
                return 0;
            }
            if (id == kIdUser || id == kIdPass) {
                if (HIWORD(wp) == EN_SETFOCUS) InvalidateRect(hwnd_, nullptr, FALSE);
            }
            return 0;
        }
        case WM_CTLCOLOREDIT: {
            HDC dc = reinterpret_cast<HDC>(wp);
            SetTextColor(dc, Th::text);
            SetBkColor(dc, RGB(23, 24, 27));
            return reinterpret_cast<LRESULT>(editBrush_);
        }
        case WM_DRAWITEM: {
            DRAWITEMSTRUCT* dis = reinterpret_cast<DRAWITEMSTRUCT*>(lp);
            if (dis && dis->CtlID == kIdLogin) {
                const bool disabled = (dis->itemState & ODS_DISABLED) != 0;
                const bool pressed = (dis->itemState & ODS_SELECTED) != 0;
                COLORREF fill = disabled ? RGB(52, 62, 82) : (pressed ? RGB(52, 112, 190) : RGB(66, 130, 214));
                fillSolid(dis->hDC, dis->rcItem, fill);
                drawTextIn(dis->hDC, disabled ? L"正在验证..." : L"登 录", dis->rcItem, RGB(245, 248, 255),
                           Th::fontUiBold, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
                return TRUE;
            }
            return 0;
        }
        case WM_LBUTTONDOWN: {
            const int x = GET_X_LPARAM(lp);
            const int y = GET_Y_LPARAM(lp);
            RECT rc;
            GetClientRect(hwnd_, &rc);
            if (y < Th::S(36) && x > rc.right - Th::S(40)) {
                DestroyWindow(hwnd_);
                return 0;
            }
            if (y < Th::S(36)) {
                ReleaseCapture();
                SendMessageW(hwnd_, WM_NCLBUTTONDOWN, HTCAPTION, 0);
                return 0;
            }
            SetFocus(hwnd_);
            return 0;
        }
        case WM_NCDESTROY: {
            HWND closing = hwnd_;
            KillTimer(hwnd_, 1);
            if (editBrush_) DeleteObject(editBrush_);
            if (fontEdit_) DeleteObject(fontEdit_);
            hwnd_ = nullptr;
            App::get().detachLogin();
            const bool loggedIn = App::get().session().loggedIn;
            LRESULT result = DefWindowProcW(closing, msg, wp, lp);
            if (!loggedIn) PostQuitMessage(0);
            delete this;
            return result;
        }
        default:
            break;
    }
    return DefWindowProcW(hwnd_, msg, wp, lp);
}

}  // namespace ot
