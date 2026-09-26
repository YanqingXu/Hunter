# 注册真实契约、脚本工具与进程集成测试；生产测试使用显式提供的签名制品。
set(HUNTER_FIXTURE "${HUNTER_GEN}/fixture")
add_custom_command(OUTPUT "${HUNTER_FIXTURE}/content.json" "${HUNTER_FIXTURE}/ContentId.h"
    "${HUNTER_FIXTURE}/cfg/Manifest.json"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/../export/gameplay.py"
        --fixture "${CMAKE_CURRENT_SOURCE_DIR}/../design/combat_demo.json"
        --output "${HUNTER_FIXTURE}/content.json" --header "${HUNTER_FIXTURE}/ContentId.h"
        --cfg "${HUNTER_FIXTURE}/cfg" --luax "${HUNTER_LUAX}"
    DEPENDS ../design/combat_demo.json ../export/gameplay.py ../export/export.py
        tools/assemble.py tools/publish.py ${HUNTER_CFG_LUA} ${HUNTER_LUAX_DEP} VERBATIM)
add_custom_target(hunter_fixture_content DEPENDS "${HUNTER_FIXTURE}/ContentId.h"
    "${HUNTER_FIXTURE}/cfg/Manifest.json")
add_custom_command(OUTPUT "${HUNTER_FIXTURE}/game.lua" "${HUNTER_FIXTURE}/game.map.json"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/tools/assemble.py"
        --manifest "${CMAKE_CURRENT_SOURCE_DIR}/lua/modules.json"
        --cfg "${HUNTER_FIXTURE}/cfg" --output "${HUNTER_FIXTURE}/game.lua"
        --map "${HUNTER_FIXTURE}/game.map.json"
    DEPENDS ${HUNTER_LUA} lua/modules.json tools/assemble.py
        "${HUNTER_FIXTURE}/cfg/Manifest.json" VERBATIM)
add_custom_target(hunter_fixture_scripts DEPENDS "${HUNTER_FIXTURE}/game.lua")
add_dependencies(hunter_fixture_scripts hunter_fixture_content)
add_executable(hunter_session_contract tests/contract/SessionContract.cpp)
hunter_target(hunter_session_contract)
add_test(NAME hunter_session_contract COMMAND hunter_session_contract)
add_executable(hunter_raid_contract tests/contract/RaidContract.cpp)
hunter_target(hunter_raid_contract)
target_link_libraries(hunter_raid_contract PRIVATE hunter_script)
add_test(NAME hunter_raid_contract COMMAND hunter_raid_contract)
add_executable(hunter_pve_contract tests/contract/PveContract.cpp)
hunter_target(hunter_pve_contract)
target_link_libraries(hunter_pve_contract PRIVATE hunter_script)
add_dependencies(hunter_pve_contract hunter_scripts)
add_executable(hunter_gameplay_contract tests/contract/GameplayContract.cpp)
hunter_target(hunter_gameplay_contract)
target_link_libraries(hunter_gameplay_contract PRIVATE hunter_script)
add_dependencies(hunter_gameplay_contract hunter_scripts)
add_executable(hunter_gameplay_edges_contract tests/contract/GameplayEdges.cpp)
hunter_target(hunter_gameplay_edges_contract)
target_link_libraries(hunter_gameplay_edges_contract PRIVATE hunter_script)
add_dependencies(hunter_gameplay_edges_contract hunter_scripts)
add_executable(hunter_action_contract tests/contract/ActionContract.cpp)
hunter_target(hunter_action_contract)
target_link_libraries(hunter_action_contract PRIVATE hunter_protocol)
add_test(NAME hunter_action_contract COMMAND hunter_action_contract)
add_executable(hunter_demo_contract tests/contract/DemoContract.cpp)
hunter_target(hunter_demo_contract)
target_link_libraries(hunter_demo_contract PRIVATE hunter_script)
add_test(NAME hunter_demo_contract COMMAND hunter_demo_contract)
add_executable(hunter_storage_contract tests/contract/StorageContract.cpp)
hunter_target(hunter_storage_contract)
target_link_libraries(hunter_storage_contract PRIVATE hunter_storage
    nlohmann_json::nlohmann_json hunter_sqlite)
