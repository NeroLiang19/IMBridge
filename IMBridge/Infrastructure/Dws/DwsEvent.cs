using System.Text.Json.Serialization;

namespace IMBridge.Infrastructure.Dws;

/// <summary>dws event consume --flatten 输出的一行 NDJSON（只取需要的字段）。</summary>
internal sealed class DwsEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("event_id")]
    public string? EventId { get; set; }

    [JsonPropertyName("message_id")]
    public string? MessageId { get; set; }

    [JsonPropertyName("conversation_id")]
    public string? ConversationId { get; set; }

    [JsonPropertyName("sender")]
    public string? Sender { get; set; }

    [JsonPropertyName("sender_open_dingtalk_id")]
    public string? SenderOpenDingTalkId { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }
}
