# 装配共用服务端、桌面宿主、脚本工具及可复现的契约测试。
cmake_minimum_required(VERSION 3.28)
project(HunterServer VERSION 0.1.0 LANGUAGES CXX)
file(MAKE_DIRECTORY "${CMAKE_BINARY_DIR}/.cmake/api/v1/query")
file(WRITE "${CMAKE_BINARY_DIR}/.cmake/api/v1/query/codemodel-v2" "")
set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
option(HUNTER_PRODUCTION "仅允许签名 Bundle 的运行时" OFF)
option(HUNTER_BUILD_TOOLS "构建本机 protoc 和 Luax 离线工具" ON)
option(HUNTER_TOOLS_ONLY "仅准备本机构建工具" OFF)
include(CTest)
find_package(Python3 3.10 REQUIRED COMPONENTS Interpreter)
find_package(Threads REQUIRED)
include(CheckCXXSourceCompiles)
include(CMakePushCheckState)
cmake_push_check_state(RESET)
set(CMAKE_REQUIRED_INCLUDES "${CMAKE_CURRENT_SOURCE_DIR}/src")
if(MSVC)
    set(CMAKE_REQUIRED_FLAGS "/utf-8")
endif()
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/cmake/CheckCxx.cpp" hunter_probe_source)
check_cxx_source_compiles("${hunter_probe_source}" HUNTER_HAS_CXX23_TYPES)
cmake_pop_check_state()
if(NOT HUNTER_HAS_CXX23_TYPES)
    message(FATAL_ERROR "C++23 std::expected and std::jthread are required")
endif()
if(CMAKE_CROSSCOMPILING AND HUNTER_BUILD_TOOLS)
    message(FATAL_ERROR "Cross builds require HUNTER_BUILD_TOOLS=OFF and HUNTER_HOST_PROTOC")
endif()
include(cmake/Deps.cmake)
if(HUNTER_BUILD_TOOLS)
    add_custom_target(hunter_tools DEPENDS protoc luax_compiler_tool luax_bundle_tool)
endif()
if(HUNTER_TOOLS_ONLY)
    return()
endif()

# 为项目自有目标应用告警、字符集和直接头文件依赖。
function(hunter_target target)
    target_include_directories(${target} PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/src")
    if(MSVC)
        target_compile_options(${target} PRIVATE /W4 /permissive- /utf-8 /FS)
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic -Wconversion)
    endif()
endfunction()

set(HUNTER_GEN "${CMAKE_CURRENT_BINARY_DIR}/generated")
file(MAKE_DIRECTORY "${HUNTER_GEN}" "${HUNTER_GEN}/csharp")
add_custom_command(OUTPUT "${HUNTER_GEN}/content.json" "${HUNTER_GEN}/ContentSpec.h"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/../export/combat.py"
        --source "${CMAKE_CURRENT_SOURCE_DIR}/../design/combat_demo.json"
        --output "${HUNTER_GEN}/content.json" --header "${HUNTER_GEN}/ContentSpec.h"
    DEPENDS ../design/combat_demo.json ../export/combat.py tools/publish.py VERBATIM)
add_custom_target(hunter_content ALL DEPENDS
    "${HUNTER_GEN}/content.json" "${HUNTER_GEN}/ContentSpec.h")
set(HUNTER_PROTO "${CMAKE_CURRENT_SOURCE_DIR}/../protobuf/hunter.proto")
if(HUNTER_BUILD_TOOLS)
    set(HUNTER_PROTOC "$<TARGET_FILE:protoc>")
    set(HUNTER_PROTOC_DEP protoc)
else()
    set(HUNTER_HOST_PROTOC "" CACHE FILEPATH "与固定 Protobuf 版本一致的本机 protoc")
    if(NOT EXISTS "${HUNTER_HOST_PROTOC}")
        message(FATAL_ERROR "Set HUNTER_HOST_PROTOC to a native protoc 33.0")
    endif()
    execute_process(COMMAND "${HUNTER_HOST_PROTOC}" --version
        OUTPUT_VARIABLE protoc_version OUTPUT_STRIP_TRAILING_WHITESPACE)
    if(NOT protoc_version STREQUAL "libprotoc 33.0")
        message(FATAL_ERROR "Expected libprotoc 33.0, got ${protoc_version}")
    endif()
    set(HUNTER_PROTOC "${HUNTER_HOST_PROTOC}")
    set(HUNTER_PROTOC_DEP "${HUNTER_HOST_PROTOC}")
