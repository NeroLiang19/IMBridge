using System.Collections.Immutable;

namespace IMBridge.Domain;

/// <summary>
/// 一条发给"我本人"的 IM 消息（私聊或群里@我）。
/// 该类型与具体 IM 解耦：钉钉专属的 sender id / media 正则只存在于 Dws 适配层，
/// 这里只保留通用字段 ChannelId / ReplyTarget / ChannelContext / Attachments。
/// </summary>
public sealed record IncomingMessage
{
    /// <summary>通道标识（如 "dingtalk"），去重与会话路由都以它为准，且不能跨通道回复。</summary>
    public required string ChannelId { get; init; }

    /// <summary>事件去重用的事件 ID（配合 ChannelId 构成去重键）。</summary>
    public required string EventId { get; init; }

    /// <summary>消息 ID（用于幂等回复键与媒体下载上下文）。</summary>
    public required string MessageId { get; init; }

    /// <summary>原始事件类型（由适配层填充，仅作日志/诊断用）。</summary>
    public required string EventType { get; init; }

    /// <summary>发送人展示名（用于提示词与日志）。</summary>
    public required string SenderName { get; init; }

    /// <summary>消息文本内容。</summary>
    public required string Content { get; init; }

    /// <summary>回复目标（会话类型 + 目标 ID）。</summary>
    public required ReplyTarget ReplyTarget { get; init; }

    /// <summary>附件列表（由适配层填充，可为空）。</summary>
    public ImmutableArray<Attachment> Attachments { get; init; } = ImmutableArray<Attachment>.Empty;

    /// <summary>
    /// IM 专属上下文（不透明键值对），仅供该通道的适配层（如媒体下载）使用，
    /// 例如钉钉会放入 conversationId 以便 download-media 调用。
    /// </summary>
    public IReadOnlyDictionary<string, string>? ChannelContext { get; init; }
}
