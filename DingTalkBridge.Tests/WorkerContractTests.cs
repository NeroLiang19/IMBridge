using DingTalkBridge.Abstractions;
using DingTalkBridge.Application;
using DingTalkBridge.Domain;
using Microsoft.Extensions.Logging.Abstractions;

namespace DingTalkBridge.Tests;

internal static class WorkerContractTests
{
    public static void Run()
    {
        foreach (var stage in new[] { "agent", "sink", "enricher", "unrelated-cancellation" })
            ContinuesAfterFailure(stage).GetAwaiter().GetResult();
        StopsOnHostCancellation().GetAwaiter().GetResult();
    }

    private static IncomingMessage Message(string id) => new()
    {
        ChannelId = "test", EventId = id, MessageId = id, EventType = "text",
        SenderName = "tester", Content = id,
        ReplyTarget = new ReplyTarget { Type = ConversationType.Direct, TargetId = "receiver" },
    };

    private static async Task ContinuesAfterFailure(string stage)
    {
        var sink = new ThrowingSink(stage == "sink");
        var gateway = new FakeGateway(m =>
        {
            if (m.EventId == "first" && stage == "agent") throw new InvalidOperationException("agent failure");
            if (m.EventId == "first" && stage == "unrelated-cancellation") throw new OperationCanceledException("adapter timeout");
            return AgentReply.Success(m.Content);
        });
        var binding = new ChannelBinding
        {
            ChannelId = "test", AgentGatewayId = "g", Source = new FakeSource(Message("first"), Message("second")),
            Sink = sink, Enricher = new ThrowingEnricher(stage == "enricher"),
        };
        using var worker = new BridgeWorker(new[] { binding }, new Dictionary<string, IAgentGateway> { ["g"] = gateway },
            new MessageDispatcher(1), NullLogger<BridgeWorker>.Instance);
        await worker.StartAsync(default);
        await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5));
        if (!sink.Sent.Contains("second")) throw new InvalidOperationException($"{stage}: 第一条失败导致第二条消息未处理");
        Console.WriteLine($"  [PASS] {stage} 异常后通道继续处理下一条");
    }

    private static async Task StopsOnHostCancellation()
    {
        var source = new WaitingSource();
        var binding = new ChannelBinding
        {
            ChannelId = "test", AgentGatewayId = "g", Source = source,
            Sink = new FakeSink(), Enricher = new FakeEnricher(),
        };
        using var worker = new BridgeWorker(new[] { binding }, new Dictionary<string, IAgentGateway> { ["g"] = new FakeGateway(_ => AgentReply.NoMatch) },
            new MessageDispatcher(1), NullLogger<BridgeWorker>.Instance);
        await worker.StartAsync(default);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(deadline.Token);
        if (!worker.ExecuteTask!.IsCompleted) throw new InvalidOperationException("宿主取消未结束通道");
        Console.WriteLine("  [PASS] 宿主取消正常结束通道");
    }

    private sealed class ThrowingSink(bool fail) : IMessageSink
    {
        public List<string> Sent { get; } = new();
        public Task SendAsync(IncomingMessage origin, string text, CancellationToken token)
        {
            if (fail && origin.EventId == "first") throw new InvalidOperationException("send failure");
            Sent.Add(origin.EventId);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingEnricher(bool fail) : IMessageEnricher
    {
        public Task<IncomingMessage> EnrichAsync(IncomingMessage message, CancellationToken token)
        {
            if (fail && message.EventId == "first") throw new InvalidOperationException("enrich failure");
            return Task.FromResult(message);
        }
    }

    private sealed class WaitingSource : IMessageSource
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            yield break;
        }
    }
}
