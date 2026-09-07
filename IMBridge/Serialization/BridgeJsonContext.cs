using System.Text.Json.Serialization;
using IMBridge.Infrastructure.Dws;

namespace IMBridge.Serialization;

/// <summary>System.Text.Json 源生成上下文——AOT 下序列化不走反射。</summary>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(DwsEvent))]
internal partial class BridgeJsonContext : JsonSerializerContext;
