// 提供按数量和字节同时限额的拥有型队列；线程同步由调用者负责。
#pragma once

#include "common/Types.h"

#include <deque>
#include <optional>
#include <utility>

namespace hunter
{
template<typename T>
class BoundedQueue
{
public:
    // 创建固定容量队列；大小参数表达拥有数据的计费字节数。
    BoundedQueue(usize max_count, usize max_bytes)
        : max_count_(max_count), max_bytes_(max_bytes)
    {
    }

    // 容量不足时不改变队列；成功时转移值的所有权。
    bool push(T value, usize bytes)
    {
        if (items_.size() >= max_count_ || bytes > max_bytes_ - bytes_)
        {
            return false;
        }

        items_.emplace_back(std::move(value), bytes);
        bytes_ += bytes;
        return true;
    }

    // 移出最早值并归还其容量；空队列返回空值。
    Opt<T> pop()
    {
        if (items_.empty())
        {
            return std::nullopt;
        }

        auto item = std::move(items_.front());
        items_.pop_front();
        bytes_ -= item.second;
        return std::move(item.first);
    }

    // 丢弃全部拥有值并归还容量。
    void clear()
    {
        items_.clear();
        bytes_ = 0;
    }

    // 返回当前元素数量。
    usize size() const
    {
        return items_.size();
    }

    // 返回当前计费字节数。
    usize bytes() const
    {
        return bytes_;
    }

private:
    std::deque<std::pair<T, usize>> items_;
    usize bytes_ = 0;
    usize max_count_;
    usize max_bytes_;
};
}
