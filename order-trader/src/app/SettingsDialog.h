// Order Trader v1.0 - 设置对话框（tqsdk 账号信息）
#pragma once

#include <string>

#include "util/Win.h"

namespace ot {

class SettingsDialog {
public:
    explicit SettingsDialog(HWND owner);
    ~SettingsDialog();

    /// 模态显示；返回 true 表示已保存。
    bool showModal();

    const std::string& user() const { return user_; }
    const std::string& password() const { return password_; }

private:
    static LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp);
    LRESULT handleMessage(UINT msg, WPARAM wp, LPARAM lp);

    bool create();
    void layout();
    void submit();

    HWND hwnd_ = nullptr;
    HWND owner_ = nullptr;
    HWND editUser_ = nullptr;
    HWND editPass_ = nullptr;
    HWND btnSave_ = nullptr;
    HWND btnCancel_ = nullptr;
    HBRUSH bgBrush_ = nullptr;
    HBRUSH editBrush_ = nullptr;
    bool done_ = false;
    bool saved_ = false;
    std::string user_;
    std::string password_;
};

}  // namespace ot
