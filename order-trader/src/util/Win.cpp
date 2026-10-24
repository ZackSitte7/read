#include "Win.h"

#include <vector>

namespace ot {

namespace {

constexpr std::size_t kMaxFiles = 60000;

void unblockTree(const std::wstring& dir, int* removed, std::size_t* visited, int depth) {
    if (depth > 12 || *visited >= kMaxFiles) return;
    WIN32_FIND_DATAW data;
    const std::wstring pattern = dir + L"\\*";
    HANDLE find = FindFirstFileW(pattern.c_str(), &data);
    if (find == INVALID_HANDLE_VALUE) return;
    do {
        if (wcscmp(data.cFileName, L".") == 0 || wcscmp(data.cFileName, L"..") == 0) continue;
        const std::wstring path = dir + L"\\" + data.cFileName;
        if (data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
            unblockTree(path, removed, visited, depth + 1);
            continue;
        }
        ++*visited;
        if (DeleteFileW((path + L":Zone.Identifier").c_str())) ++*removed;
    } while (FindNextFileW(find, &data) && *visited < kMaxFiles);
    FindClose(find);
}

}  // namespace

std::wstring exeDir() {
    wchar_t buf[MAX_PATH * 2] = {0};
    GetModuleFileNameW(nullptr, buf, static_cast<DWORD>(std::size(buf)));
    std::wstring path(buf);
    const std::size_t pos = path.find_last_of(L"\\/");
    if (pos == std::wstring::npos) return L".";
    return path.substr(0, pos);
}

int clearMarkOfTheWeb(const std::wstring& root) {
    int removed = 0;
    std::size_t visited = 0;
    unblockTree(root, &removed, &visited, 0);
    return removed;
}

}  // namespace ot
