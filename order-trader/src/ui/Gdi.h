// Order Trader v1.0 - GDI 绘制辅助
#pragma once

#include <string>

#include "Theme.h"
#include "util/Win.h"

namespace ot {

inline COLORREF blendColor(COLORREF a, COLORREF b, double t) {
    if (t < 0) t = 0;
    if (t > 1) t = 1;
    const int r = static_cast<int>(GetRValue(a) + (GetRValue(b) - GetRValue(a)) * t);
    const int g = static_cast<int>(GetGValue(a) + (GetGValue(b) - GetGValue(a)) * t);
    const int bl = static_cast<int>(GetBValue(a) + (GetBValue(b) - GetBValue(a)) * t);
    return RGB(r, g, bl);
}

inline void fillSolid(HDC dc, const RECT& r, COLORREF color) {
    HBRUSH brush = CreateSolidBrush(color);
    FillRect(dc, &r, brush);
    DeleteObject(brush);
}

inline void drawHLine(HDC dc, int x1, int x2, int y, COLORREF color) {
    HPEN pen = CreatePen(PS_SOLID, 1, color);
    HGDIOBJ old = SelectObject(dc, pen);
    MoveToEx(dc, x1, y, nullptr);
    LineTo(dc, x2, y);
    SelectObject(dc, old);
    DeleteObject(pen);
}

inline void drawVLine(HDC dc, int x, int y1, int y2, COLORREF color) {
    HPEN pen = CreatePen(PS_SOLID, 1, color);
    HGDIOBJ old = SelectObject(dc, pen);
    MoveToEx(dc, x, y1, nullptr);
    LineTo(dc, x, y2);
    SelectObject(dc, old);
    DeleteObject(pen);
}

inline void drawTextIn(HDC dc, const std::wstring& text, const RECT& r, COLORREF color, HFONT font,
                       UINT format) {
    if (text.empty()) return;
    HGDIOBJ oldFont = SelectObject(dc, font);
    const COLORREF oldColor = SetTextColor(dc, color);
    const int oldMode = SetBkMode(dc, TRANSPARENT);
    RECT rc = r;
    DrawTextW(dc, text.c_str(), static_cast<int>(text.size()), &rc, format);
    SetBkMode(dc, oldMode);
    SetTextColor(dc, oldColor);
    SelectObject(dc, oldFont);
}

/// 双缓冲绘制
class BufferedPaint {
public:
    explicit BufferedPaint(HWND hwnd) : hwnd_(hwnd) {
        winDc_ = BeginPaint(hwnd_, &ps_);
        memDc_ = CreateCompatibleDC(winDc_);
        GetClientRect(hwnd_, &rect_);
        const int w = rect_.right - rect_.left;
        const int h = rect_.bottom - rect_.top;
        bitmap_ = CreateCompatibleBitmap(winDc_, w > 0 ? w : 1, h > 0 ? h : 1);
        oldBitmap_ = static_cast<HBITMAP>(SelectObject(memDc_, bitmap_));
        SetBkMode(memDc_, TRANSPARENT);
        SetBkColor(memDc_, Th::bg);
    }

    ~BufferedPaint() {
        BitBlt(winDc_, 0, 0, rect_.right - rect_.left, rect_.bottom - rect_.top, memDc_, 0, 0, SRCCOPY);
        SelectObject(memDc_, oldBitmap_);
        DeleteObject(bitmap_);
        DeleteDC(memDc_);
        EndPaint(hwnd_, &ps_);
    }

    HDC dc() const { return memDc_; }
    const RECT& client() const { return rect_; }

private:
    HWND hwnd_;
    PAINTSTRUCT ps_{};
    HDC winDc_ = nullptr;
    HDC memDc_ = nullptr;
    HBITMAP bitmap_ = nullptr;
    HBITMAP oldBitmap_ = nullptr;
    RECT rect_{};
};

/// 价格格式化：按最小变动价位推导小数位
inline std::wstring formatPrice(double price, int decs) {
    if (price == 0) return L"-";
    wchar_t buf[64];
    if (decs > 0) {
        swprintf(buf, std::size(buf), L"%.*f", decs, price);
    } else {
        swprintf(buf, std::size(buf), L"%.0f", price);
    }
    return buf;
}

inline std::wstring formatMoney(double value) {
    wchar_t buf[64];
    swprintf(buf, std::size(buf), L"%.2f", value);
    return buf;
}

inline std::wstring formatMoneyShort(double value) {
    wchar_t buf[64];
    if (value >= 0) swprintf(buf, std::size(buf), L"+%.2f", value);
    else swprintf(buf, std::size(buf), L"%.2f", value);
    return buf;
}

inline std::wstring formatInt(long long value) {
    wchar_t buf[64];
    swprintf(buf, std::size(buf), L"%lld", value);
    return buf;
}

}  // namespace ot
