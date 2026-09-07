using System.Collections.Concurrent;
using IMBridge.Abstractions;
using IMBridge.Domain;
using Microsoft.Extensions.Logging;

namespace IMBridge.Application;

/// <summary>
/// 单条消息的调度核心（与常驻循环解耦，便于测试）：
/// 去重(含ChannelId) -> 富化 -> 按 AgentGatewayId 路由到 Agent -> 仅成功时回本通道 sink。
/// 失败/无匹配均保持沉默；失败绝不把错误输出发给用户。
/// </summary>
public sealed class MessageDispatcher
{
    private readonly ConcurrentDictionary<(string ChannelId, string EventId), byte> _seen = new();
    private readonly ConcurrentQueue<(string ChannelId, string EventId)> _order = new();
    private readonly SemaphoreSlim _semaphore;
    private const int MaxSeen = 5000;

    public MessageDispatcher(int maxConcurrentTasks)
    {
        _semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrentTasks));
    }

    public async Task DispatchAsync(
        IncomingMessage message,
        ChannelBinding binding,
        IReadOnlyDictionary<string, IAgentGateway> agents,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        if (!StringComparer.Ordinal.Equals(message.ChannelId, binding.ChannelId))
            throw new InvalidOperationException("消息与通道绑定不一致，拒绝跨通道路由。");

        // 用二元组消除分隔符碰撞；保留现有 at-most-once 尝试语义，不自动重放有副作用的 Agent。
        var dedupKey = (message.ChannelId, message.EventId);
        if (!TryMarkSeen(dedupKey))
        {
            logger?.LogDebug("[dedup] 跳过重复事件 {Key}", dedupKey);
            return;
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            var enriched = await binding.Enricher.EnrichAsync(message, cancellationToken);
            if (enriched.ChannelId != message.ChannelId || enriched.EventId != message.EventId ||
                enriched.MessageId != message.MessageId || enriched.ReplyTarget != message.ReplyTarget)
                throw new InvalidOperationException("富化器改变了消息身份或回复目标，拒绝发送。");

            logger?.LogInformation("[msg] {Channel} | {Type} | {Sender}: {Content}",
                message.ChannelId, message.EventType, message.SenderName,
                enriched.Content[..Math.Min(enriched.Content.Length, 80)]);

            if (!agents.TryGetValue(binding.AgentGatewayId, out var gateway))
            {
                logger?.LogWarning("[route] 未找到 Agent 网关 {AgentId}，丢弃", binding.AgentGatewayId);
                return;
            }

            var reply = await gateway.AskAsync(enriched, cancellationToken);

            // 仅 Success 才回复；NoMatch/Failed 保持沉默（失败也不外发错误输出）。
            if (reply.Outcome == AgentOutcome.Success)
            {
                await binding.Sink.SendAsync(enriched, reply.Text, cancellationToken);
            }
            else
            {
                logger?.LogInformation("[silent] {Channel} 事件 {EventId} 结果={Outcome}，不回复",
                    message.ChannelId, message.EventId, reply.Outcome);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool TryMarkSeen((string ChannelId, string EventId) key)
    {
        if (!_seen.TryAdd(key, 0))
        {
            return false;
        }
        _order.Enqueue(key);
        while (_order.Count > MaxSeen && _order.TryDequeue(out var oldest))
        {
            _seen.TryRemove(oldest, out _);
        }
        return true;
    }
}
