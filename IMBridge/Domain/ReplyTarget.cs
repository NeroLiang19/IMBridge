namespace IMBridge.Domain;

/// <summary>会话类型：单聊（Direct）或群聊（Group）。</summary>
public enum ConversationType
{
    Direct,
    Group,
}

/// <summary>
/// 通用回复目标。取代原本钉钉专属的 IsGroupAt/ConversationId/SenderOpenDingTalkId 三元组，
/// 让 Domain 不依赖任何 IM 的字段命名。具体 TargetId 的语义由各 IM 适配层解释
/// （例如钉钉：Direct 时为发送人 open_dingtalk_id，Group 时为群 conversation_id）。
/// </summary>
public sealed record ReplyTarget
{
    public required ConversationType Type { get; init; }
    public required string TargetId { get; init; }
}
