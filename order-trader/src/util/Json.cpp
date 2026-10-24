#include "Json.h"

#include <cmath>
#include <cstdio>
#include <cstdlib>
#include <cstring>

namespace ot {

class JsonParser {
public:
    JsonParser(const std::string& text) : s_(text) {}

    bool run(Json& out, std::string* error) {
        skipWs();
        if (!parseValue(out)) {
            if (error) *error = error_.empty() ? "invalid json" : error_;
            return false;
        }
        skipWs();
        if (pos_ != s_.size()) {
            if (error) *error = "trailing characters";
            return false;
        }
        return true;
    }

private:
    bool parseValue(Json& v) {
        if (depth_ > 64) return fail("too deep");
        if (pos_ >= s_.size()) return fail("unexpected end");
        const char c = s_[pos_];
        switch (c) {
            case '{': return parseObject(v);
            case '[': return parseArray(v);
            case '"': {
                std::string text;
                if (!parseString(text)) return false;
                v.type_ = Json::Type::String;
                v.str_ = std::move(text);
                return true;
            }
            case 't':
                if (s_.compare(pos_, 4, "true") == 0) {
                    pos_ += 4;
                    v.type_ = Json::Type::Bool;
                    v.bool_ = true;
                    return true;
                }
                return fail("bad literal");
            case 'f':
                if (s_.compare(pos_, 5, "false") == 0) {
                    pos_ += 5;
                    v.type_ = Json::Type::Bool;
                    v.bool_ = false;
                    return true;
                }
                return fail("bad literal");
            case 'n':
                if (s_.compare(pos_, 4, "null") == 0) {
                    pos_ += 4;
                    v.type_ = Json::Type::Null;
                    return true;
                }
                return fail("bad literal");
            default: return parseNumber(v);
        }
    }

    bool parseObject(Json& v) {
        ++pos_;  // {
        ++depth_;
        v.type_ = Json::Type::Object;
        skipWs();
        if (pos_ < s_.size() && s_[pos_] == '}') {
            ++pos_;
            --depth_;
            return true;
        }
        for (;;) {
            skipWs();
            if (pos_ >= s_.size() || s_[pos_] != '"') return fail("expected key");
            std::string key;
            if (!parseString(key)) return false;
            skipWs();
            if (pos_ >= s_.size() || s_[pos_] != ':') return fail("expected ':'");
            ++pos_;
            skipWs();
            Json child;
            if (!parseValue(child)) return false;
            v.members_.emplace_back(std::move(key), std::move(child));
            skipWs();
            if (pos_ >= s_.size()) return fail("unterminated object");
            if (s_[pos_] == ',') {
                ++pos_;
                continue;
            }
            if (s_[pos_] == '}') {
                ++pos_;
                --depth_;
                return true;
            }
            return fail("expected ',' or '}'");
        }
    }

    bool parseArray(Json& v) {
        ++pos_;  // [
        ++depth_;
        v.type_ = Json::Type::Array;
        skipWs();
        if (pos_ < s_.size() && s_[pos_] == ']') {
            ++pos_;
            --depth_;
            return true;
        }
        for (;;) {
            skipWs();
            Json child;
            if (!parseValue(child)) return false;
            v.items_.push_back(std::move(child));
            skipWs();
            if (pos_ >= s_.size()) return fail("unterminated array");
            if (s_[pos_] == ',') {
                ++pos_;
                continue;
            }
            if (s_[pos_] == ']') {
                ++pos_;
                --depth_;
                return true;
            }
            return fail("expected ',' or ']'");
        }
    }

