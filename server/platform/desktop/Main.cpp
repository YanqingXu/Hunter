// 桌面服务入口：独立管道线程交换有界 JSON 行，主线程负责可取消收尾。
#include "common/Types.h"
#include "core/Cfg.h"
#include "core/Runtime.h"

#include <nlohmann/json.hpp>

#include <array>
#include <atomic>
#include <chrono>
#include <charconv>
#include <cstdlib>
#include <iostream>
#include <thread>

#ifdef _WIN32
#include <windows.h>
#else
#include <cerrno>
#include <csignal>
#include <poll.h>
#include <unistd.h>
#endif

namespace
{
// 读取一块标准输入；平台取消或 EOF 返回空块。
usize read_stdin(char* data, usize size, const std::atomic<bool>& canceled)
{
#ifdef _WIN32
    DWORD count = 0;
    if (canceled || !ReadFile(GetStdHandle(STD_INPUT_HANDLE), data,
        static_cast<DWORD>(size), &count, nullptr))
    {
        return 0;
    }

    return count;
#else
    while (!canceled)
    {
        pollfd fd{STDIN_FILENO, POLLIN, 0};
        const auto ready = poll(&fd, 1, 50);
        if (ready < 0 && errno == EINTR)
        {
            continue;
        }

        if (ready > 0)
        {
            const auto count = read(STDIN_FILENO, data, size);
            return count > 0 ? static_cast<usize>(count) : 0;
        }
    }

    return 0;
#endif
}

// 将完整控制行或诊断分别写到 stdout 或 stderr，断开或取消时报告失败。
bool write_line(const Str& line, bool diagnostic, const std::atomic<bool>& canceled)
{
    usize offset = 0;

    while (offset < line.size() && !canceled)
    {
#ifdef _WIN32
        DWORD count = 0;
        const auto stream = diagnostic ? STD_ERROR_HANDLE : STD_OUTPUT_HANDLE;
        if (!WriteFile(GetStdHandle(stream), line.data() + offset,
            static_cast<DWORD>(line.size() - offset), &count, nullptr) || count == 0)
        {
            return false;
        }
#else
        const auto stream = diagnostic ? STDERR_FILENO : STDOUT_FILENO;
        pollfd fd{stream, POLLOUT, 0};
        const auto ready = poll(&fd, 1, 50);
        if (ready <= 0)
        {
            continue;
        }

        const auto count = write(stream, line.data() + offset, line.size() - offset);
        if (count <= 0)
        {
            return false;
        }
#endif
        offset += static_cast<usize>(count);
    }

    return offset == line.size();
}

// 持续读取有界行；输入错误直接请求失败停止，不在读取线程写任何管道。
void read_commands(hunter::Runtime& runtime, const hunter::Cfg& cfg,
    const std::atomic<bool>& canceled)
{
    std::array<char, 4096> data{};
    Str line;

    while (!canceled)
    {
        const auto count = read_stdin(data.data(), data.size(), canceled);
        if (count == 0)
        {
            runtime.stop();
            return;
        }

        for (usize i = 0; i < count; ++i)
        {
            if (data[i] == '\n')
            {
                if (!line.empty() && line.back() == '\r')
                {
                    line.pop_back();
                }

                if (!runtime.submit(std::move(line)))
                {
                    runtime.stop("control_input_rejected");
                    return;
                }

                line.clear();
            }
            else if (line.size() >= cfg.max_json_bytes)
            {
                runtime.stop("control_line_size");
                return;
            }
            else
            {
                line.push_back(data[i]);
            }
        }
    }
}

// 反复取消同步管道调用，覆盖检查标志与进入系统调用之间的竞争窗口。
void cancel_and_join(std::thread& thread, std::atomic<bool>& canceled,
    const std::atomic<bool>& done, u32 timeout_ms)
{
    canceled = true;
    const auto deadline = std::chrono::steady_clock::now()
        + std::chrono::milliseconds(timeout_ms);

    while (!done)
    {
        if (std::chrono::steady_clock::now() >= deadline)
        {
            // 逻辑线程已释放资源；系统未完成管道取消时终止本进程，避免无限阻塞退出。
            std::_Exit(2);
        }

#ifdef _WIN32
        CancelSynchronousIo(thread.native_handle());
#endif
        std::this_thread::sleep_for(std::chrono::milliseconds(5));
    }

    thread.join();
}

// 解析完整无符号十进制正整数，并在修改配置前检查公开上界。
u64 parse_limit(const Str& value, u64 maximum)
{
    u64 parsed = 0;
    const auto result = std::from_chars(value.data(), value.data() + value.size(), parsed);
    if (value.empty() || result.ec != std::errc{} || result.ptr != value.data() + value.size()
        || parsed == 0 || parsed > maximum)
    {
        throw std::runtime_error("invalid positive decimal limit: " + value.substr(0, 64));
    }

    return parsed;
}

// 解析离线资源路径与测试可注入限制，拒绝未知参数。
hunter::Cfg parse_cfg(i32 argc, char** argv)
{
    hunter::Cfg cfg;
#ifdef HUNTER_SCRIPT_PATH
    cfg.source_path = HUNTER_SCRIPT_PATH;
#endif

    for (i32 i = 1; i < argc; ++i)
    {
        const Str name = argv[i];

        if (i + 1 >= argc)
        {
            throw std::runtime_error("missing argument value");
        }

        const Str value = argv[++i];

        if (name == "--source")
        {
            cfg.source_path = value;
        }
        else if (name == "--bundle")
        {
            cfg.bundle_path = value;
            cfg.source_path.clear();
        }
        else if (name == "--policy")
        {
            cfg.policy_path = value;
        }
        else if (name == "--handshake-ms")
        {
            cfg.handshake_timeout_ms = static_cast<u32>(parse_limit(value, 60000));
        }
        else if (name == "--stop-ms")
        {
            cfg.stop_timeout_ms = static_cast<u32>(parse_limit(value, 60000));
        }
        else if (name == "--queue-count")
        {
            cfg.max_queue_count = static_cast<usize>(parse_limit(value, 65536));
        }
        else if (name == "--send-bytes")
        {
            cfg.max_send_bytes = static_cast<usize>(parse_limit(value, 16 * 1024 * 1024));
        }
        else
        {
            throw std::runtime_error("unknown argument: " + name);
        }
    }

    return cfg;
}
}

