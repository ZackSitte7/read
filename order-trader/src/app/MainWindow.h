// Order Trader v1.0 - 主窗口（行情/账户/多页 DOM 梯子）
#pragma once

#include <memory>
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

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    void buildControls();
    void layout();
    void relayoutLadders();
    void rebuildLadders();
    void updateScrollBar();
    void paint(HDC dc);
    void paintTopBar(HDC dc, const RECT& rc);
    void paintToolbar(HDC dc, const RECT& rc);
    void paintTabs(HDC dc, const RECT& rc);
    void paintStatus(HDC dc, const RECT& rc);
    void paintEmptyPage(HDC dc, const RECT& rc);
    void paintButton(HDC dc, const RECT& rc, const std::wstring& text, bool active, bool primary);
    void paintMetric(HDC dc, int right, int centerY, const wchar_t* label, const std::wstring& value,
                     COLORREF color, int* newRight);
    int hitTest(POINT pt) const;
    void addSymbolFromEdit();
    void removeLadder(HWND ladderHwnd);

    HWND hwnd_ = nullptr;
    HWND editSymbol_ = nullptr;
    HBRUSH editBrush_ = nullptr;
    HFONT fontEdit_ = nullptr;
    InfoPanel* info_ = nullptr;
    std::vector<std::unique_ptr<PriceLadder>> ladders_;

    RECT topBar_{};
    RECT toolbar_{};
    RECT tabs_{};
    RECT content_{};
    RECT status_{};
    RECT editRect_{};
    RECT btnLive_{};
    RECT btnReplay_{};
    RECT speed_[5]{};
    RECT btnAdd_{};
    RECT btnCancel_{};
    RECT btnReset_{};
    std::vector<RECT> tabRects_;

    int scrollX_ = 0;
    int maxScroll_ = 0;
    bool scrollVisible_ = false;
    bool inLayout_ = false;
    int ladderCount_ = 0;
};

}  // namespace ot
