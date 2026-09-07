using IMBridge.Abstractions;
using IMBridge.Application;
using IMBridge.Domain;

namespace IMBridge.Tests;

internal static class RoutingContractTests
{
    public static async Task RunAsync()
    {
        var dispatcher = new MessageDispatcher(2);
        var sink = new FakeSink();
        var agents = new Dictionary<string, IAgentGateway> { ["g"] = new FakeGateway(_ => AgentReply.Success("ok")) };
        await dispatcher.DispatchAsync(Message("a:b", "c"), Binding("a:b", sink), agents, null, default);
        await dispatcher.DispatchAsync(Message("a", "b:c"), Binding("a", sink), agents, null, default);
        if (sink.Sent.Count != 2) throw new InvalidOperationException("去重键分隔符碰撞导致不同事件丢失");
        Console.WriteLine("  [PASS] 二元组去重键无分隔符碰撞");

        try
        {
            await dispatcher.DispatchAsync(Message("other", "e"), Binding("a", sink), agents, null, default);
            throw new Exception("错误通道绑定未被拒绝");
        }
        catch (InvalidOperationException) { }
        if (sink.Sent.Count != 2) throw new InvalidOperationException("跨通道消息被发送");
        Console.WriteLine("  [PASS] 不一致的通道绑定拒绝发送");

        try
        {
            await dispatcher.DispatchAsync(Message("a", "tamper"), Binding("a", sink) with { Enricher = new TargetChangingEnricher() }, agents, null, default);
            throw new Exception("富化器改变回复目标未被拒绝");
        }
        catch (InvalidOperationException) { }
        if (sink.Sent.Count != 2) throw new InvalidOperationException("发送了被篡改回复目标的消息");
        Console.WriteLine("  [PASS] 富化器不得改变消息身份或回复目标");
    }

    private sealed class TargetChangingEnricher : IMessageEnricher
    {
        public Task<IncomingMessage> EnrichAsync(IncomingMessage message, CancellationToken token)
            => Task.FromResult(message with { ReplyTarget = message.ReplyTarget with { TargetId = "wrong-receiver" } });
    }

    private static IncomingMessage Message(string channel, string id) => new()
    {
        ChannelId = channel, EventId = id, MessageId = id, EventType = "text", SenderName = "tester", Content = "hello",
        ReplyTarget = new ReplyTarget { Type = ConversationType.Direct, TargetId = "receiver" },
    };

    private static ChannelBinding Binding(string channel, IMessageSink sink) => new()
    {
        ChannelId = channel, AgentGatewayId = "g", Source = new FakeSource(), Sink = sink, Enricher = new FakeEnricher(),
    };
}
