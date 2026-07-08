# 时空 DEMO 程序远程过程调用(XML-RPC)消息协议 v1.3

***

|发布版本|发布日期|说明|
|:---|:---|:----|
|1.0|2023-11-13|初版|
|1.1|2025-04-26|增加响应超时状态，细节优化|
|1.2|2026-06-00|优化协议，移除支持多消息 InvokeMessages 格式|

> XML-RPC协议：远程过程调用(Remote Procedure Call) 或 反射程序控制(Reflection Program Control)
>
> 需求场景：DEMO 交互控制，PC 端应用程序控制
>
> 交互特点：局域网络交互，数据量小，传输频率低(30fps以下)，属于指令、控制类型数据，对消息加密没有特别要求
>
> 分析：
>
> 1.  XML 格式可读性高，比较友好，对编辑软件没能特别要求
>
> 2.  本地应用程序配置文件一般都是使用 XML 格式
>
> 3.  C# 应用(包括Unit3D, .NET应用)，支持 LINQ(语言集成查询) to XML，操作、解析方便
>
> 4.  未来可扩展，增加指令列表的描述定义文件(文件中包含UI描述，层级描述，指令描述)，自动传输至各控制端，控制端跟据描述信息自动解析，生成UI、连接、控制按扭等

## 协议核心原则
* 强调可读性，可编辑性，可直接使用第三方工具调试
* 单消息单请求模式，一条消息打包为一行文本内容，换行回车('\r\n', 0D0A)结束
* 强类型与弱类型兼容：支持通过 XML 节点显式声明强类型（@Type），也支持通过属性隐式推断弱类型（自动类型转换）。
* 同步/异步机制：协议本身为同步请求-响应模型。若需异步执行，服务端应在收到请求后立即返回 StatusCode="0" (Success)，后续通过其他通道（如回调或状态推送）通知执行结果。
* 调用模式：同步/异步，响应/不响应

## XML 消息控制格式

```XML
<InvokeMessage ObjectName="" MethodName="" Parameters="" Comment="" >
	<Parameter Type="System.Int32">12</Parameter>
	<Parameter Type="System.String">play</Parameter>
	<Parameter Type="System.String"><![CDATA[hello,world.如果存在特殊符号字符请使用 CDATA ]]></Parameter>
	<Parameter Type="System.Byte[]">8,9,10,A,B,C</Parameter>
</InvokeMessage>
```

