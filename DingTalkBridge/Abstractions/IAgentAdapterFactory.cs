namespace DingTalkBridge.Abstractions;

/// <summary>
/// Agent 适配工厂：按 Agent 配置的显式 Type 创建对应的 Agent 网关（如 WorkBuddyAgentAdapterFactory）。
/// 未知 Type 由注册层在启动时拒绝。
/// </summary>
public interface IAgentAdapterFactory
{
    IAgentGateway CreateGateway(string agentId, AgentConfig config);
}
