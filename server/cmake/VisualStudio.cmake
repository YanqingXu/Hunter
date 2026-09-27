# 为 Windows 开发构建生成单项目 VS 入口，编译规则仍由原 CMake 目标维护。
if(NOT CMAKE_GENERATOR MATCHES "^Visual Studio " OR
   NOT CMAKE_VS_PLATFORM_NAME STREQUAL "x64" OR HUNTER_PRODUCTION)
    return()
endif()

# 转义 XML 属性和文本中的特殊字符，不处理生成器表达式。
function(hunter_vs_xml output value)
    string(REPLACE "&" "&amp;" value "${value}")
    string(REPLACE "<" "&lt;" value "${value}")
    string(REPLACE ">" "&gt;" value "${value}")
    string(REPLACE "\"" "&quot;" value "${value}")
    set(${output} "${value}" PARENT_SCOPE)
endfunction()

set(hunter_vs_dir "${CMAKE_CURRENT_BINARY_DIR}/ide")
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/HunterServer.slnx")
file(MAKE_DIRECTORY "${hunter_vs_dir}")
file(GLOB_RECURSE hunter_vs_files CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cpp" "${CMAKE_CURRENT_SOURCE_DIR}/src/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/platform/*.cpp" "${CMAKE_CURRENT_SOURCE_DIR}/platform/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/lua/*.lua" "${CMAKE_CURRENT_SOURCE_DIR}/lua/*.json"
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/*.cmake" "${CMAKE_CURRENT_SOURCE_DIR}/cmake/*.cpp"
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/*.in"
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/*.cpp" "${CMAKE_CURRENT_SOURCE_DIR}/tools/*.py"
    "${CMAKE_CURRENT_SOURCE_DIR}/tools/*.md"
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.cpp" "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.py" "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.lua"
    "${CMAKE_CURRENT_SOURCE_DIR}/tests/*.json"
    "${CMAKE_CURRENT_SOURCE_DIR}/intents/*.md" "${CMAKE_CURRENT_SOURCE_DIR}/rules/*.md")
file(GLOB hunter_vs_root CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/*.md" "${CMAKE_CURRENT_SOURCE_DIR}/CMake*.json")
list(APPEND hunter_vs_files ${hunter_vs_root}
    "${CMAKE_CURRENT_SOURCE_DIR}/CMakeLists.txt"
    "${CMAKE_CURRENT_SOURCE_DIR}/HunterServer.slnx"
    "${HUNTER_PROTO}" "${CMAKE_CURRENT_SOURCE_DIR}/../protobuf/README.md")
set(hunter_vs_items "")
set(hunter_vs_filter_items "")
set(hunter_vs_folders "")
foreach(path IN LISTS hunter_vs_files)
    file(RELATIVE_PATH display "${CMAKE_CURRENT_SOURCE_DIR}" "${path}")
    string(REGEX REPLACE "^\\.\\./" "" display "${display}")
    get_filename_component(folder "${display}" DIRECTORY)
    set(kind None)
    if(path MATCHES "\\.(cpp|cc)$")
        set(kind ClCompile)
    elseif(path MATCHES "\\.(h|hpp)$")
        set(kind ClInclude)
    endif()
    file(RELATIVE_PATH relative "${hunter_vs_dir}" "${path}")
    hunter_vs_xml(item "${relative}")
    string(APPEND hunter_vs_items "    <${kind} Include=\"${item}\" />\n")
    if(folder)
        string(REPLACE "/" "\\" filter "${folder}")
        hunter_vs_xml(filter "${filter}")
        string(APPEND hunter_vs_filter_items
            "    <${kind} Include=\"${item}\"><Filter>${filter}</Filter></${kind}>\n")
        while(folder)
            list(APPEND hunter_vs_folders "${folder}")
            get_filename_component(folder "${folder}" DIRECTORY)
        endwhile()
    endif()
endforeach()

# 生成文件在首次编译前也可见，防止工程首次打开缺少协议与配置身份入口。
foreach(name hunter.pb.cc hunter.pb.h ContentId.h SchemaSpec.h game.lua game.map.json
    content.json cfg/Manifest.json csharp/Hunter.cs)
    hunter_vs_xml(item "../generated/${name}")
    string(APPEND hunter_vs_items "    <None Include=\"${item}\" />\n")
    get_filename_component(folder "generated/${name}" DIRECTORY)
    string(REPLACE "/" "\\" filter "${folder}")
    string(APPEND hunter_vs_filter_items
        "    <None Include=\"${item}\"><Filter>${filter}</Filter></None>\n")
    list(APPEND hunter_vs_folders "${folder}" generated)
endforeach()
list(REMOVE_DUPLICATES hunter_vs_folders)
list(SORT hunter_vs_folders)
set(hunter_vs_filters "")
foreach(folder IN LISTS hunter_vs_folders)
    string(UUID guid NAMESPACE "27c8d2c2-433e-45b6-8e0c-6b2a792642cb"
        NAME "${folder}" TYPE SHA1 UPPER)
    string(REPLACE "/" "\\" filter "${folder}")
    hunter_vs_xml(filter "${filter}")
    string(APPEND hunter_vs_filters
        "    <Filter Include=\"${filter}\"><UniqueIdentifier>{${guid}}</UniqueIdentifier></Filter>\n")
endforeach()

set(hunter_vs_configs "")
foreach(cfg IN LISTS CMAKE_CONFIGURATION_TYPES)
    hunter_vs_xml(cfg "${cfg}")
    string(APPEND hunter_vs_configs
        "    <ProjectConfiguration Include=\"${cfg}|x64\"><Configuration>${cfg}</Configuration>"
        "<Platform>x64</Platform></ProjectConfiguration>\n")
endforeach()
configure_file("${CMAKE_CURRENT_LIST_DIR}/HunterServer.vcxproj.in"
    "${hunter_vs_dir}/HunterServer.vcxproj" @ONLY)
configure_file("${CMAKE_CURRENT_LIST_DIR}/HunterServer.vcxproj.filters.in"
    "${hunter_vs_dir}/HunterServer.vcxproj.filters" @ONLY)

# 同时读取各自有编译目标，包含仅供存档实现使用的 SQLite 等私有包含路径。
set(hunter_vs_includes "")
set(hunter_vs_defines "")
foreach(target hunter_server_desktop hunter_server_core hunter_script hunter_protocol
    hunter_storage hunter_storage_common)
    list(APPEND hunter_vs_includes "$<TARGET_PROPERTY:${target},INCLUDE_DIRECTORIES>")
    list(APPEND hunter_vs_defines "$<TARGET_PROPERTY:${target},COMPILE_DEFINITIONS>")
endforeach()
set(hunter_vs_includes "$<FILTER:$<REMOVE_DUPLICATES:${hunter_vs_includes}>,EXCLUDE,^$>")
set(hunter_vs_defines "$<FILTER:$<REMOVE_DUPLICATES:${hunter_vs_defines}>,EXCLUDE,^$>")
configure_file("${CMAKE_CURRENT_LIST_DIR}/HunterServer.props.in"
    "${hunter_vs_dir}/HunterServer.props.in" @ONLY)
file(GENERATE OUTPUT "${hunter_vs_dir}/$<CONFIG>.props"
    INPUT "${hunter_vs_dir}/HunterServer.props.in" TARGET hunter_server_desktop)

file(READ "${CMAKE_CURRENT_SOURCE_DIR}/HunterServer.slnx" hunter_vs_solution)
string(REPLACE "build/win-dev/ide/HunterServer.vcxproj" "ide/HunterServer.vcxproj"
    hunter_vs_solution "${hunter_vs_solution}")
file(CONFIGURE OUTPUT "${CMAKE_CURRENT_BINARY_DIR}/HunterServer.Dev.slnx"
    CONTENT "${hunter_vs_solution}" @ONLY)
message(STATUS "VS 单项目入口: ${CMAKE_CURRENT_BINARY_DIR}/HunterServer.Dev.slnx")
