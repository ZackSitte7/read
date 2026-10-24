#include "Symbol.h"

#include <cctype>
#include <cstddef>
#include <vector>

namespace ot {

namespace {

struct ExchangeRule {
    const char* exchange;
    const char* products;
    bool upperProduct;   // 品种代码是否大写（郑商所 / 中金所）
    bool dashOption;     // 期权是否使用 m2601-C-2500 形式（大商所 / 中金所）
};

const ExchangeRule kRules[] = {
    {"SHFE", "rb hc cu al zn pb ni sn au ag ss sp fu bu ru wr br", false, false},
    {"DCE", "a b c cs i j jm jd l m p pp v y eg eb pg lh bb fb rr lg", false, true},
    {"CZCE", "MA TA SR CF CY FG RM OI ZC SM SF AP CJ UR SA PF PK RS RI WH JR LR PM PX SH PR",
     true, false},
    {"CFFEX", "IF IH IC IM IO HO MO T TF TS TL", true, true},
    {"INE", "sc lu bc nr ec", false, false},
    {"GFEX", "si lc ps", false, false},
};

std::string upperText(const std::string& text) {
    std::string out;
    out.reserve(text.size());
    for (char c : text) out.push_back(static_cast<char>(std::toupper(static_cast<unsigned char>(c))));
    return out;
}

std::string lowerText(const std::string& text) {
    std::string out;
    out.reserve(text.size());
    for (char c : text) out.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
    return out;
}

bool hasProduct(const ExchangeRule& rule, const std::string& productUpper) {
    std::string token;
    const std::string list = rule.products;
    for (std::size_t i = 0; i <= list.size(); ++i) {
        if (i == list.size() || list[i] == ' ') {
            if (token == productUpper) return true;
            token.clear();
        } else {
            token.push_back(static_cast<char>(std::toupper(static_cast<unsigned char>(list[i]))));
        }
    }
    return false;
}

const ExchangeRule* ruleForExchange(const std::string& exchange) {
    const std::string wanted = upperText(exchange);
    for (const auto& rule : kRules) {
        if (wanted == rule.exchange) return &rule;
    }
    return nullptr;
}

const ExchangeRule* ruleForProduct(const std::string& productUpper) {
    for (const auto& rule : kRules) {
        if (hasProduct(rule, productUpper)) return &rule;
    }
    return nullptr;
}

}  // namespace

std::string normalizeSymbol(const std::string& raw) {
    std::string cleaned;
    for (char c : raw) {
        if (c == ' ' || c == '\t' || c == '\r' || c == '\n') continue;
        cleaned.push_back(c);
    }
    if (cleaned.empty()) return std::string();

    std::string exchange;
    std::string body = cleaned;
    const std::size_t dot = cleaned.find('.');
    if (dot != std::string::npos) {
        exchange = cleaned.substr(0, dot);
        body = cleaned.substr(dot + 1);
    }

    std::size_t alpha = 0;
    while (alpha < body.size() && std::isalpha(static_cast<unsigned char>(body[alpha]))) ++alpha;
    if (alpha == 0) return std::string();

    const std::string product = body.substr(0, alpha);
    const std::string productUpper = upperText(product);

    const ExchangeRule* rule = ruleForProduct(productUpper);
    if (exchange.empty() && rule) {
        exchange = rule->exchange;
    } else if (!exchange.empty()) {
        const ExchangeRule* named = ruleForExchange(exchange);
        if (named && hasProduct(*named, productUpper)) {
            rule = named;
            exchange = named->exchange;
        } else if (named) {
            rule = named;
            exchange = named->exchange;
        }
    }
    if (!rule) return std::string();

    std::string rest;
    for (std::size_t i = alpha; i < body.size(); ++i) {
        if (body[i] != '-') rest.push_back(body[i]);
    }

    std::size_t digits = 0;
    while (digits < rest.size() && std::isdigit(static_cast<unsigned char>(rest[digits]))) ++digits;
    if (digits == 0) return std::string();

    const std::string month = rest.substr(0, digits);
    const std::string tail = rest.substr(digits);

    char optionSide = 0;
    std::string strike;
    if (!tail.empty()) {
        const char c = static_cast<char>(std::toupper(static_cast<unsigned char>(tail[0])));
        if (c == 'C' || c == 'P') {
            optionSide = c;
            for (std::size_t i = 1; i < tail.size(); ++i) {
                const char ch = tail[i];
                if (std::isdigit(static_cast<unsigned char>(ch)) || ch == '.') strike.push_back(ch);
            }
            if (strike.empty()) optionSide = 0;
        }
    }

    const std::string productOut = rule->upperProduct ? productUpper : lowerText(productUpper);
    if (!optionSide) return exchange + "." + productOut + month;

    std::string suffix;
    suffix.push_back(optionSide);
    if (rule->dashOption) {
        return exchange + "." + productOut + month + "-" + suffix + "-" + strike;
    }
    return exchange + "." + productOut + month + suffix + strike;
}

std::string displaySymbol(const std::string& symbol) {
    const std::size_t dot = symbol.find('.');
    return dot == std::string::npos ? symbol : symbol.substr(dot + 1);
}

}  // namespace ot
