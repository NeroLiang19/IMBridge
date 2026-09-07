using DingTalkBridge.Domain;

namespace DingTalkBridge.Abstractions;

/// <summary>
/// Agent 处理端口：只接收通用消息并返回统一结果，不解释 IM 专属 ChannelContext。
/// 正常业务失败返回 Failed，无匹配返回 NoMatch；取消遵从调用方令牌。
/// 无法归类的基础设施异常允许抛出，由应用层按单条消息隔离，不影响后续消息。
/// </summary>
public interface IAgentGateway
{
    Task<AgentReply> AskAsync(IncomingMessage message, CancellationToken cancellationToken);
}
