// Order Trader v1.0 - 合约代码规范化
#pragma once

#include <string>

namespace ot {

/// 把用户输入的短代码补全为 tqsdk 合约代码：
///   ag2612            -> SHFE.ag2612
///   ag2612c13500      -> SHFE.ag2612C13500
///   MA601             -> CZCE.MA601
///   m2601c2500        -> DCE.m2601-C-2500
/// 已带交易所前缀的代码只做大小写/期权格式校正。
std::string normalizeSymbol(const std::string& raw);

/// 去掉交易所前缀，用于输入框回显、窗口标题与列表展示。
std::string displaySymbol(const std::string& symbol);

}  // namespace ot
