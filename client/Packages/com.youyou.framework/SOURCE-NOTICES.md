# 来源与改动

本包从用户提供的悠游课堂客户端工程整理。原文件中的作者署名与出处已在抽取或改写的相关文件保留；本次整理不为原素材另行声明开源许可证。

原始框架根目录：`Assets/YouYouFramework`。

| 包内文件 | 原始来源 | 改动 |
| --- | --- | --- |
| Core/Events/* | Managers/Event/CommonEvent.cs、SocketEvent.cs、EventManager.cs | 移除 XLua 与 Unity 引用，监听快照和去重 |
| Core/Fsm/* | Managers/Fsm/*；作者署名：边涯，http://www.u3dol.com | 保留状态与生命周期 API；参数数据去池化、校验与安全释放 |
| Core/Time/* | Managers/Time/TimeManager.cs、TimeAction.cs | 改用实例所属管理器与 deltaTime；修正取消、静态回调与重用 |
| Core/Pool/ClassObjectPool.cs | Managers/Pool/ClassObjectPool.cs | 类型键、容量限制、引用去重与重置接口 |
| Core/Utilities/MMO_MemoryStream.cs | Core/MMO_MemoryStream.cs；作者署名：边涯 | 独立命名空间、移除 Unity 引用、小端处理与 EOF 检查 |
| Core/Utilities/BitUtil.cs | Utils/BitUtil.cs | 独立命名空间、移除 Unity 引用 |
| Core/Utilities/Crc16.cs | Utils/Crc16.cs | 独立命名空间 |
| Core/Utilities/GZipUtil.cs | Utils/GZipUtil.cs | 独立命名空间、修复源文本编码 |

其余代码为本次围绕原框架模块边界编写的解耦实现、Unity 适配、示例与验证工具；不宣称与原业务 API 或网络协议完全兼容。商业插件与第三方运行库没有复制进包。