add_test(NAME hunter_storage_contract COMMAND hunter_storage_contract)
set_tests_properties(hunter_storage_contract PROPERTIES TIMEOUT 60)

# 注入点只编译进测试专用副本，不改变正式存储目标。
add_library(hunter_storage_fault STATIC ${HUNTER_STORAGE_SOURCES})
hunter_target(hunter_storage_fault)
target_include_directories(hunter_storage_fault PRIVATE tests/fixtures)
target_compile_definitions(hunter_storage_fault PRIVATE HUNTER_STORAGE_TESTING=1)
target_link_libraries(hunter_storage_fault PUBLIC hunter_asio Threads::Threads
    PRIVATE hunter_sqlite nlohmann_json::nlohmann_json)
add_executable(hunter_storage_crash tests/fixtures/StorageCrash.cpp)
hunter_target(hunter_storage_crash)
target_link_libraries(hunter_storage_crash PRIVATE hunter_storage_fault hunter_sqlite)
add_test(NAME hunter_storage_crash_integration COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/storage_test.py"
    --exe $<TARGET_FILE:hunter_storage_crash>)
set_tests_properties(hunter_storage_crash_integration PROPERTIES TIMEOUT 120)

add_executable(hunter_perf tests/contract/Perf.cpp)
hunter_target(hunter_perf)
target_link_libraries(hunter_perf PRIVATE hunter_server_core)
add_executable(hunter_object_contract tests/contract/ObjectContract.cpp)
hunter_target(hunter_object_contract)
target_link_libraries(hunter_object_contract PRIVATE hunter_script)
add_test(NAME hunter_object_contract COMMAND hunter_object_contract)
add_executable(hunter_core_contract tests/unit/CoreTest.cpp)
hunter_target(hunter_core_contract)
target_link_libraries(hunter_core_contract PRIVATE hunter_server_core)
add_test(NAME hunter_core_contract COMMAND hunter_core_contract)
add_executable(hunter_net_contract tests/unit/NetTest.cpp)
hunter_target(hunter_net_contract)
target_link_libraries(hunter_net_contract PRIVATE hunter_server_core)
add_test(NAME hunter_net_contract COMMAND hunter_net_contract)
add_executable(hunter_script_contract tests/contract/ScriptContract.cpp)
hunter_target(hunter_script_contract)
target_link_libraries(hunter_script_contract PRIVATE hunter_script)
add_dependencies(hunter_script_contract hunter_scripts)
add_executable(hunter_game_contract tests/contract/GameContract.cpp)
hunter_target(hunter_game_contract)
target_link_libraries(hunter_game_contract PRIVATE hunter_script)
add_dependencies(hunter_game_contract hunter_scripts hunter_content)
add_custom_command(OUTPUT "${HUNTER_GEN}/entities.lua" "${HUNTER_GEN}/entities.map.json"
    COMMAND ${Python3_EXECUTABLE} "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/assemble_entities.py"
        --manifest "${CMAKE_CURRENT_SOURCE_DIR}/lua/modules.json"
        --fixture "${CMAKE_CURRENT_SOURCE_DIR}/tests/fixtures/entities.lua"
        --cfg "${HUNTER_FIXTURE}/cfg"
        --output "${HUNTER_GEN}/entities.lua" --map "${HUNTER_GEN}/entities.map.json"
    DEPENDS ${HUNTER_LUA} lua/modules.json tests/fixtures/entities.lua
        tests/scripts/assemble_entities.py tools/assemble.py tools/publish.py
        "${HUNTER_FIXTURE}/cfg/Manifest.json" VERBATIM)
