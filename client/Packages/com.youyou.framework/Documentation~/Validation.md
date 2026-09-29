# 验证记录

验证日期：2026-09-26。检查仅针对这个独立包，没有打开、升级或编译原游戏工程。

## 已执行

| 环境 | 验证 | 结果 |
| --- | --- | --- |
| .NET SDK 8.0.421，C# 7.3 编译模式 | 核心源码独立编译与 18 项功能检查；不引用 Unity | 通过 |
| Unity 2022.3.62f3c1 / Windows | 空白工程导入、核心检查、Editor 检查、Play Mode 生命周期 | 通过 |
| Unity 2021.3.8f1 / Windows | 最小包依赖工程导入，包含最终 Editor 菜单与运行时修改；同组功能检查 | 通过 |

Unity 2021 验证工程的 manifest 只有 `com.youyou.framework` 本地包和示例所需 `com.unity.modules.imgui`；其余依赖由包声明解析。没有复制原项目脚本或插件。

核心检查覆盖事件监听增删/去重/重入、SocketEvent、定时器取消遍历/暂停/次数/重启/静态回调、FSM 无效转换/共享状态校验/销毁、项目流程、对象池按引用识别/重置/容量、独立 Context、本地化、二进制小端/截断检测、GZip，以及 TCP 半包/粘包/长度上限/本机回环收发/主线程派发。

Unity Editor 检查覆盖 UI 打开去重/关闭/释放/缺组件失败释放、GameObjectPool 复用/重复回池/容量、真实 Resources 资源加载。Play Mode 检查覆盖 Awake 初始化、Update 驱动定时器、重复入口清理、OnDestroy 释放、再次启动。QuickStart 示例同时参与实际 Unity 编译。

## 尚未实测

- Unity 2020.3 编辑器未安装；该版本作为 API 基线，实际运行验证为上表版本。
- 未进行手机、IL2CPP、WebGL 构建或真实项目服务器联调。
- HTTP、声音播放、跨场景切换通过编译；未进行远程 HTTP、真实音频设备和多场景行为检查。
- 旧 AssetBundle 更新链路、XLua、FMOD、业务表与旧 MMO 协议未打包，也不在本次验证范围。

## 复现

在包目录执行 `Tools~/Test-Framework.ps1`。加 `-UnityPath` 指向安装的 Unity.exe 即可复现 Unity 验证。脚本必须收到完整成功标记 `YOUYOU_FRAMEWORK_VALIDATION_PASSED` 才返回成功，单纯退出码为 0 不算通过。

首次 2022 验证工程：`E:/study/YouYouFrameworkValidation-20260926-01/validation.log`。

最终 2021 最小依赖验证工程：`E:/study/YouYouFrameworkValidation-20260926-02/validation.log`。

以上绝对路径仅为本次环境中的日志位置，包的运行、导入和验证脚本均不依赖这些路径。
