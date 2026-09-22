---
id: SRV-002
status: active
target: ["hunter_server_core"]
depends_on: []
verification: ["hunter_net_contract", "hunter_process_integration"]
---

# 回环传输与框架协议

## 目标与非目标

通过唯一来源 `protobuf/hunter.proto` 验证单客户端框架输入、确认和快照；不定义玩法。

## 不变量、线程与所有权

只监听 `127.0.0.1:0`。网络对象与发送队列仅在逻辑线程访问。
每帧是四字节大端长度加 Protobuf Envelope；空帧和超过 64 KiB 的帧关闭连接。
一个活动连接包含握手阶段；额外连接直接关闭。所有异步缓存保持到回调完成。

## 接口与值语义

Hello 校验协议版本 1、内容版本、实例与令牌，默认 5 秒超时。
FrameInput 序号必须从正整数单调递增，值范围 [-1000,1000]；重复序号不重新执行。
已处理重复输入返回缓存确认，已排队重复输入等待原确认。过旧序号返回错误。
按 Tick 顺序提交脚本，Ack 与 Snapshot 的内容仅由成功脚本输出产生。
原生协议输入不使用 Protobuf JSON 作为脚本 schema。

## 失败、取消与退出

未认证输入、非法字段、超限队列、握手失败或读取失败关闭该连接并终止会话。
发送队列按条数和字节有界；未发送快照合并，正在发送的缓冲不可修改。
脚本一次调用输出先整批转换、验证、预留容量，再原子提交；关键输出不得丢弃。
断连清空积压输入并销毁脚本状态；旧会话不能由新连接恢复。

## 验证入口与依赖

`hunter_net_contract` 覆盖编解码、边界和发送容量；`hunter_process_integration`
覆盖真实 TCP 拆包粘包、认证失败、重复输入和额外客户端。没有 Unity 兼容性声明。
