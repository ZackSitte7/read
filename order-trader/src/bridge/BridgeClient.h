// Order Trader v1.0 - tqsdk 桥接子进程客户端
#pragma once

#include <windows.h>

#include <functional>
#include <mutex>
#include <string>
#include <thread>
#include <vector>

#include "util/Json.h"

namespace ot {

struct BridgeMessage {
    std::string type;
    Json data;
};

class BridgeClient {
public:
    ~BridgeClient();

    bool start(const std::wstring& python, const std::wstring& script, const std::wstring& workDir,
               std::string* error);
    void stop();
    bool running() const { return running_; }

    void setNotifyWindow(HWND hwnd) { notify_ = hwnd; }
    void send(const std::string& jsonLine);
    /// 在 UI 线程分发已解析消息
    void dispatch(const std::function<void(const BridgeMessage&)>& handler);
    bool popMessage(BridgeMessage* out);

    const std::wstring& pythonPath() const { return python_; }

private:
    void readerLoop();
    void pushMessage(BridgeMessage msg, bool notify);

    HANDLE stdinWrite_ = nullptr;
    HANDLE stdoutRead_ = nullptr;
    HANDLE process_ = nullptr;
    DWORD pid_ = 0;
    std::wstring python_;
    HWND notify_ = nullptr;
    volatile bool running_ = false;
    std::thread reader_;
    std::mutex writeMutex_;
    std::mutex queueMutex_;
    std::vector<BridgeMessage> queue_;
};

}  // namespace ot