    bool parseString(std::string& out) {
        ++pos_;  // "
        out.clear();
        while (pos_ < s_.size()) {
            const unsigned char c = static_cast<unsigned char>(s_[pos_]);
            if (c == '"') {
                ++pos_;
                return true;
            }
            if (c == '\\') {
                ++pos_;
                if (pos_ >= s_.size()) return fail("bad escape");
                const char e = s_[pos_++];
                switch (e) {
                    case '"': out.push_back('"'); break;
                    case '\\': out.push_back('\\'); break;
                    case '/': out.push_back('/'); break;
                    case 'b': out.push_back('\b'); break;
                    case 'f': out.push_back('\f'); break;
                    case 'n': out.push_back('\n'); break;
                    case 'r': out.push_back('\r'); break;
                    case 't': out.push_back('\t'); break;
                    case 'u': {
                        unsigned cp = 0;
                        if (!readHex4(cp)) return false;
                        if (cp >= 0xD800 && cp <= 0xDBFF && pos_ + 1 < s_.size() &&
                            s_[pos_] == '\\' && s_[pos_ + 1] == 'u') {
                            const std::size_t save = pos_;
                            pos_ += 2;
                            unsigned low = 0;
                            if (!readHex4(low)) return false;
                            if (low >= 0xDC00 && low <= 0xDFFF) {
                                cp = 0x10000 + ((cp - 0xD800) << 10) + (low - 0xDC00);
                            } else {
                                pos_ = save;
                            }
                        }
                        appendUtf8(out, cp);
                        break;
                    }
                    default: return fail("bad escape");
                }
                continue;
            }
            if (c < 0x20) return fail("control char in string");
            out.push_back(static_cast<char>(c));
            ++pos_;
        }
        return fail("unterminated string");
    }

    bool readHex4(unsigned& out) {
        if (pos_ + 4 > s_.size()) return fail("bad \\u");
        out = 0;
        for (int i = 0; i < 4; ++i) {
            const char c = s_[pos_++];
            out <<= 4;
            if (c >= '0' && c <= '9') out |= static_cast<unsigned>(c - '0');
            else if (c >= 'a' && c <= 'f') out |= static_cast<unsigned>(c - 'a' + 10);
            else if (c >= 'A' && c <= 'F') out |= static_cast<unsigned>(c - 'A' + 10);
            else return fail("bad hex");
        }
        return true;
    }

    static void appendUtf8(std::string& out, unsigned cp) {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }

    bool parseNumber(Json& v) {
        const std::size_t start = pos_;
        if (pos_ < s_.size() && (s_[pos_] == '-' || s_[pos_] == '+')) ++pos_;
        bool any = false;
        while (pos_ < s_.size() && std::isdigit(static_cast<unsigned char>(s_[pos_]))) {
            ++pos_;
            any = true;
        }
        if (pos_ < s_.size() && s_[pos_] == '.') {
            ++pos_;
            while (pos_ < s_.size() && std::isdigit(static_cast<unsigned char>(s_[pos_]))) {
                ++pos_;
                any = true;
            }
        }
        if (any && pos_ < s_.size() && (s_[pos_] == 'e' || s_[pos_] == 'E')) {
            ++pos_;
            if (pos_ < s_.size() && (s_[pos_] == '-' || s_[pos_] == '+')) ++pos_;
            while (pos_ < s_.size() && std::isdigit(static_cast<unsigned char>(s_[pos_]))) ++pos_;
        }
        if (!any) return fail("invalid value");
        v.type_ = Json::Type::Number;
        v.num_ = std::strtod(s_.substr(start, pos_ - start).c_str(), nullptr);
        return true;
    }

    void skipWs() {
        while (pos_ < s_.size()) {
            const char c = s_[pos_];
            if (c == ' ' || c == '\t' || c == '\n' || c == '\r') ++pos_;
            else break;
        }
    }

    bool fail(const char* message) {
        if (error_.empty()) {
            char buf[128];
            std::snprintf(buf, sizeof(buf), "%s at %zu", message, pos_);
            error_ = buf;
        }
        return false;
    }

