# 注册真实契约、脚本工具与进程集成测试；生产测试使用显式提供的签名制品。
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
if(HUNTER_PRODUCTION)
    set(HUNTER_TEST_BUNDLE "" CACHE FILEPATH "仅供测试的已签名 Bundle")
    set(HUNTER_TEST_POLICY "" CACHE FILEPATH "仅供测试的 Bundle 公钥和身份策略")
    if(NOT EXISTS "${HUNTER_TEST_BUNDLE}" OR NOT EXISTS "${HUNTER_TEST_POLICY}")
        message(FATAL_ERROR "Production tests require HUNTER_TEST_BUNDLE and HUNTER_TEST_POLICY")
    endif()
    add_test(NAME hunter_script_contract COMMAND hunter_script_contract
        "${HUNTER_TEST_BUNDLE}" "${HUNTER_TEST_POLICY}")
    set(process_args --bundle "${HUNTER_TEST_BUNDLE}" --policy "${HUNTER_TEST_POLICY}")
else()
    add_test(NAME hunter_script_contract COMMAND hunter_script_contract "${HUNTER_GEN}/game.lua")
    add_executable(hunter_async_contract tests/contract/AsyncContract.cpp)
    hunter_target(hunter_async_contract)
    target_link_libraries(hunter_async_contract PRIVATE hunter_script)
    add_test(NAME hunter_async_contract COMMAND hunter_async_contract)
    set(process_args --source "${HUNTER_GEN}/game.lua")
endif()
if(TARGET hunter_server_desktop)
    add_test(NAME hunter_process_integration COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/integration/process_test.py"
        --exe $<TARGET_FILE:hunter_server_desktop> ${process_args})
    set_tests_properties(hunter_process_integration PROPERTIES TIMEOUT 150)
    if(HUNTER_PRODUCTION)
        add_test(NAME hunter_production_link_contract COMMAND ${Python3_EXECUTABLE}
            "${CMAKE_CURRENT_SOURCE_DIR}/tools/verify_production.py"
            --build "${CMAKE_CURRENT_BINARY_DIR}" --config $<CONFIG>)
    endif()
endif()
add_test(NAME hunter_assemble_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_assemble.py")
add_test(NAME hunter_schema_contract COMMAND ${Python3_EXECUTABLE}
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_schema.py")
if(HUNTER_BUILD_TOOLS)
    add_test(NAME hunter_bundle_contract COMMAND ${Python3_EXECUTABLE}
        "${CMAKE_CURRENT_SOURCE_DIR}/tests/scripts/test_bundle.py"
        --luax-root "${luax_SOURCE_DIR}" --luaxc $<TARGET_FILE:luax_compiler_tool>
        --bundle-tool $<TARGET_FILE:luax_bundle_tool> --source "${HUNTER_GEN}/game.lua"
        --output-dir "${CMAKE_CURRENT_BINARY_DIR}/bundle-test")
    set_tests_properties(hunter_bundle_contract PROPERTIES TIMEOUT 60)
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
get_property(hunter_targets DIRECTORY PROPERTY BUILDSYSTEM_TARGETS)
list(JOIN hunter_targets "\",\n  \"" hunter_targets_json)
file(WRITE "${CMAKE_CURRENT_BINARY_DIR}/hunter-targets.json" "[\n  \"${hunter_targets_json}\"\n]\n")
