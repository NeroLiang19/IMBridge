using IMBridge.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IMBridge.Application;

/// <summary>
/// 常驻主循环：为每个注册的通道启动一条读取管线（source -> dispatcher），
/// dispatcher 内完成富化 / Agent 路由 / 条件回复。单条消息异常只记录不中断，保证常驻稳定性。
/// </summary>
internal sealed class BridgeWorker(
    IReadOnlyList<ChannelBinding> channels,
    IReadOnlyDictionary<string, IAgentGateway> agents,
    MessageDispatcher dispatcher,
    ILogger<BridgeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("IMBridge 已启动（通道数={Count}）", channels.Count);
        var pumps = channels.Select(binding => PumpAsync(binding, stoppingToken)).ToArray();
        await Task.WhenAll(pumps);
    }

    private async Task PumpAsync(ChannelBinding binding, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in binding.Source.ReadMessagesAsync(stoppingToken))
            {
                try
                {
                    await dispatcher.DispatchAsync(message, binding, agents, logger, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "通道 {Channel} 消息 {EventId} 处理失败，继续读取下一条", binding.ChannelId, message.EventId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停机，忽略
        }
        catch (Exception ex)
        {
            // 单通道读取异常不拖垮其它通道；记录后由 source 内部的重连逻辑兜底。
            logger.LogError(ex, "通道 {Channel} 读取管线异常", binding.ChannelId);
        }
    }
}
