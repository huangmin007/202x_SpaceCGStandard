# Project Memory

## RPC 框架设计约定

### 架构对称原则
- 服务端 `RPCServerBase` 与客户端 `RPCClientBase` 镜像对称设计
- 两者均以 CRLF (0x0D 0x0A) 为数据行分割标识
- 两者均使用环形缓冲（Ring Buffer）+ 子类抽象方法实现协议解析层

### 客户端 API 设计
- `InvokeFuncAsync()` — 请求-响应模式（ResponseMode=1，必须响应），对应 C# `Func<T>` 有返回值语义
- `InvokeActionAsync()` — 单向通知模式（ResponseMode=-1），对应 C# `Action` 无返回值、发射后即忘语义
- `InvokeFuncAsync` 内部使用 `ConcurrentDictionary<int, PendingCall>` + `TaskCompletionSource` 匹配响应
- 发送使用 `SemaphoreSlim(1,1)` 序列化，防止并发写入导致字节交错
- 不使用 `AutoReconnectTcpClient`，TCP 连接管理内置于 `RPCClientBase`

### 超时与错误码约定
- 默认超时 3 秒
- **客户端本地错误码**：-96: Id 冲突，-97: 响应超时，-100: 未连接，-101: 连接已关闭，-102: 连接关闭，-105: 序列化失败，-106: 序列化结果为空，-107: 写入失败
- **服务端错误码**：0: 成功(void)，1: 成功(有返回值)，-3: 调用被拦截取消(ClientInvokeRequest)，-10: 对象未注册，-11: 方法被过滤，-12: 方法不存在，-13: 参数转换失败，-14: 方法执行异常，-15: 内部处理异常

### 协议扩展
- 新增协议只需实现两个抽象方法：`SerializeInvokeMessage` / `DeserializeResponseMessage`（客户端）、`DeserializeInvokeMessage` / `SerializeResponseMessage`（服务端）
- 客户端与服务端抽象方法完全对称
- 服务端与客户端均以 CRLF 行分隔，**一行一条消息**
