using DingTalkBridge.Abstractions;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Application;

/// <summary>
/// 注册层：按配置的显式 Type，用对应的适配工厂创建各通道/各 Agent 实例。
/// 未知通道类型或未知 Agent 类型 -> 启动即抛错（不静默降级）。
/// 每个通道创建各自独立的 source（使用其 ChannelId/EventKeys），enricher 视觉使用所绑 Agent 配置。
/// </summary>
public sealed class BridgeRegistry
{
    public IReadOnlyList<ChannelBinding> Channels { get; }
    public IReadOnlyDictionary<string, IAgentGateway> Agents { get; }

    public BridgeRegistry(
        BridgeOptions options,
        IReadOnlyDictionary<string, IChannelAdapterFactory> channelFactories,
        IReadOnlyDictionary<string, IAgentAdapterFactory> agentFactories,
        IReadOnlyDictionary<string, IVisualRecognizerFactory> visualFactories,
        ILogger<BridgeRegistry>? logger = null)
    {
        // 1) 先建 Agent 网关（含配置快照，供通道 enricher 取所绑 Agent 配置）
        var agents = new Dictionary<string, IAgentGateway>(StringComparer.OrdinalIgnoreCase);
        var agentConfigs = new Dictionary<string, AgentConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, cfg) in options.Agents)
        {
            if (!agentFactories.TryGetValue(cfg.Type, out var agentFactory))
            {
                throw new InvalidOperationException($"未知 Agent 类型 '{cfg.Type}'（agent '{id}'），无对应适配工厂，启动中止。");
            }
            agents[id] = agentFactory.CreateGateway(id, cfg);
            agentConfigs[id] = cfg;
            logger?.LogInformation("注册 Agent 网关: {Id} type={Type}", id, cfg.Type);
        }

        var visualRecognizers = new Dictionary<string, IVisualRecognizer>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, cfg) in options.Visions)
        {
            if (!visualFactories.TryGetValue(cfg.Type, out var factory)) throw new InvalidOperationException($"未知视觉类型 '{cfg.Type}'（vision '{id}'）。");
            visualRecognizers[id] = factory.Create(id, cfg);
        }

        var channels = new List<ChannelBinding>();
        foreach (var (id, cfg) in options.Channels)
        {
            if (!channelFactories.TryGetValue(cfg.Type, out var channelFactory))
            {
                throw new InvalidOperationException($"未知通道类型 '{cfg.Type}'（channel '{id}'），无对应适配工厂，启动中止。");
            }
            if (!agents.TryGetValue(cfg.Agent, out _))
            {
                throw new InvalidOperationException($"通道 '{id}' 绑定的 Agent '{cfg.Agent}' 未注册，启动中止。");
            }
            var source = channelFactory.CreateSource(id, cfg);
            var sink = channelFactory.CreateSink(cfg);
            IVisualRecognizer? visual = null;
            if (cfg.Vision is not null && !visualRecognizers.TryGetValue(cfg.Vision, out visual)) throw new InvalidOperationException($"通道 '{id}' 引用未知视觉 '{cfg.Vision}'。");
            var enricher = channelFactory.CreateEnricher(cfg, visual);

            channels.Add(new ChannelBinding
            {
                ChannelId = id,
                Source = source,
                Sink = sink,
                Enricher = enricher,
                AgentGatewayId = cfg.Agent,
            });
            logger?.LogInformation("注册通道: {Id} type={Type} agent={Agent}", id, cfg.Type, cfg.Agent);
        }

        Channels = channels;
        Agents = agents;
    }
}
