#include "SettingsDialog.h"

#include <algorithm>
#include <iterator>
#include <string>

#include "App.h"
#include "ui/Gdi.h"
#include "ui/Theme.h"

namespace ot {

namespace {

const wchar_t* kClass = L"OT_SettingsDialog";
constexpr int kIdSave = 1;
constexpr int kIdCancel = 2;

int sc(int v) { return Th::S(v); }

WNDPROC g_editProc = nullptr;

LRESULT CALLBACK SettingsEditProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_KEYDOWN && wp == VK_RETURN) {
        PostMessageW(GetParent(hwnd), WM_COMMAND, MAKEWPARAM(kIdSave, BN_CLICKED), 0);
        return 0;
    }
    return CallWindowProcW(g_editProc, hwnd, msg, wp, lp);
}

std::string trimmed(const std::wstring& text) {
    std::string out = wideToUtf8(text);
    std::size_t begin = 0;
    std::size_t end = out.size();
    while (begin < end && (out[begin] == ' ' || out[begin] == '\t' || out[begin] == '\r' || out[begin] == '\n')) ++begin;
    while (end > begin && (out[end - 1] == ' ' || out[end - 1] == '\t' || out[end - 1] == '\r' || out[end - 1] == '\n')) --end;
    return out.substr(begin, end - begin);
}

}  // namespace

SettingsDialog::SettingsDialog(HWND owner) : owner_(owner) {}

SettingsDialog::~SettingsDialog() {
    if (hwnd_) {
        SetWindowLongPtrW(hwnd_, GWLP_USERDATA, 0);
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
    if (bgBrush_) {
        DeleteObject(bgBrush_);
        bgBrush_ = nullptr;
    }
    if (editBrush_) {
        DeleteObject(editBrush_);
        editBrush_ = nullptr;
    }
}

bool SettingsDialog::create() {
    if (bgBrush_) { DeleteObject(bgBrush_); bgBrush_ = nullptr; }
    if (editBrush_) { DeleteObject(editBrush_); editBrush_ = nullptr; }

    WNDCLASSEXW wc;
    ZeroMemory(&wc, sizeof(wc));
    wc.cbSize = sizeof(wc);
    wc.style = CS_HREDRAW | CS_VREDRAW;
    wc.lpfnWndProc = &SettingsDialog::WndProc;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    wc.hbrBackground = nullptr;
    wc.lpszClassName = kClass;
    RegisterClassExW(&wc);

    RECT anchor{};
    if (owner_) {
        GetWindowRect(owner_, &anchor);
    } else {
        SystemParametersInfoW(SPI_GETWORKAREA, 0, &anchor, 0);
    }
    const int width = sc(440);
    const int height = sc(232);
    const int x = anchor.left + ((anchor.right - anchor.left) - width) / 2;
    const int y = anchor.top + ((anchor.bottom - anchor.top) - height) / 2;

    bgBrush_ = CreateSolidBrush(Th::panel);
    editBrush_ = CreateSolidBrush(RGB(23, 24, 27));

    hwnd_ = CreateWindowExW(WS_EX_DLGMODALFRAME | WS_EX_CONTROLPARENT, kClass,
                            L"设置 — tqsdk 账号", WS_POPUP | WS_CAPTION | WS_SYSMENU, x, y, width,
                            height, owner_, nullptr, GetModuleHandleW(nullptr), this);
    if (!hwnd_) return false;
    applyDarkTitleBar(hwnd_);

    auto label = [this](const wchar_t* text, int id) {
        return CreateWindowExW(0, L"STATIC", text, WS_CHILD | WS_VISIBLE | SS_LEFT, 0, 0, 10, 10,
                               hwnd_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(id)),
                               GetModuleHandleW(nullptr), nullptr);
    };
    HWND labelUser = label(L"tqsdk 账号", 100);
    HWND labelPass = label(L"tqsdk 密码", 101);
    HWND hint = label(L"账号密码保存在 config/order-trader.json", 102);

    const std::string initUser = App::get().config().user;
    const std::string initPass = App::get().config().password;
    editUser_ = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", utf8ToWide(initUser).c_str(),
                                WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL, 0, 0, 10, 10,
                                hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
    editPass_ = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", utf8ToWide(initPass).c_str(),
                                WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL | ES_PASSWORD,
                                0, 0, 10, 10, hwnd_, nullptr, GetModuleHandleW(nullptr), nullptr);
    SendMessageW(editUser_, EM_SETLIMITTEXT, 64, 0);
    SendMessageW(editPass_, EM_SETLIMITTEXT, 64, 0);

    btnSave_ = CreateWindowExW(0, L"BUTTON", L"保存并连接",
                               WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 0, 0, 10, 10,
                               hwnd_, reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdSave)),
                               GetModuleHandleW(nullptr), nullptr);
    btnCancel_ = CreateWindowExW(0, L"BUTTON", L"取消", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 0, 0,
                                 10, 10, hwnd_,
                                 reinterpret_cast<HMENU>(static_cast<INT_PTR>(kIdCancel)),
                                 GetModuleHandleW(nullptr), nullptr);

    for (HWND child : {labelUser, labelPass, hint, editUser_, editPass_, btnSave_, btnCancel_}) {
        SendMessageW(child, WM_SETFONT, reinterpret_cast<WPARAM>(Th::fontUi), TRUE);
    }

    g_editProc = reinterpret_cast<WNDPROC>(
        SetWindowLongPtrW(editUser_, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&SettingsEditProc)));
    SetWindowLongPtrW(editPass_, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&SettingsEditProc));

    layout();
    return true;
}

