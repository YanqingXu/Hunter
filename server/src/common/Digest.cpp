// 使用固定宽度无符号运算计算跨平台内容摘要。
#include "common/Digest.h"
#include "common/Types.h"
#include <array>
#include <bit>

namespace hunter
{
Str digest(std::string_view text)
{
    constexpr std::array<u32, 64> constants = {
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1,
        0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
        0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786,
        0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147,
        0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
        0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b,
        0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a,
        0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
        0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2};
    std::array<u32, 8> state = {0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a,
        0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19};
    Vec<u8> bytes(text.begin(), text.end());
    const u64 bits = static_cast<u64>(bytes.size()) * 8;
    bytes.push_back(0x80);

    while (bytes.size() % 64 != 56)
    {
        bytes.push_back(0);
    }

    for (i32 shift = 56; shift >= 0; shift -= 8)
    {
        bytes.push_back(static_cast<u8>(bits >> shift));
    }

    for (usize offset = 0; offset < bytes.size(); offset += 64)
    {
        std::array<u32, 64> words{};

        for (usize index = 0; index < 16; ++index)
        {
            const auto start = offset + index * 4;
            words[index] = (static_cast<u32>(bytes[start]) << 24)
                | (static_cast<u32>(bytes[start + 1]) << 16)
                | (static_cast<u32>(bytes[start + 2]) << 8) | bytes[start + 3];
        }

        for (usize index = 16; index < words.size(); ++index)
        {
            const u32 first = words[index - 15];
            const u32 second = words[index - 2];
            const u32 low = std::rotr(first, 7) ^ std::rotr(first, 18) ^ (first >> 3);
            const u32 high = std::rotr(second, 17) ^ std::rotr(second, 19) ^ (second >> 10);
            words[index] = words[index - 16] + low + words[index - 7] + high;
        }

        auto [a, b, c, d, e, f, g, h] = state;

        for (usize index = 0; index < words.size(); ++index)
        {
            const u32 high = std::rotr(e, 6) ^ std::rotr(e, 11) ^ std::rotr(e, 25);
            const u32 choose = (e & f) ^ (~e & g);
            const u32 first = h + high + choose + constants[index] + words[index];
            const u32 low = std::rotr(a, 2) ^ std::rotr(a, 13) ^ std::rotr(a, 22);
            const u32 majority = (a & b) ^ (a & c) ^ (b & c);
            h = g;
            g = f;
            f = e;
            e = d + first;
            d = c;
            c = b;
            b = a;
            a = first + low + majority;
        }

        const std::array<u32, 8> values = {a, b, c, d, e, f, g, h};

        for (usize index = 0; index < state.size(); ++index)
        {
            state[index] += values[index];
        }
    }

    constexpr char digits[] = "0123456789abcdef";
    Str result;
    result.reserve(64);

    for (const u32 value : state)
    {
        for (i32 shift = 28; shift >= 0; shift -= 4)
        {
            result.push_back(digits[(value >> shift) & 15]);
        }
    }

    return result;
}
}
