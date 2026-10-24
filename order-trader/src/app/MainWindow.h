// Order Trader v1.0 - 主窗口（行情/账户/页面切换、独立 DOM 梯子窗口）
#pragma once

#include <map>
#include <memory>
#include <set>
#include <string>
#include <vector>

#include "util/Win.h"

namespace ot {

class PriceLadder;
class InfoPanel;

class MainWindow {
public:
    MainWindow();
    ~MainWindow();

    bool create(HINSTANCE inst);
    HWND hwnd() const { return hwnd_; }

    void show();
    void refresh();
    void onPageChanged();
    void recenterLadders();
    void applyLadderSize();
    void showSettings();
    void openLadder(const std::string& symbol);
    void onLadderClosed(const std::string& symbol);

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    struct ContractRow {
        std::string symbol;
        RECT row{};
        RECT button{};
    };

    void buildControls();
    void layout();
    void layoutRows();
    void rebuildLadders();
    void syncLadderVisibility();
    void openPageLadders(bool open);
    void toggleLadder(const std::string& symbol);
    void paint(HDC dc);
    void paintTopBar(HDC dc, const RECT& rc);
    void paintToolbar(HDC dc, const RECT& rc);
    void paintTabs(HDC dc, const RECT& rc);
    void paintStatus(HDC dc, const RECT& rc);
    void paintContent(HDC dc, const RECT& rc);
    void paintEmptyPage(HDC dc, const RECT& rc);
    void paintButton(HDC dc, const RECT& rc, const std::wstring& text, bool active, bool primary);
    void paintMetric(HDC dc, int right, int centerY, const wchar_t* label, const std::wstring& value,
                     COLORREF color, int* newRight);
    int hitTest(POINT pt) const;
    void addSymbolFromEdit();
    void removeSymbolFromMessage(const std::string& symbol, int page);

    HWND hwnd_ = nullptr;
    HWND editSymbol_ = nullptr;
    HBRUSH editBrush_ = nullptr;
    HFONT fontEdit_ = nullptr;
    InfoPanel* info_ = nullptr;
    std::map<std::string, std::unique_ptr<PriceLadder>> ladders_;
    std::set<std::string> openSymbols_;
    std::vector<ContractRow> rows_;

    RECT topBar_{};
    RECT toolbar_{};
    RECT tabs_{};
    RECT content_{};
    RECT status_{};
    RECT contentHeader_{};
    RECT editRect_{};
    RECT btnSettings_{};
    RECT btnLive_{};
    RECT btnReplay_{};
    RECT speed_[5]{};
    RECT btnAdd_{};
    RECT btnCancel_{};
    RECT btnReset_{};
    RECT btnOpenAll_{};
    RECT btnCloseAll_{};
    std::vector<RECT> tabRects_;

    bool inLayout_ = false;
};

}  // namespace ot