add_custom_target(hunter_entity_scripts DEPENDS "${HUNTER_GEN}/entities.lua")
add_dependencies(hunter_entity_scripts hunter_fixture_content)
add_executable(hunter_entity_contract tests/contract/EntityContract.cpp)
hunter_target(hunter_entity_contract)
target_link_libraries(hunter_entity_contract PRIVATE hunter_script)
add_dependencies(hunter_entity_contract hunter_entity_scripts hunter_content)
if(HUNTER_PRODUCTION)
    set(HUNTER_TEST_BUNDLE "" CACHE FILEPATH "仅供测试的已签名 Bundle")
    set(HUNTER_TEST_POLICY "" CACHE FILEPATH "仅供测试的 Bundle 公钥和身份策略")
    if(NOT EXISTS "${HUNTER_TEST_BUNDLE}" OR NOT EXISTS "${HUNTER_TEST_POLICY}")
        message(FATAL_ERROR "Production tests require HUNTER_TEST_BUNDLE and HUNTER_TEST_POLICY")
    endif()
    get_filename_component(hunter_test_bundle_dir "${HUNTER_TEST_BUNDLE}" DIRECTORY)
    set(HUNTER_TEST_FIXTURE_BUNDLE "${hunter_test_bundle_dir}/fixture.luxb" CACHE FILEPATH
        "旧战斗契约使用的独立签名 Lua 配置夹具")
    set(HUNTER_TEST_ENTITY_BUNDLE "${hunter_test_bundle_dir}/entities.luxb" CACHE FILEPATH
        "仅供实体生命周期验证的已签名隔离夹具")
    if(NOT EXISTS "${HUNTER_TEST_ENTITY_BUNDLE}")
        message(FATAL_ERROR "Production tests require the signed entity fixture Bundle")
    endif()
    add_test(NAME hunter_script_contract COMMAND hunter_script_contract
        "${HUNTER_TEST_FIXTURE_BUNDLE}" "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_game_contract COMMAND hunter_game_contract
        "${HUNTER_TEST_FIXTURE_BUNDLE}" "${HUNTER_FIXTURE}/content.json" "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_entity_contract COMMAND hunter_entity_contract
        "${HUNTER_TEST_ENTITY_BUNDLE}" "${HUNTER_FIXTURE}/content.json" "${HUNTER_TEST_POLICY}")
    set(process_args --bundle "${HUNTER_TEST_BUNDLE}" --policy "${HUNTER_TEST_POLICY}")
    set(fixture_process_args --bundle "${HUNTER_TEST_FIXTURE_BUNDLE}" --policy "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_pve_contract COMMAND hunter_pve_contract
        "${HUNTER_TEST_BUNDLE}" "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_gameplay_contract COMMAND hunter_gameplay_contract
        "${HUNTER_TEST_BUNDLE}" "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_gameplay_edges_contract COMMAND hunter_gameplay_edges_contract
        "${HUNTER_TEST_BUNDLE}" "${HUNTER_TEST_POLICY}")
    add_test(NAME hunter_endurance_contract COMMAND hunter_perf
        "${HUNTER_TEST_FIXTURE_BUNDLE}" "${HUNTER_FIXTURE}/content.json" "${HUNTER_TEST_POLICY}" 1800)
else()
    add_test(NAME hunter_script_contract COMMAND hunter_script_contract "${HUNTER_FIXTURE}/game.lua")
    add_test(NAME hunter_game_contract COMMAND hunter_game_contract
        "${HUNTER_FIXTURE}/game.lua" "${HUNTER_FIXTURE}/content.json")
    add_test(NAME hunter_entity_contract COMMAND hunter_entity_contract
        "${HUNTER_GEN}/entities.lua" "${HUNTER_FIXTURE}/content.json")
    add_executable(hunter_async_contract tests/contract/AsyncContract.cpp)
    hunter_target(hunter_async_contract)
    target_link_libraries(hunter_async_contract PRIVATE hunter_script)
    add_test(NAME hunter_async_contract COMMAND hunter_async_contract)
    set(process_args --source "${HUNTER_GEN}/game.lua")
    set(fixture_process_args --source "${HUNTER_FIXTURE}/game.lua")
    add_test(NAME hunter_pve_contract COMMAND hunter_pve_contract "${HUNTER_GEN}/game.lua")
    add_test(NAME hunter_gameplay_contract COMMAND hunter_gameplay_contract "${HUNTER_GEN}/game.lua")
    add_test(NAME hunter_gameplay_edges_contract COMMAND hunter_gameplay_edges_contract
        "${HUNTER_GEN}/game.lua")
    add_test(NAME hunter_endurance_contract COMMAND hunter_perf
        "${HUNTER_FIXTURE}/game.lua" "${HUNTER_FIXTURE}/content.json" 1800)