void SettingsDialog::layout() {
    if (!hwnd_) return;
    RECT rc;
    GetClientRect(hwnd_, &rc);
    const int left = sc(18);
    const int labelW = sc(96);
    const int editLeft = left + labelW + sc(10);
    const int editRight = rc.right - sc(18);
    const int editH = sc(24);
    const int row1 = sc(22);
    const int row2 = row1 + sc(42);

    SetWindowPos(GetDlgItem(hwnd_, 100), nullptr, left, row1 + sc(3), labelW, editH,
                 SWP_NOZORDER);
    SetWindowPos(GetDlgItem(hwnd_, 101), nullptr, left, row2 + sc(3), labelW, editH,
                 SWP_NOZORDER);
    SetWindowPos(editUser_, nullptr, editLeft, row1, editRight - editLeft, editH, SWP_NOZORDER);
    SetWindowPos(editPass_, nullptr, editLeft, row2, editRight - editLeft, editH, SWP_NOZORDER);
    SetWindowPos(GetDlgItem(hwnd_, 102), nullptr, left, row2 + sc(44), rc.right - left * 2, sc(40),
                 SWP_NOZORDER);

    const int btnH = sc(30);
    const int btnY = rc.bottom - sc(18) - btnH;
    SetWindowPos(btnSave_, nullptr, rc.right - sc(18) - sc(132), btnY, sc(132), btnH, SWP_NOZORDER);
    SetWindowPos(btnCancel_, nullptr, rc.right - sc(18) - sc(132) - sc(10) - sc(94), btnY, sc(94),
                 btnH, SWP_NOZORDER);
}

void SettingsDialog::submit() {
    wchar_t userBuf[128] = {0};
    wchar_t passBuf[128] = {0};
    if (editUser_) GetWindowTextW(editUser_, userBuf, static_cast<int>(std::size(userBuf)));
    if (editPass_) GetWindowTextW(editPass_, passBuf, static_cast<int>(std::size(passBuf)));

    std::string user = trimmed(userBuf);
    std::string password = trimmed(passBuf);
    if (user.empty() || password.empty()) {
        MessageBoxW(hwnd_, L"账号和密码不能为空。", L"设置", MB_ICONWARNING);
        return;
    }
    user_ = user;
    password_ = password;
    saved_ = true;
    done_ = true;
    ShowWindow(hwnd_, SW_HIDE);
}

