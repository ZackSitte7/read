// Order Trader v1.0 - 日志
#pragma once

#include <string>

namespace ot {
namespace applog {

void init(const std::wstring& dir);
void write(const std::string& line);
std::wstring filePath();

}  // namespace applog
}  // namespace ot