endif()
add_custom_command(OUTPUT "${HUNTER_GEN}/hunter.pb.cc" "${HUNTER_GEN}/hunter.pb.h"
    "${HUNTER_GEN}/csharp/Hunter.cs"
    COMMAND ${HUNTER_PROTOC} --proto_path=${CMAKE_CURRENT_SOURCE_DIR}/../protobuf
        --cpp_out=${HUNTER_GEN} --csharp_out=${HUNTER_GEN}/csharp "${HUNTER_PROTO}"
    DEPENDS "${HUNTER_PROTO}" ${HUNTER_PROTOC_DEP} VERBATIM)
add_library(hunter_protocol STATIC "${HUNTER_GEN}/hunter.pb.cc")
target_include_directories(hunter_protocol SYSTEM PUBLIC "${HUNTER_GEN}")
# 固定版本的公开解析头引用 utf8_validity，上游只把该依赖声明为 PRIVATE。
target_link_libraries(hunter_protocol PUBLIC protobuf::libprotobuf-lite utf8_validity)
add_custom_target(hunter_proto_csharp DEPENDS "${HUNTER_GEN}/csharp/Hunter.cs")

file(GLOB_RECURSE HUNTER_LUA CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/lua/*.lua")
add_custom_command(OUTPUT "${HUNTER_GEN}/game.lua" "${HUNTER_GEN}/game.map.json"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/tools/assemble.py"
        --manifest "${CMAKE_CURRENT_SOURCE_DIR}/lua/modules.json"
        --output "${HUNTER_GEN}/game.lua" --map "${HUNTER_GEN}/game.map.json"
    DEPENDS ${HUNTER_LUA} lua/modules.json tools/assemble.py tools/publish.py VERBATIM)
add_custom_target(hunter_scripts ALL DEPENDS "${HUNTER_GEN}/game.lua")
add_custom_command(OUTPUT "${HUNTER_GEN}/SchemaSpec.h"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/tools/gen_schema.py"
        --contract "${CMAKE_CURRENT_SOURCE_DIR}/lua/contract.json"
        --output "${HUNTER_GEN}/SchemaSpec.h"
    DEPENDS lua/contract.json tools/gen_schema.py VERBATIM)
add_library(hunter_script STATIC src/script/Script.cpp src/script/Async.cpp
    src/script/Objects.cpp src/game/Value.cpp src/game/Entity.cpp src/game/Unit.cpp
    src/game/Player.cpp src/game/Monster.cpp src/game/Weapon.cpp src/game/Item.cpp
    src/game/World.cpp src/game/State.cpp src/game/Snapshot.cpp
    src/script/Schema.cpp "${HUNTER_GEN}/SchemaSpec.h")
add_dependencies(hunter_script hunter_content)
hunter_target(hunter_script)
target_include_directories(hunter_script SYSTEM PUBLIC "${HUNTER_GEN}")
target_compile_definitions(hunter_script PUBLIC HUNTER_PRODUCTION=$<BOOL:${HUNTER_PRODUCTION}>)
target_link_libraries(hunter_script PUBLIC hunter_asio nlohmann_json::nlohmann_json
    Luax::Bind Threads::Threads hunter_protocol)
if(HUNTER_PRODUCTION)
    target_link_libraries(hunter_script PUBLIC Luax::Runtime)
else()
    target_link_libraries(hunter_script PUBLIC Luax::DevelopmentRuntime)
endif()
add_library(hunter_server_core STATIC src/core/Runtime.cpp src/core/TickClock.cpp
    src/net/Protocol.cpp src/net/Transport.cpp)
hunter_target(hunter_server_core)
target_link_libraries(hunter_server_core PUBLIC hunter_script hunter_protocol)
if(WIN32)
    target_link_libraries(hunter_server_core PRIVATE bcrypt)
    add_executable(hunter_server_desktop platform/desktop/Main.cpp)
    hunter_target(hunter_server_desktop)
    target_link_libraries(hunter_server_desktop PRIVATE hunter_server_core)
    target_compile_definitions(hunter_server_desktop PRIVATE
        HUNTER_SCRIPT_PATH="${HUNTER_GEN}/game.lua")
    add_dependencies(hunter_server_desktop hunter_scripts)
    install(TARGETS hunter_server_desktop RUNTIME DESTINATION bin)
    add_executable(hunter_client tools/Client.cpp)
    hunter_target(hunter_client)
    target_link_libraries(hunter_client PRIVATE hunter_server_core)
endif()
if(ANDROID)
    add_executable(hunter_android_probe platform/android/Probe.cpp)
    hunter_target(hunter_android_probe)
    target_link_libraries(hunter_android_probe PRIVATE hunter_server_core)
    target_link_options(hunter_android_probe PRIVATE "-Wl,-z,max-page-size=16384")
endif()

if(BUILD_TESTING AND NOT CMAKE_CROSSCOMPILING)
    include(cmake/Tests.cmake)
endif()
