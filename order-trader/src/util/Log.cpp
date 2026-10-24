#include "Log.h"

#include <windows.h>

#include <cstdarg>
#include <cstdio>
#include <mutex>
#include <string>

#include "Win.h"

namespace ot {
namespace applog {

namespace {
std::mutex g_mutex;
std::wstring g_path;
FILE* g_file = nullptr;
}

void init(const std::wstring& dir) {
    std::lock_guard<std::mutex> lock(g_mutex);
    CreateDirectoryW(dir.c_str(), nullptr);
    SYSTEMTIME st;
    GetLocalTime(&st);
    wchar_t name[128];
    wsprintfW(name, L"order-trader-%04d%02d%02d.log", st.wYear, st.wMonth, st.wDay);
    g_path = dir + L"\\" + name;
    if (g_file) {
        std::fclose(g_file);
        g_file = nullptr;
    }
    g_file = _wfopen(g_path.c_str(), L"ab");
}

void write(const std::string& line) {
    std::lock_guard<std::mutex> lock(g_mutex);
    SYSTEMTIME st;
    GetLocalTime(&st);
    char stamp[64];
    std::snprintf(stamp, sizeof(stamp), "%02d:%02d:%02d.%03d ", st.wHour, st.wMinute,
                  st.wSecond, st.wMilliseconds);
    if (g_file) {
        std::fprintf(g_file, "%s%s\n", stamp, line.c_str());
        std::fflush(g_file);
    }
}

std::wstring filePath() {
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_path;
}

}  // namespace applog
}  // namespace ot
