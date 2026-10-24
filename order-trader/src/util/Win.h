// Order Trader v1.0 - Windows 公共前置声明与时钟
#pragma once

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif

#include <windows.h>

#include <cstdint>
#include <string>

namespace ot {

inline int64_t nowMs() {
    return static_cast<int64_t>(GetTickCount64());
}

inline std::wstring utf8ToWide(const std::string& text) {
    if (text.empty()) return std::wstring();
    const int len = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, nullptr, 0);
    if (len <= 0) return std::wstring();
    std::wstring out(static_cast<std::size_t>(len), L'\0');
    MultiByteToWideChar(CP_UTF8, 0, text.c_str(), -1, &out[0], len);
    out.resize(static_cast<std::size_t>(len - 1));
    return out;
}

inline std::string wideToUtf8(const std::wstring& text) {
    if (text.empty()) return std::string();
    const int len = WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, nullptr, 0, nullptr, nullptr);
    if (len <= 0) return std::string();
    std::string out(static_cast<std::size_t>(len), '\0');
    WideCharToMultiByte(CP_UTF8, 0, text.c_str(), -1, &out[0], len, nullptr, nullptr);
    out.resize(static_cast<std::size_t>(len - 1));
    return out;
}

std::wstring exeDir();

/// 删除目录下所有文件的“来自 Internet”标记（Zone.Identifier），
/// 避免 Windows 对解压出来的每个 dll/pyc 做联网信誉检查导致首次加载极慢。
int clearMarkOfTheWeb(const std::wstring& root);

}  // namespace ot
