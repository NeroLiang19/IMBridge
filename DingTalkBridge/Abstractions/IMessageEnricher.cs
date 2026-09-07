using DingTalkBridge.Domain;

namespace DingTalkBridge.Abstractions;

/// <summary>
/// 消息富化器。单一职责：把一条原始消息加工成更适合 Agent 处理的形态
/// （例如钉钉的图片消息：下载媒体 + 视觉识别，将图片转成文本）。
/// 返回值必须保留 ChannelId、EventId、MessageId 和 ReplyTarget，不得改变消息所属通道或回复目标。
/// </summary>
public interface IMessageEnricher
{
    Task<IncomingMessage> EnrichAsync(IncomingMessage message, CancellationToken cancellationToken);
}
