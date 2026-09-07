using IMBridge.Domain;

namespace IMBridge.Abstractions;

/// <summary>消息来源。只做一件事：持续产出发给我本人的消息。</summary>
public interface IMessageSource
{
    IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(CancellationToken cancellationToken);
}
