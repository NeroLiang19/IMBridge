using IMBridge.Domain;

namespace IMBridge.Abstractions;

/// <summary>消息出口。只做一件事：把回复发回原会话。</summary>
public interface IMessageSink
{
    Task SendAsync(IncomingMessage origin, string text, CancellationToken cancellationToken);
}