endif()
set_tests_properties(hunter_endurance_contract PROPERTIES TIMEOUT 90)
if(TARGET hunter_server_desktop)
    add_test(NAME hunter_gameplay_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/gameplay_test.py"
        --exe $<TARGET_FILE:hunter_server_desktop> --client $<TARGET_FILE:hunter_client>
        ${process_args})
    set_tests_properties(hunter_gameplay_integration PROPERTIES TIMEOUT 60)
    set(lua_cfg_args --exe $<TARGET_FILE:hunter_server_desktop>
        --tools "${HUNTER_GEN}/test-cfg-tools-$<CONFIG>.json")
    if(HUNTER_PRODUCTION)
        list(APPEND lua_cfg_args --mode bundle --policy "${HUNTER_TEST_POLICY}")
    else()
        list(APPEND lua_cfg_args --mode source)
    endif()
    add_test(NAME hunter_lua_cfg_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/lua_cfg_test.py" ${lua_cfg_args})
    set_tests_properties(hunter_lua_cfg_integration PROPERTIES TIMEOUT 60)
    add_library(hunter_fault_core STATIC src/core/Runtime.cpp src/core/TickClock.cpp
        src/net/Protocol.cpp src/net/Transport.cpp)
    hunter_target(hunter_fault_core)
    target_link_libraries(hunter_fault_core PUBLIC hunter_script hunter_storage_fault bcrypt)
    add_executable(hunter_runtime_fault platform/desktop/Main.cpp tests/fixtures/RuntimeFault.cpp)
    hunter_target(hunter_runtime_fault)
    target_link_libraries(hunter_runtime_fault PRIVATE hunter_fault_core)
    target_compile_definitions(hunter_runtime_fault PRIVATE HUNTER_SCRIPT_PATH="${HUNTER_GEN}/game.lua")
    add_test(NAME hunter_demo_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/demo_test.py"
        --exe $<TARGET_FILE:hunter_server_desktop> --client $<TARGET_FILE:hunter_client>
        ${process_args})
    set_tests_properties(hunter_demo_integration PROPERTIES TIMEOUT 720)
    add_test(NAME hunter_demo_edges_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/demo_edges_test.py"
        --exe $<TARGET_FILE:hunter_server_desktop> --client $<TARGET_FILE:hunter_client>
        ${process_args})
    set_tests_properties(hunter_demo_edges_integration PROPERTIES TIMEOUT 120)
    add_test(NAME hunter_runtime_crash_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/runtime_fault_test.py"
        --exe $<TARGET_FILE:hunter_runtime_fault> --client $<TARGET_FILE:hunter_client>
        ${process_args})
    set_tests_properties(hunter_runtime_crash_integration PROPERTIES TIMEOUT 400)
    add_library(hunter_fixture_core STATIC src/core/Runtime.cpp src/core/TickClock.cpp
        src/net/Protocol.cpp src/net/Transport.cpp)
    hunter_target(hunter_fixture_core)
    target_include_directories(hunter_fixture_core BEFORE PRIVATE "${HUNTER_FIXTURE}")
    target_link_libraries(hunter_fixture_core PUBLIC hunter_script hunter_storage bcrypt)
    add_dependencies(hunter_fixture_core hunter_fixture_content)
    add_executable(hunter_fixture_server platform/desktop/Main.cpp)
    hunter_target(hunter_fixture_server)
    target_link_libraries(hunter_fixture_server PRIVATE hunter_fixture_core)
    target_compile_definitions(hunter_fixture_server PRIVATE HUNTER_SCRIPT_PATH="${HUNTER_GEN}/game.lua")
    add_executable(hunter_fixture_client tools/Client.cpp)
    hunter_target(hunter_fixture_client)
    target_include_directories(hunter_fixture_client BEFORE PRIVATE "${HUNTER_FIXTURE}")
    target_link_libraries(hunter_fixture_client PRIVATE hunter_fixture_core)
    add_test(NAME hunter_process_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/process_test.py"
        --exe $<TARGET_FILE:hunter_fixture_server> --client $<TARGET_FILE:hunter_fixture_client>
        ${fixture_process_args})
    set_tests_properties(hunter_process_integration PROPERTIES TIMEOUT 150)
    if(HUNTER_PRODUCTION)
        add_test(NAME hunter_production_link_contract COMMAND ${Python3_EXECUTABLE}
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/verify_production.py"
            --build "${CMAKE_CURRENT_BINARY_DIR}" --config $<CONFIG>)
    endif()
endif()
foreach(target hunter_object_contract hunter_script_contract)
    target_include_directories(${target} BEFORE PRIVATE "${HUNTER_FIXTURE}")
    add_dependencies(${target} hunter_fixture_content)
endforeach()
add_test(NAME hunter_assemble_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_assemble.py")
add_test(NAME hunter_schema_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_schema.py")
add_test(NAME hunter_content_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_content.py")
add_test(NAME hunter_demo_content_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_demo.py")
add_test(NAME hunter_gameplay_content_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_gameplay_content.py")
add_test(NAME hunter_package_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_package.py")
if(HUNTER_BUILD_TOOLS)
    add_test(NAME hunter_bundle_contract COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_bundle.py"
        --luax-root "${luax_SOURCE_DIR}" --luaxc $<TARGET_FILE:luax_compiler_tool>
        --bundle-tool $<TARGET_FILE:luax_bundle_tool> --source "${HUNTER_GEN}/game.lua"
        --entity-source "${HUNTER_GEN}/entities.lua"
        --fixture-source "${HUNTER_FIXTURE}/game.lua"
        --output-dir "${CMAKE_CURRENT_BINARY_DIR}/bundle-test")
    set_tests_properties(hunter_bundle_contract PROPERTIES TIMEOUT 120)
    add_dependencies(hunter_script_contract hunter_tools)
endif()
add_test(NAME hunter_intent_checker_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/contract/test_intents.py")
add_test(NAME hunter_intent_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/verify_intents.py"
    --root "${CMAKE_CURRENT_SOURCE_DIR}" --build "${CMAKE_CURRENT_BINARY_DIR}"
    --ctest "${CMAKE_CTEST_COMMAND}" --config $<CONFIG>
    --mode $<IF:$<BOOL:${HUNTER_PRODUCTION}>,production,development>
    --build-tools $<BOOL:${HUNTER_BUILD_TOOLS}>)
# 测试夹具由独立进程离线生成；生产 Runtime 和服务端没有编译器依赖。
if(HUNTER_BUILD_TOOLS)
    set(hunter_test_luaxc "$<TARGET_FILE:luax_compiler_tool>")
    set(hunter_test_bundle_tool "$<TARGET_FILE:luax_bundle_tool>")
else()
    get_filename_component(hunter_host_tools "${HUNTER_HOST_LUAX}" DIRECTORY)
    set(hunter_test_luaxc "${hunter_host_tools}/luaxc${CMAKE_EXECUTABLE_SUFFIX}")
    set(hunter_test_bundle_tool "${hunter_host_tools}/luax-bundle${CMAKE_EXECUTABLE_SUFFIX}")
endif()
file(GENERATE OUTPUT "${HUNTER_GEN}/test-cfg-tools-$<CONFIG>.json" CONTENT
    "{\"luax\":\"${HUNTER_LUAX}\",\"luaxc\":\"${hunter_test_luaxc}\",\"bundle_tool\":\"${hunter_test_bundle_tool}\",\"cache\":\"${CMAKE_CURRENT_BINARY_DIR}/cfg-cache\"}")
foreach(target hunter_script_contract hunter_game_contract hunter_entity_contract hunter_perf
    hunter_pve_contract hunter_gameplay_contract hunter_gameplay_edges_contract)
    target_compile_definitions(${target} PRIVATE
        HUNTER_CONTENT_JSON="${HUNTER_GEN}/content.json"
        HUNTER_TEST_PYTHON="${Python3_EXECUTABLE}"
        HUNTER_TEST_CFG_SCRIPT="${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/build_cfg.py"
        HUNTER_TEST_CFG_TOOLS="${HUNTER_GEN}/test-cfg-tools-$<CONFIG>.json"
        HUNTER_TEST_CFG_DIR="${CMAKE_CURRENT_BINARY_DIR}/cfg-cache")
    add_dependencies(${target} hunter_fixture_scripts)
endforeach()
set_tests_properties(hunter_game_contract hunter_gameplay_contract
    hunter_gameplay_edges_contract PROPERTIES TIMEOUT 240)
get_property(hunter_targets DIRECTORY PROPERTY BUILDSYSTEM_TARGETS)
list(JOIN hunter_targets "\",\n  \"" hunter_targets_json)
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/hunter-targets.json" "[\n  \"${hunter_targets_json}\"\n]\n")
