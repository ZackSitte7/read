// Order Trader v1.0 - DOM 价格梯
#pragma once

#include <string>

#include "util/Win.h"

namespace ot {

class PriceLadder {
public:
    static void registerClass(HINSTANCE inst);

    PriceLadder(HWND owner, const std::string& symbol, int page);
    ~PriceLadder();

    HWND hwnd() const { return hwnd_; }
    const std::string& symbol() const { return symbol_; }
    int page() const { return page_; }
    void setPage(int page) { page_ = page; }
    void setVisible(bool visible);
    void activate();
    bool isVisible() const;
    void moveTo(int x, int y, int w, int h);
    void resizeTo(int w, int h);
    void resetCenter();
    void refresh();
    void requestRemove();

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    void create();
    void destroyChildren();
    void layoutEdits();
    void paint(HDC dc);
    void paintHeader(HDC dc, const RECT& client, int headerH, int stripW);
    void paintRows(HDC dc, const RECT& client, int top, int rowH, int rows, int stripW,
                   const int* colX);
    void paintFooter(HDC dc, const RECT& client, int top);

    int rowCount(int bodyH, int rowH) const { return rowH > 0 ? bodyH / rowH : 0; }
    long long anchorKey() const;
    long long keyAtRow(int row, int rows) const;
    int rowAtY(int y, int bodyTop, int rowH, int rows, long long* key) const;
    double priceOfKey(long long key) const;
    double tickSize() const;
    int priceDecs() const;

    void handleClick(int x, int y, bool rightButton);
    void beginResize(bool horizontal);
    void updateResize();
    void endResize();
    int leftQty() const;
    int rightQty() const;
    void scrollBy(int rows);
    void syncAnchor();

    HWND hwnd_ = nullptr;
    HWND owner_ = nullptr;
    HWND editLeft_ = nullptr;
    HWND editRight_ = nullptr;
    HFONT editFont_ = nullptr;
    HBRUSH editBrush_ = nullptr;
    std::string symbol_;
    int page_ = 0;

    long long anchorKey_ = 0;
    long long lastKey_ = 0;
    bool manualScroll_ = false;
    bool following_ = true;

    bool resizing_ = false;
    bool resizeWidth_ = false;
    POINT resizeOrigin_{};
    int startWidth_ = 0;
    int startHeight_ = 0;
    RECT startRect_{};
};

}  // namespace ot
