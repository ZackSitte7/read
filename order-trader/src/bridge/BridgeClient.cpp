#include "BridgeClient.h"

#include <cstdio>
#include <cstring>

#include "util/Log.h"
#include "util/Win.h"

namespace ot {

namespace {

constexpr UINT kBridgeMessageId = WM_APP + 10;

std::vector<wchar_t> buildEnvBlock() {
    std::vector<wchar_t> block;
    LPWCH env = GetEnvironmentStringsW();
    if (env) {
        const wchar_t* skip[] = {L"PYTHONIOENCODING=", L"PYTHONUTF8=", L"PYTHONUNBUFFERED="};
        for (LPWCH p = env; *p;) {
            const std::size_t len = wcslen(p);
            bool keep = true;
            for (const wchar_t* prefix : skip) {
                if (wcsncmp(p, prefix, wcslen(prefix)) == 0) {
                    keep = false;
                    break;
                }
            }
            if (keep) {
                block.insert(block.end(), p, p + len);
                block.push_back(L'\0');
            }
            p += len + 1;
        }
        FreeEnvironmentStringsW(env);
    }
    const wchar_t* extra[] = {L"PYTHONIOENCODING=utf-8", L"PYTHONUTF8=1", L"PYTHONUNBUFFERED=1"};
    for (const wchar_t* item : extra) {
        const std::size_t len = wcslen(item);
        block.insert(block.end(), item, item + len);
        block.push_back(L'\0');
    }
    block.push_back(L'\0');
    return block;
}

}  // namespace

BridgeClient::~BridgeClient() { stop(); }

bool BridgeClient::start(const std::wstring& python, const std::wstring& script,
                         const std::wstring& workDir, std::string* error) {
    stop();
    python_ = python;

    SECURITY_ATTRIBUTES sa;
    sa.nLength = sizeof(sa);
    sa.lpSecurityDescriptor = nullptr;
    sa.bInheritHandle = TRUE;

    HANDLE stdinRead = nullptr;
    HANDLE stdoutWrite = nullptr;
    if (!CreatePipe(&stdinRead, &stdinWrite_, &sa, 0)) {
        if (error) *error = "CreatePipe(stdin) failed";
        return false;
    }
    if (!CreatePipe(&stdoutRead_, &stdoutWrite, &sa, 0)) {
        if (error) *error = "CreatePipe(stdout) failed";
        CloseHandle(stdinRead);
        CloseHandle(stdinWrite_);
        stdinWrite_ = nullptr;
        return false;
    }
    SetHandleInformation(stdinWrite_, HANDLE_FLAG_INHERIT, 0);
    SetHandleInformation(stdoutRead_, HANDLE_FLAG_INHERIT, 0);

    std::wstring cmd = L"\"" + python + L"\" -u \"" + script + L"\"";
    std::vector<wchar_t> env = buildEnvBlock();

    STARTUPINFOW si;
    std::memset(&si, 0, sizeof(si));
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESTDHANDLES;
    si.hStdInput = stdinRead;
    si.hStdOutput = stdoutWrite;
    si.hStdError = stdoutWrite;

    PROCESS_INFORMATION pi;
    std::memset(&pi, 0, sizeof(pi));

    const BOOL ok = CreateProcessW(nullptr, &cmd[0], nullptr, nullptr, TRUE, CREATE_NO_WINDOW,
                                   env.data(), workDir.c_str(), &si, &pi);
    CloseHandle(stdinRead);
    CloseHandle(stdoutWrite);

    if (!ok) {
        if (error) {
            char buf[128];
            std::snprintf(buf, sizeof(buf), "CreateProcess failed (%lu)", GetLastError());
            *error = buf;
        }
        CloseHandle(stdinWrite_);
        CloseHandle(stdoutRead_);
        stdinWrite_ = nullptr;
        stdoutRead_ = nullptr;
        return false;
    }

    process_ = pi.hProcess;
    pid_ = pi.dwProcessId;
    CloseHandle(pi.hThread);

    running_ = true;
    reader_ = std::thread(&BridgeClient::readerLoop, this);
    return true;
}

void BridgeClient::stop() {
    if (stdinWrite_) {
        const char* shutdown = "{\"cmd\":\"shutdown\"}\n";
        DWORD written = 0;
        WriteFile(stdinWrite_, shutdown, static_cast<DWORD>(strlen(shutdown)), &written, nullptr);
    }
    if (process_) {
        if (WaitForSingleObject(process_, 800) == WAIT_TIMEOUT) {
            TerminateProcess(process_, 0);
            WaitForSingleObject(process_, 500);
        }
        CloseHandle(process_);
        process_ = nullptr;
    }
    if (stdinWrite_) {
        CloseHandle(stdinWrite_);
        stdinWrite_ = nullptr;
    }
    if (stdoutRead_) {
        // reader 线程阻塞在 ReadFile，关闭读端让其返回
        CancelIoEx(stdoutRead_, nullptr);
        CloseHandle(stdoutRead_);
        stdoutRead_ = nullptr;
    }
    running_ = false;
    if (reader_.joinable()) reader_.join();
    std::lock_guard<std::mutex> lock(queueMutex_);
    queue_.clear();
    pid_ = 0;
}

void BridgeClient::send(const std::string& jsonLine) {
    std::lock_guard<std::mutex> lock(writeMutex_);
    if (!stdinWrite_) return;
    std::string line = jsonLine;
    line.push_back('\n');
    std::size_t offset = 0;
    while (offset < line.size()) {
        DWORD written = 0;
        if (!WriteFile(stdinWrite_, line.data() + offset,
                       static_cast<DWORD>(line.size() - offset), &written, nullptr)) {
            return;
        }
        if (written == 0) return;
        offset += written;
    }
}

void BridgeClient::pushMessage(BridgeMessage msg, bool notify) {
    {
        std::lock_guard<std::mutex> lock(queueMutex_);
        queue_.push_back(std::move(msg));
    }
    if (notify && notify_) PostMessageW(notify_, kBridgeMessageId, 0, 0);
}

bool BridgeClient::popMessage(BridgeMessage* out) {
    std::lock_guard<std::mutex> lock(queueMutex_);
    if (queue_.empty()) return false;
    *out = std::move(queue_.front());
    queue_.erase(queue_.begin());
    return true;
}

void BridgeClient::dispatch(const std::function<void(const BridgeMessage&)>& handler) {
    std::vector<BridgeMessage> batch;
    {
        std::lock_guard<std::mutex> lock(queueMutex_);
        batch.swap(queue_);
    }
    for (const auto& msg : batch) handler(msg);
}

void BridgeClient::readerLoop() {
    std::string buffer;
    char chunk[65536];
    for (;;) {
        DWORD got = 0;
        const BOOL ok = ReadFile(stdoutRead_, chunk, sizeof(chunk), &got, nullptr);
        if (!ok || got == 0) break;
        buffer.append(chunk, got);
        for (;;) {
            const std::size_t pos = buffer.find('\n');
            if (pos == std::string::npos) break;
            std::string line = buffer.substr(0, pos);
            buffer.erase(0, pos + 1);
            if (!line.empty() && line.back() == '\r') line.pop_back();
            if (line.empty()) continue;

            std::string error;
            Json parsed = Json::parse(line, &error);
            BridgeMessage msg;
            if (parsed.isObject()) {
                msg.type = parsed.find("type") ? parsed.find("type")->str("message") : std::string("message");
                msg.data = std::move(parsed);
            } else {
                msg.type = "textlog";
                msg.data = Json(line);
                applog::write("[bridge] " + line);
            }
            pushMessage(std::move(msg), true);
        }
    }
    running_ = false;
    BridgeMessage bye;
    bye.type = "bye";
    bye.data = Json(std::string("bridge process exited"));
    pushMessage(std::move(bye), true);
}

}  // namespace ot
