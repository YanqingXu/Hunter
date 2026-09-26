// 统一定义服务端基础类型、容器、智能指针和函数对象的短类型别名。
// 为各模块提供一致的类型命名，不改变底层类型及其所有权语义。
#ifndef GAME_TYPES_H
#define GAME_TYPES_H

#include <cstddef>
#include <cstdint>
#include <vector>
#include <string>
#include <map>
#include <set>
#include <memory>
#include <unordered_map>
#include <unordered_set>
#include <functional>
#include <expected>
#include <array>
#include <optional>
#include <span>
#include <string_view>


/// 动态数组容器
template<typename T>
using Vec = std::vector<T>;

/// 哈希映射容器
template<typename K, typename V>
using HashMap = std::unordered_map<K, V>;

/// 有序映射容器
template<typename K, typename V>
using Map = std::map<K, V>;

/// 哈希集合容器
template<typename T>
using HashSet = std::unordered_set<T>;

/// 有序集合容器
template<typename T>
using Set = std::set<T>;

// 字符串类型别名
using Str = std::string;

// 只读字符串视图（不持有数据）
using StrView = std::string_view;

// 整型类型别名
using i8 = std::int8_t;
using u8 = std::uint8_t;
using i16 = std::int16_t;
using u16 = std::uint16_t;
using i32 = std::int32_t;
using u32 = std::uint32_t;
using i64 = std::int64_t;
using u64 = std::uint64_t;

// 内存大小、容器长度及索引类型
using usize = std::size_t;

// 浮点类型别名
using f32 = float;
using f64 = double;

/// 固定长度数组
template<typename T, usize N>
using Arr = std::array<T, N>;

/// 连续内存视图（不持有数据，默认动态长度）
template<typename T, usize N = std::dynamic_extent>
using Span = std::span<T, N>;

/// 可选值
template<typename T>
using Opt = std::optional<T>;

/// 共享指针（引用计数）
template<typename T>
using Ptr = std::shared_ptr<T>;

/// 弱引用指针
template<typename T>
using WPtr = std::weak_ptr<T>;

/// 独占指针（唯一所有权）
template<typename T>
using UPtr = std::unique_ptr<T>;

/// 函数对象类型
template<typename Signature>
using Func = std::function<Signature>;

/// 成功值或错误结果
template<typename T, typename E>
using Expect = std::expected<T, E>;

/// 显式错误结果
template<typename E>
using Unexpect = std::unexpected<E>;

#endif // 结束类型定义头文件保护