| <div style="width:90pt;">**节点/@属性名称**</div> | **说明**                                                                                                                                          | <div style="width:40pt;">**值类型**</div> | <div style="width:50pt;">**必要** |
| :------------------------------------------ | :----------------------------------------------------------------------------------------------------------------------- | :------------------------------------- | :------------------------------ |
| **InvokeMessage**                           | 调用远程方法或函数的消息对象，单个调用消息                                                                                                                           |                                        | 是                               |
| @ObjectName                                 | 需要控制的实例或对象的名称                                                                                                                                   | String                                 | 是                               |
| @MethodName                                 | 实例或对象的方法或函数名称                                                                                                                                   | String                                 | 是                               |
| @Parameters                                 | 节点 **Parameter** 的简单形式，**优先级低于 Parameter 节点**，所有参数将按匹配的函数参数强制类型转换；<br />示例：Parameters="12,play,'hello,world.',\[0x08,0x09,0x10,0x0A,0x0B,0x0C]" | String                                 | 否                               |
| @Comment                                    | 该条控制消息说明、注释或描述信息；也可以预留未来给控制端做 Label 使用                                                                                                          | String                                 | 否，保留                            |
| @RequestId                                   |消息的唯一标识，可用于请求与响应进行准确匹配                                                                                                | Guid/String                                 | 否，保留                            |
| @Timestamp                                   |消息生成或是发送的时间戳，建议使用 ISO8601 标准；调试分析，延迟统计，日志追踪，超时判断等                                                                                               | DateTime/String                                 | 否，保留                            |
| @Version                                   |协议版本号                                                                                           | Number/String                                 | 否，保留                            |
| @Mode                                     |调用模式，Sync:同步调用，Async:异步调用，Notify:只发送不响应                                                                                          | Number/String                                 | 否，保留                            |
| **Parameter**                               | 方法或函数的参数信息，跟据方法或函数是否存在参数而定义；<br />**优先级高于 @Parameters 属性**                                                                                      |                                        | 否                               |
| @Type                                       | 参数的数据类型(为服务端的数据类型全名)，如果不明确指定数据类型，则会跟据方法对应的参数强制类型转换；<br />参考类型示例：[TypeCode 枚举](https://learn.microsoft.com/zh-cn/dotnet/api/system.typecode?view=net-7.0)         | String                                 | 否                               |
| @扩展属性或节点                                    | 可在 InvokeMessage 根节点上扩展属性，或根节点之下扩展子节点(非Parameter节点)                                                                                             |                                        |                                 |

*   [x] **建议：手动输入消息编码使用 @Parameters 属性，代码封装或序列化使用 Parameter 节点；**

> #### 控制消息示例：
>
> ```XML
> <InvokeMessage ObjectName="Window" MethodName="Show" /> 
> <InvokeMessage ObjectName="Window" MethodName="Close" />
> <InvokeMessage ObjectName="Demo" MethodName="GetCurrentPage" />
> <InvokeMessage ObjectName="Demo" MethodName="OpenPage" Parameters="2,en-Us" />
> <InvokeMessage ObjectName="Demo" MethodName="OpenPage">
> 	<Parameter Type="System.Int32">2</Parameter>
> 	<Parameter Type="System.Enum">en-Us</Parameter>
> </InvokeMessage>
> <InvokeMessage ObjectName="Video" MethodName="GetCurrentPosition" />
> <InvokeMessage ObjectName="Video" MethodName="Play" />
> <InvokeMessage ObjectName="Video" MethodName="Seek" Parameters="5.6"  />
> <InvokeMessage ObjectName="Video" MethodName="Seek" >
> 	<Parameter Type="System.Float">5.6</Parameter>
> </InvokeMessage>
>
> ```

## XML 消息响应格式

```XML
<InvokeResult StatusCode="" ExceptionMessage="" ObjectMethod="" ReturnType="" ReturnValue="">
	<Return Type="System.Int32">6</Return>
</InvokeResult>
```

| <div style="width:110pt;">**节点/@属性名称**</div> | <div style="width:400pt;">**说明**  </div>                                                                                            | <div style="width:40pt;">**值类型**</div> | <div style="width:50pt;">**必要** |
| :------------------------------------------- | :---------------------------------------------------------------------------------------------------------------------------------- | :------------------------------------- | :------------------------------ |
| **InvokeResult**                             | 调用远程方法或函数的返回消息对象，单个响应消息                                                                                                             |                                        | 是                               |
| @RequestId                                   |消息的唯一标识，可用于请求与响应进行准确匹配                                                                                                | Guid/String                                 | 否，保留                            |
| @Timestamp                                   |消息生成或是发送的时间戳，建议使用 ISO8601 标准；调试分析，延迟统计，日志追踪，超时判断等                                                                                               | DateTime/String                                 | 否，保留                            |
| @Version                                   |协议版本号                                                                                           | Number/String                                 | 否，保留                            |
| @StatusCode                                  | 方法或函数执行的状态码，执行失败小于0，执行成功大于等于0；保留状态码：-2, -1, 0, 1                                                                                    | Int32                                  | 是                               |
| @ObjectMethod                                | 远程对象或实例的方法或函数的完整名称，格式：{ObjectName}.{MethodName}；示例：ObjectMethod="Window\.Close"                                                     | String                                 | 是                               |
| @ExceptionMessage                            | 方法或函数执行的异常信息，状态码为小于 0 的解释说明                                                                                                         | String                                 | 否                               |
| @ReturnType                                  | 节点 Return 的简单形式，**优先级低于 Return 节点**                                                                                                 | String                                 | 否                               |
| @ReturnValue                                 | 节点 Return 的简单形式，**优先级低于 Return 节点**                                                                                                 | String                                 | 否                               |
| **Return**                                   | 方法或函数的返回值，**优先级高于@ReturnType,@ReturnValue**                                                                                         |                                        | 否                               |
| @Type                                        | 返回的值类型(为服务端的数据类型全名)，如果为System.Void类型，则值为 null；<br /> 参考：[TypeCode 枚举](https://learn.microsoft.com/zh-cn/dotnet/api/system.typecode?view=net-7.0) | String                                 | 否                               |
| @扩展属性或节点                                     | 可在 InvokeResult 根节点上扩展属性，或根节点之下扩展子节点(非Return节点)                                                                                     |                                        |                                 |

| **执行状 @StatusCode** | **状态码**  | **函数执行状态**                                          | **是否有返回值** |
| :------------------ | :------- | :-------------------------------------------------- | :--------- |
| Unknown              | int.MinValue       | 未知状态，远程方法可能执行成功，也有可能执行失败，可能是在传输过程中出现不可预测的异常，或是消息读写超时等 |            |
| Timeout              | -2       | 客户端发送消息，等待服务端响应超时                                              | 无          |
| Failed              | -1       | 服务端接收到消息数据，但可能调用失败                                            | 无          |
| Success             | 0        | 确认执行成功，函数无返回值为 System.Void 类型                       | 无          |
| SuccessAndReturn    | 1        | 确认执行成功，函数有返回值(为非 System.Void 类型)                    | 有          |
|                     | 其它自定义状态码 | 执行失败小于0，执行成功大于等于0                                   |            |

| 状态码  | 名称               | 说明      |
| ---- | ------------------------ | --------------------------- |
| -100 | ParseError       | 消息格式错误  |
| -101 | InvalidObject    | 对象不存在   |
| -102 | InvalidMethod    | 方法不存在   |
| -103 | ParameterError   | 参数错误    |
| -104 | AccessDenied     | 权限不足    |
| -105 | NotSupported     | 功能不支持   |
| -106 | ExecutionTimeout | 执行超时    |
| -107 | InternalError    | 服务端内部异常 |
| -2   | Timeout          | 客户端等待超时 |
| -1   | Failed           | 调用失败    |
| 0    | Success          | 成功，无返回值 |
| 1    | SuccessAndReturn | 成功，有返回值 |

> #### 响应消息示例
>
> ```XML
> <InvokeResult StatusCode="0" ObjectMethod="Window.Show" /> 
> <InvokeResult StatusCode="-1" ObjectMethod="Window.Close" ExceptionMessage="excption message content" />
> <InvokeResult StatusCode="1" ObjectMethod="Demo.OpenPage" ReturnType="System.Boolean" ReturnValue="True" />
> <InvokeResult StatusCode="1" ObjectMethod="Video.GetCurrentPosition">
> 	<Return Type="System.Float">5.6</Return>
> </InvokeResult>
>
> ```

## 属性 @Parameters 的约定(弱类型传参)

*   多个参数值以英文 ',' 符号间隔区分，不用明确值类型
*   支持集合类型参数，在 '\[]' 符号内定义数据，元素类型为基本的值类型
*   支持识别十六进制字符内容，以 '0x' 开头的字符
*   支持字符串识别，以单引号或双引号包裹内的字符，字符串应控制在 256 长度，不包括特殊符号
*   示例：Parameters="12,play,1024,\[0x01,0xA0,0xAA],'this is string content'"
### 当使用 @Parameters 属性进行弱类型传参时，需遵循以下规则：
* 分隔符：多个参数值以英文逗号 , 间隔区分，不用明确值类型。
* 集合/数组类型：支持集合类型参数，在 [] 符号内定义数据，元素类型为基本的值类型。示例：[1,2,3]。
* 十六进制识别：支持识别十六进制字符内容，必须以 0x 开头。示例：0x0A, 0xFF。
* 字符串识别：以单引号 ' 或双引号 " 包裹的字符。
	* 长度限制：建议字符串长度控制在 256 以内（不包括包裹符号）
* 字节数组 (Byte[])：在 [] 内使用带 0x 前缀的十六进制表示。示例：[0x01, 0xA0, 0xAA]。

## 属性 @ReturnValue 的解析
* 返回值类型应是简单的值类型、少量的字符标识，或是数组
* 与 @Parameters 一致


## 消息加密等级参考

| 等级      | 说明          | 参考算法               |
| :------ | :---------- | :----------------- |
| 0级 \[x] | 明码传输，不做任何处理 |                    |
| 1级      | 隐藏，二次编码     | Base64, 其它自定义二进制序列 |
| 2级      | 对称加密        | AES, DES 等         |
| 3级      | 非对称加密       | RSA, DSA 等         |



