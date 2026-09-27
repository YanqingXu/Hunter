// 装配已编译后端和默认门面入口；公共异步实现不依赖 SQLite。
#include "common/Types.h"
#include "storage/Backend.h"
#include "storage/SqliteBackend.h"
#include "storage/Storage.h"

namespace hunter::storage
{

UPtr<Backend> make_backend(const OpenCfg& cfg)
{
    if (cfg.backend == "sqlite")
    {
        return std::make_unique<SqliteBackend>();
    }

    throw Error{Code::Unsupported, 0, false, "storage_backend_unsupported", cfg.backend};
}

Storage::Storage(asio::io_context& io, StorageCfg cfg, u64 instance)
    : Storage(io, cfg, instance, make_backend)
{
}

}