// 运行桌面宿主，等待逻辑线程结束后在有限等待内取消阻塞管道。
int main(int argc, char** argv)
{
    try
    {
#ifndef _WIN32
        std::signal(SIGPIPE, SIG_IGN);
#endif
        const auto cfg = parse_cfg(argc, argv);
        hunter::Runtime runtime(cfg);
        std::atomic<bool> read_canceled = false;
        std::atomic<bool> write_canceled = false;
        std::atomic<bool> read_done = false;
        std::atomic<bool> write_done = false;
        std::thread reader([&]
        {
            read_commands(runtime, cfg, read_canceled);
            read_done = true;
        });
        std::thread writer([&]
        {
            while (!write_canceled)
            {
                auto evt = runtime.next_evt(50);
                if (!evt)
                {
                    if (runtime.finished())
                    {
                        break;
                    }

                    continue;
                }

                const auto json = nlohmann::json::parse(*evt, nullptr, false);
                const bool diagnostic = !json.is_discarded()
                    && json.value("type", "") == "Diagnostic";

                if (!write_line(*evt + "\n", diagnostic, write_canceled))
                {
                    runtime.stop();
                    break;
                }
            }

            write_done = true;
        });

        runtime.join();
        cancel_and_join(reader, read_canceled, read_done, cfg.stop_timeout_ms);
        const auto deadline = std::chrono::steady_clock::now()
            + std::chrono::milliseconds(cfg.stop_timeout_ms);

        while (!write_done && std::chrono::steady_clock::now() < deadline)
        {
            std::this_thread::sleep_for(std::chrono::milliseconds(5));
        }

        cancel_and_join(writer, write_canceled, write_done, cfg.stop_timeout_ms);
        return runtime.faulted() ? 1 : 0;
    }
    catch (const std::exception& err)
    {
        std::cerr << err.what() << '\n';
        return 1;
    }
}
