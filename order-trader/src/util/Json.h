// Order Trader v1.0 - 极简 JSON 解析/构造（无第三方依赖）
#pragma once

#include <string>
#include <utility>
#include <vector>

namespace ot {

class Json {
public:
    enum class Type { Null, Bool, Number, String, Array, Object };

    Json() = default;
    explicit Json(bool v) : type_(Type::Bool), bool_(v) {}
    explicit Json(double v) : type_(Type::Number), num_(v) {}
    explicit Json(int v) : type_(Type::Number), num_(static_cast<double>(v)) {}
    explicit Json(long long v) : type_(Type::Number), num_(static_cast<double>(v)) {}
    explicit Json(const std::string& v) : type_(Type::String), str_(v) {}

    static Json parse(const std::string& text, std::string* error = nullptr);

    Type type() const { return type_; }
    bool isNull() const { return type_ == Type::Null; }
    bool isObject() const { return type_ == Type::Object; }
    bool isArray() const { return type_ == Type::Array; }
    bool isString() const { return type_ == Type::String; }
    bool isNumber() const { return type_ == Type::Number; }
    bool isBool() const { return type_ == Type::Bool; }

    bool asBool(bool def = false) const;
    double asDouble(double def = 0.0) const;
    long long asInt(long long def = 0) const;
    const std::string& asString() const;
    std::string str(const std::string& def = std::string()) const;

    std::size_t size() const;
    const std::vector<Json>& items() const { return items_; }
    const Json* find(const std::string& key) const;
    bool has(const std::string& key) const { return find(key) != nullptr; }
    const std::vector<std::pair<std::string, Json>>& members() const { return members_; }

    /// 转义为 JSON 字符串字面量（含首尾引号）
    static std::string quote(const std::string& s);
    /// 数值输出（避免科学计数法与多余小数）
    static std::string num(double v);
    std::string dump() const;

private:
    friend class JsonParser;

    Type type_ = Type::Null;
    bool bool_ = false;
    double num_ = 0.0;
    std::string str_;
    std::vector<Json> items_;
    std::vector<std::pair<std::string, Json>> members_;
};

/// 拼接 JSON 对象：值为已编码好的 JSON 片段
std::string jsonObject(const std::vector<std::pair<std::string, std::string>>& kv);

}  // namespace ot
