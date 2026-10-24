// Order Trader v1.0 - 登录窗口
#pragma once

#include <string>

#include "util/Win.h"

namespace ot {

class LoginWindow {
public:
    LoginWindow() = default;
    ~LoginWindow() = default;

    bool create(HINSTANCE inst);
    HWND hwnd() const { return hwnd_; }
    void showError(const std::string& message);
    void closeSelf();

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);
    void buildControls();
    void layoutControls();
    void paint(HDC dc);
    void submit();
    void setStatus(const std::wstring& text, COLORREF color);

    HWND hwnd_ = nullptr;
    HWND editUser_ = nullptr;
    HWND editPass_ = nullptr;
    HWND buttonLogin_ = nullptr;
    HBRUSH editBrush_ = nullptr;
    HFONT fontEdit_ = nullptr;
    std::wstring status_ = L"请输入 tqsdk 账号与密码";
    COLORREF statusColor_ = RGB(138, 143, 152);
    bool busy_ = false;
    bool dragging_ = false;
    POINT dragPoint_{};
};

}  // namespace ot
