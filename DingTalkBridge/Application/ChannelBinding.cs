using DingTalkBridge.Abstractions;
using DingTalkBridge.Domain;

namespace DingTalkBridge.Application;

/// <summary>
/// 一个通道的绑定：把 source / sink / enricher 三件套与"指定用哪个 Agent 网关"绑在一起。
/// 应用层按注册的通道列表处理多个 IM；去重键含 ChannelId，回复只走本通道的 sink（不能跨通道回复）。
/// </summary>
public sealed record ChannelBinding
{
    public required string ChannelId { get; init; }
    public required IMessageSource Source { get; init; }
    public required IMessageSink Sink { get; init; }
    public required IMessageEnricher Enricher { get; init; }

    /// <summary>该通道消息交由哪个命名 Agent 网关处理（对应 Agents 配置里的 Key）。</summary>
    public required string AgentGatewayId { get; init; }
}