    const std::string& s_;
    std::size_t pos_ = 0;
    int depth_ = 0;
    std::string error_;
};

Json Json::parse(const std::string& text, std::string* error) {
    Json out;
    JsonParser parser(text);
    if (!parser.run(out, error)) return Json();
    return out;
}

bool Json::asBool(bool def) const { return type_ == Type::Bool ? bool_ : def; }

double Json::asDouble(double def) const {
    if (type_ == Type::Number) return num_;
    if (type_ == Type::String) return std::strtod(str_.c_str(), nullptr);
    if (type_ == Type::Bool) return bool_ ? 1.0 : 0.0;
    return def;
}

long long Json::asInt(long long def) const {
    if (type_ == Type::Number) return static_cast<long long>(std::llround(num_));
    if (type_ == Type::String) return std::strtoll(str_.c_str(), nullptr, 10);
    return def;
}

const std::string& Json::asString() const {
    static const std::string kEmpty;
    return type_ == Type::String ? str_ : kEmpty;
}

std::string Json::str(const std::string& def) const {
    if (type_ == Type::String) return str_;
    if (type_ == Type::Number) return num(num_);
    if (type_ == Type::Bool) return bool_ ? "true" : "false";
    return def;
}

std::size_t Json::size() const {
    if (type_ == Type::Array) return items_.size();
    if (type_ == Type::Object) return members_.size();
    return 0;
}

const Json* Json::find(const std::string& key) const {
    if (type_ != Type::Object) return nullptr;
    for (const auto& kv : members_) {
        if (kv.first == key) return &kv.second;
    }
    return nullptr;
}

std::string Json::quote(const std::string& s) {
    std::string out;
    out.reserve(s.size() + 2);
    out.push_back('"');
    for (const char ch : s) {
        const unsigned char c = static_cast<unsigned char>(ch);
        switch (c) {
            case '"': out += "\\\""; break;
            case '\\': out += "\\\\"; break;
            case '\b': out += "\\b"; break;
            case '\f': out += "\\f"; break;
            case '\n': out += "\\n"; break;
            case '\r': out += "\\r"; break;
            case '\t': out += "\\t"; break;
            default:
                if (c < 0x20) {
                    char buf[8];
                    std::snprintf(buf, sizeof(buf), "\\u%04x", c);
                    out += buf;
                } else {
                    out.push_back(ch);
                }
                break;
        }
    }
    out.push_back('"');
    return out;
}

std::string Json::num(double v) {
    if (!std::isfinite(v)) return "0";
    char buf[64];
    const double rounded = std::floor(v + 0.5);
    if (std::fabs(v - rounded) < 1e-9 && std::fabs(v) < 1e15) {
        std::snprintf(buf, sizeof(buf), "%.0f", rounded);
    } else {
        std::snprintf(buf, sizeof(buf), "%.10g", v);
    }
    return buf;
}

std::string Json::dump() const {
    switch (type_) {
        case Type::Null: return "null";
        case Type::Bool: return bool_ ? "true" : "false";
        case Type::Number: return num(num_);
        case Type::String: return quote(str_);
        case Type::Array: {
            std::string out = "[";
            for (std::size_t i = 0; i < items_.size(); ++i) {
                if (i) out.push_back(',');
                out += items_[i].dump();
            }
            out.push_back(']');
            return out;
        }
        case Type::Object: {
            std::string out = "{";
            for (std::size_t i = 0; i < members_.size(); ++i) {
                if (i) out.push_back(',');
                out += quote(members_[i].first);
                out.push_back(':');
                out += members_[i].second.dump();
            }
            out.push_back('}');
            return out;
        }
    }
    return "null";
}

std::string jsonObject(const std::vector<std::pair<std::string, std::string>>& kv) {
    std::string out = "{";
    bool first = true;
    for (const auto& item : kv) {
        if (!first) out.push_back(',');
        first = false;
        out += Json::quote(item.first);
        out.push_back(':');
        out += item.second;
    }
    out.push_back('}');
    return out;
}

}  // namespace ot