bool SettingsDialog::showModal() {
    if (!create()) return false;

    EnableWindow(owner_, FALSE);
    ShowWindow(hwnd_, SW_SHOWNORMAL);
    UpdateWindow(hwnd_);
    SetForegroundWindow(hwnd_);
    SetFocus(editUser_);

    MSG msg;
    while (!done_) {
        const BOOL got = GetMessageW(&msg, nullptr, 0, 0);
        if (got <= 0) {
            if (got == 0) PostQuitMessage(static_cast<int>(msg.wParam));
            break;
        }
        if (!IsDialogMessageW(hwnd_, &msg)) {
            TranslateMessage(&msg);
            DispatchMessageW(&msg);
        }
    }

    if (owner_) {
        EnableWindow(owner_, TRUE);
        SetForegroundWindow(owner_);
    }
    if (hwnd_) {
        SetWindowLongPtrW(hwnd_, GWLP_USERDATA, 0);
        DestroyWindow(hwnd_);
        hwnd_ = nullptr;
    }
    return saved_;
}

LRESULT CALLBACK SettingsDialog::WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == WM_NCCREATE) {
        CREATESTRUCTW* cs = reinterpret_cast<CREATESTRUCTW*>(lp);
        SettingsDialog* self = static_cast<SettingsDialog*>(cs->lpCreateParams);
        SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        self->hwnd_ = hwnd;
    }
    SettingsDialog* self = reinterpret_cast<SettingsDialog*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
    if (!self) return DefWindowProcW(hwnd, msg, wp, lp);
    return self->handleMessage(msg, wp, lp);
}

LRESULT SettingsDialog::handleMessage(UINT msg, WPARAM wp, LPARAM lp) {
    switch (msg) {
        case WM_COMMAND: {
            const int id = LOWORD(wp);
            if (id == kIdSave) {
                submit();
                return 0;
            }
            if (id == kIdCancel) {
                done_ = true;
                ShowWindow(hwnd_, SW_HIDE);
                return 0;
            }
            return 0;
        }
        case WM_KEYDOWN:
            if (wp == VK_ESCAPE) {
                done_ = true;
                ShowWindow(hwnd_, SW_HIDE);
                return 0;
            }
            break;
        case WM_CLOSE:
            done_ = true;
            ShowWindow(hwnd_, SW_HIDE);
            return 0;
        case WM_ERASEBKGND: {
            RECT rc;
            GetClientRect(hwnd_, &rc);
            fillSolid(reinterpret_cast<HDC>(wp), rc, Th::panel);
            return 1;
        }
        case WM_PAINT: {
            PAINTSTRUCT ps;
            HDC dc = BeginPaint(hwnd_, &ps);
            RECT rc;
            GetClientRect(hwnd_, &rc);
            fillSolid(dc, rc, Th::panel);
            drawHLine(dc, 0, rc.right, rc.bottom - sc(58), Th::border);
            EndPaint(hwnd_, &ps);
            return 0;
        }
        case WM_CTLCOLORSTATIC: {
            HDC dc = reinterpret_cast<HDC>(wp);
            SetTextColor(dc, Th::textDim);
            SetBkColor(dc, Th::panel);
            return reinterpret_cast<LRESULT>(bgBrush_);
        }
        case WM_CTLCOLOREDIT: {
            HDC dc = reinterpret_cast<HDC>(wp);
            SetTextColor(dc, Th::text);
            SetBkColor(dc, RGB(23, 24, 27));
            return reinterpret_cast<LRESULT>(editBrush_);
        }
        case WM_SIZE:
            layout();
            return 0;
        case WM_NCDESTROY: {
            HWND closing = hwnd_;
            if (g_editProc) {
                if (editUser_ && IsWindow(editUser_)) {
                    SetWindowLongPtrW(editUser_, GWLP_WNDPROC,
                                      reinterpret_cast<LONG_PTR>(g_editProc));
                }
                if (editPass_ && IsWindow(editPass_)) {
                    SetWindowLongPtrW(editPass_, GWLP_WNDPROC,
                                      reinterpret_cast<LONG_PTR>(g_editProc));
                }
            }
            editUser_ = nullptr;
            editPass_ = nullptr;
            btnSave_ = nullptr;
            btnCancel_ = nullptr;
            hwnd_ = nullptr;
            return DefWindowProcW(closing, msg, wp, lp);
        }
        default:
            break;
    }
    return DefWindowProcW(hwnd_, msg, wp, lp);
}

}  // namespace ot
