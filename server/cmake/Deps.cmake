# 锁定第三方源码身份；本地覆盖必须来自干净且提交一致的检出。
include(FetchContent)
find_package(Git REQUIRED)

# 验证显式源码覆盖，避免把开发目录误报为锁定依赖。
function(hunter_check_source path revision)
    execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${path}" rev-parse HEAD
        OUTPUT_VARIABLE actual OUTPUT_STRIP_TRAILING_WHITESPACE RESULT_VARIABLE result)
    execute_process(COMMAND "${GIT_EXECUTABLE}" -C "${path}" status --porcelain
        OUTPUT_VARIABLE changes OUTPUT_STRIP_TRAILING_WHITESPACE RESULT_VARIABLE status_result)
    if(NOT result EQUAL 0 OR NOT status_result EQUAL 0 OR
       NOT actual STREQUAL revision OR NOT changes STREQUAL "")
        message(FATAL_ERROR "Dependency override must be clean at ${revision}: ${path}")
    endif()
endfunction()

# 声明带摘要的归档，优先复用已经下载并校验的本地缓存。
function(hunter_archive name repo revision digest)
    string(TOUPPER "${name}" upper)
    if(FETCHCONTENT_SOURCE_DIR_${upper})
        hunter_check_source("${FETCHCONTENT_SOURCE_DIR_${upper}}" "${revision}")
    endif()
    set(url "https://codeload.github.com/${repo}/tar.gz/${revision}")
    if(EXISTS "${HUNTER_DOWNLOAD_DIR}/${name}.tar.gz")
        set(url "${HUNTER_DOWNLOAD_DIR}/${name}.tar.gz")
    endif()
    FetchContent_Declare(${name} URL "${url}" URL_HASH "SHA256=${digest}"
        DOWNLOAD_EXTRACT_TIMESTAMP TRUE EXCLUDE_FROM_ALL)
endfunction()

set(HUNTER_DOWNLOAD_DIR "${CMAKE_CURRENT_SOURCE_DIR}/build/_downloads"
    CACHE PATH "已下载依赖归档的缓存目录")
set(HUNTER_LUAX_PIN d8a8160420074f8e590d94910d1af5a5f5a93245)
if(FETCHCONTENT_SOURCE_DIR_LUAX)
    hunter_check_source("${FETCHCONTENT_SOURCE_DIR_LUAX}" "${HUNTER_LUAX_PIN}")
endif()
FetchContent_Declare(luax GIT_REPOSITORY https://github.com/YanqingXu/luax.git
    GIT_TAG ${HUNTER_LUAX_PIN} GIT_SUBMODULES "" EXCLUDE_FROM_ALL)
hunter_archive(asio chriskohlhoff/asio 231cb29bab30f82712fcd54faaea42424cc6e710
    5def09efbd4be199dd6ddca53a2c99b9eef696f6b430910d896594b04ff59108)
hunter_archive(json nlohmann/json 55f93686c01528224f448c19128836e7df245f72
    67f4cdd9ca930c9c1e130af4a437c7fc98fab77a2846fc2d2a14b4943831f8ef)
hunter_archive(absl abseil/abseil-cpp 76bb24329e8bf5f39704eb10d21b9a80befa7c81
    ed8f7d9f39139c449e79fd19765e23c96fdb774172d32d191323d3e3ea06e5ff)
hunter_archive(protobuf protocolbuffers/protobuf a79f2d2e9fadd75e94f3fe40a0399bf0a5d90551
    c7133a9697936a2efaa2ebaaf5d4d9bb8bc12321f15f1981291317e529650f29)
set(LUA_CPP_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LUA_CPP_BUILD_TOOLS ${HUNTER_BUILD_TOOLS} CACHE BOOL "" FORCE)
set(LUA_CPP_BUILD_FUZZERS OFF CACHE BOOL "" FORCE)
set(LUAX_PACKAGE_SOURCE_REVISION ${HUNTER_LUAX_PIN} CACHE STRING "" FORCE)
set(JSON_BuildTests OFF CACHE BOOL "" FORCE)
set(JSON_Install OFF CACHE BOOL "" FORCE)
set(ABSL_PROPAGATE_CXX_STD ON CACHE BOOL "" FORCE)
set(ABSL_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(protobuf_INSTALL OFF CACHE BOOL "" FORCE)
set(protobuf_WITH_ZLIB OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(protobuf_MSVC_STATIC_RUNTIME OFF CACHE BOOL "" FORCE)
set(protobuf_BUILD_PROTOC_BINARIES ${HUNTER_BUILD_TOOLS} CACHE BOOL "" FORCE)
set(protobuf_BUILD_LIBPROTOC ${HUNTER_BUILD_TOOLS} CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(absl json asio protobuf luax)

# SQLite 使用官方固定 amalgamation，源码覆盖也验证实际文件摘要。
set(hunter_sqlite_url "https://www.sqlite.org/2026/sqlite-amalgamation-3530400.zip")
if(EXISTS "${HUNTER_DOWNLOAD_DIR}/sqlite-amalgamation-3530400.zip")
    set(hunter_sqlite_url "${HUNTER_DOWNLOAD_DIR}/sqlite-amalgamation-3530400.zip")
endif()
FetchContent_Declare(sqlite URL "${hunter_sqlite_url}"
    URL_HASH SHA256=1e71ddf93849c6a6ecf58b827c0692073d2dd7ee40196158068f7b29f422e87d
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE EXCLUDE_FROM_ALL)
FetchContent_MakeAvailable(sqlite)
file(SHA256 "${sqlite_SOURCE_DIR}/sqlite3.c" hunter_sqlite_c_hash)
file(SHA256 "${sqlite_SOURCE_DIR}/sqlite3.h" hunter_sqlite_h_hash)
if(NOT hunter_sqlite_c_hash STREQUAL "b1dd5d74ec7f29055a6684fa06fb3c2f6821c87dd38f9a458dfd2e8a1db28189" OR
   NOT hunter_sqlite_h_hash STREQUAL "919e7f2e8ed1d8f56ac17b412b8971c76aa5d1a879752cc6058f75e7d5910e1d")
    message(FATAL_ERROR "SQLite sources must match the pinned 3.53.4 amalgamation")
endif()
add_library(hunter_sqlite STATIC "${sqlite_SOURCE_DIR}/sqlite3.c")
target_include_directories(hunter_sqlite SYSTEM PUBLIC "${sqlite_SOURCE_DIR}")
target_compile_definitions(hunter_sqlite PRIVATE SQLITE_THREADSAFE=1 SQLITE_OMIT_LOAD_EXTENSION)
target_link_libraries(hunter_sqlite PRIVATE Threads::Threads)

add_library(hunter_asio INTERFACE)
target_include_directories(hunter_asio SYSTEM INTERFACE "${asio_SOURCE_DIR}/asio/include")
target_compile_definitions(hunter_asio INTERFACE ASIO_STANDALONE ASIO_NO_DEPRECATED)
if(WIN32)
    target_compile_definitions(hunter_asio INTERFACE _WIN32_WINNT=0x0A00 WIN32_LEAN_AND_MEAN NOMINMAX)
    target_link_libraries(hunter_asio INTERFACE ws2_32 mswsock)
endif()
