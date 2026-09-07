using System.Collections.Immutable;
using DingTalkBridge.Abstractions;
using DingTalkBridge.Application;
using DingTalkBridge.Domain;

namespace DingTalkBridge.Tests;

/// <summary>
/// 无外部服务的回归测试：验证多通道隔离、多 Agent 路由、no-match/failed 沉默、去重、参数 Unicode。
/// 直接用桥接核心（MessageDispatcher + 假 source/sink/enricher/gateway），不启动任何真实 CLI、不连外部服务。
/// 运行：dotnet run --project DingTalkBridge.Tests
/// </summary>
internal static class Program
{
    private static int Main()
    {
        var scenarios = new List<Func<ScenarioResult>>
        {
            TwoChannelIsolation,
            TwoAgentRouting,
            NoMatchSilent,
            FailedSilent,
            DedupSameChannelEvent,
            ParamUnicode,
            RegistryTwoChannelsIndependentSources,
            RegistryUnknownChannelTypeRejected,
            RegistryUnknownAgentTypeRejected,
            RegistryEnricherDoesNotReceiveAgentConfig,
            RegistrySupportsMinimalAdapterImplementations,
        };

        var failed = 0;
        Console.WriteLine("=== DingTalkBridge 回归测试 ===");
        foreach (var scenario in scenarios)
        {
            var r = scenario();
            var tag = r.Pass ? "PASS" : "FAIL";
            Console.WriteLine($"[{tag}] {r.Name}");
            if (!r.Pass)
            {
                failed++;
                foreach (var detail in r.Details) Console.WriteLine($"        - {detail}");
            }
        }

        RoutingContractTests.RunAsync().GetAwaiter().GetResult();
        ArchitectureContractTests.Run();
        ProcessContractTests.RunAsync().GetAwaiter().GetResult();
        WorkerContractTests.Run();
        Console.WriteLine(failed == 0 ? "ALL PASS" : $"{failed} FAILED");
        return failed == 0 ? 0 : 1;
    }

    // ---- 辅助 ----
    private static IncomingMessage MakeMessage(string channel, string eventId, string content, ConversationType type = ConversationType.Direct)
        => new()
        {
            ChannelId = channel,
            EventId = eventId,
            MessageId = "m-" + eventId,
            EventType = type == ConversationType.Group ? "user_im_message_receive_at" : "user_im_message_receive_o2o_all",
            SenderName = "同事",
            Content = content,
            ReplyTarget = new ReplyTarget { Type = type, TargetId = type == ConversationType.Group ? "conv-" + channel : "sender-" + channel },
            ChannelContext = null,
            Attachments = ImmutableArray<Attachment>.Empty,
        };

    private static ChannelBinding Bind(string channel, IMessageSink sink, IMessageSource? source = null, IMessageEnricher? enricher = null, string agent = "echo")
        => new()
        {
            ChannelId = channel,
            Source = source ?? new FakeSource(),
            Sink = sink,
            Enricher = enricher ?? new FakeEnricher(),
            AgentGatewayId = agent,
        };

    // ---- 场景 1：两通道隔离（含去重键含 ChannelId，不跨通道去重） ----
    private static ScenarioResult TwoChannelIsolation()
    {
        var sinkD = new FakeSink();
        var sinkF = new FakeSink();
        var gateway = new FakeGateway(m => AgentReply.Success($"{m.ChannelId}:{m.Content}"));
        var agents = new Dictionary<string, IAgentGateway> { ["echo"] = gateway };
        var dispatcher = new MessageDispatcher(4);

        var d = MakeMessage("dingtalk", "e1", "helloD");
        var f = MakeMessage("feishu", "e1", "helloF"); // 同 EventId，不同通道
        dispatcher.DispatchAsync(d, Bind("dingtalk", sinkD, agent: "echo"), agents, null, default).Wait();
        dispatcher.DispatchAsync(f, Bind("feishu", sinkF, agent: "echo"), agents, null, default).Wait();

        var details = new List<string>();
        var pass = true;
        if (sinkD.Sent.Count != 1 || sinkD.Sent[0].Text != "dingtalk:helloD")
        {
            pass = false; details.Add($"dingtalk sink 应为 1 条且内容 dingtalk:helloD，实际 {sinkD.Sent.Count} 条");
        }
        if (sinkF.Sent.Count != 1 || sinkF.Sent[0].Text != "feishu:helloF")
        {
            pass = false; details.Add($"feishu sink 应为 1 条且内容 feishu:helloF，实际 {sinkF.Sent.Count} 条");
        }
        if (sinkD.Sent.Any(s => s.Text.Contains("feishu")) || sinkF.Sent.Any(s => s.Text.Contains("dingtalk")))
        {
            pass = false; details.Add("出现跨通道回复");
        }
        return new ScenarioResult("两通道隔离 + 去重键含ChannelId", pass, details);
    }

    // ---- 场景 2：两 Agent 路由（channel A -> wb1 成功，channel B -> wb2 无匹配） ----
    private static ScenarioResult TwoAgentRouting()
    {
        var sinkA = new FakeSink();
        var sinkB = new FakeSink();
        var gateway1 = new FakeGateway(_ => AgentReply.Success("A-reply"));
        var gateway2 = new FakeGateway(_ => AgentReply.NoMatch);
        var agents = new Dictionary<string, IAgentGateway> { ["wb1"] = gateway1, ["wb2"] = gateway2 };
        var dispatcher = new MessageDispatcher(4);

        dispatcher.DispatchAsync(MakeMessage("chA", "e1", "x"), Bind("chA", sinkA, agent: "wb1"), agents, null, default).Wait();
        dispatcher.DispatchAsync(MakeMessage("chB", "e1", "x"), Bind("chB", sinkB, agent: "wb2"), agents, null, default).Wait();

        var details = new List<string>();
        var pass = true;
        if (sinkA.Sent.Count != 1 || sinkA.Sent[0].Text != "A-reply") { pass = false; details.Add("chA 应收到 A-reply"); }
        if (sinkB.Sent.Count != 0) { pass = false; details.Add($"chB 应沉默，实际发送 {sinkB.Sent.Count} 条"); }
        return new ScenarioResult("两 Agent 路由", pass, details);
    }

    // ---- 场景 3：无匹配沉默 ----
    private static ScenarioResult NoMatchSilent()
    {
        var sink = new FakeSink();
        var gateway = new FakeGateway(_ => AgentReply.NoMatch);
        var agents = new Dictionary<string, IAgentGateway> { ["g"] = gateway };
        var dispatcher = new MessageDispatcher(4);
        dispatcher.DispatchAsync(MakeMessage("c", "e1", "x"), Bind("c", sink, agent: "g"), agents, null, default).Wait();
        var pass = sink.Sent.Count == 0;
        var details = pass ? new List<string>() : new List<string> { $"无匹配不应回复，实际 {sink.Sent.Count} 条" };
        return new ScenarioResult("no-match 沉默", pass, details);
    }

    // ---- 场景 4：失败沉默（且不外发错误输出） ----
    private static ScenarioResult FailedSilent()
    {
        var sink = new FakeSink();
        var gateway = new FakeGateway(_ => AgentReply.Failed);
        var agents = new Dictionary<string, IAgentGateway> { ["g"] = gateway };
        var dispatcher = new MessageDispatcher(4);
        dispatcher.DispatchAsync(MakeMessage("c", "e1", "x"), Bind("c", sink, agent: "g"), agents, null, default).Wait();
        var pass = sink.Sent.Count == 0;
        var details = pass ? new List<string>() : new List<string> { $"失败不应回复，实际 {sink.Sent.Count} 条" };
        return new ScenarioResult("failed 沉默", pass, details);
    }

    // ---- 场景 5：去重（同通道同 EventId 仅处理一次） ----
    private static ScenarioResult DedupSameChannelEvent()
    {
        var sink = new FakeSink();
        var gateway = new FakeGateway(m => AgentReply.Success($"ok:{m.Content}"));
        var agents = new Dictionary<string, IAgentGateway> { ["g"] = gateway };
        var dispatcher = new MessageDispatcher(4);
        var msg = MakeMessage("c", "dup", "hi");
        dispatcher.DispatchAsync(msg, Bind("c", sink, agent: "g"), agents, null, default).Wait();
        dispatcher.DispatchAsync(msg, Bind("c", sink, agent: "g"), agents, null, default).Wait();
        var pass = sink.Sent.Count == 1;
        var details = pass ? new List<string>() : new List<string> { $"同事件应只回复 1 次，实际 {sink.Sent.Count} 次" };
        return new ScenarioResult("同通道去重", pass, details);
    }

    // ---- 场景 6：参数 Unicode（中文/emoji 透传） ----
    private static ScenarioResult ParamUnicode()
    {
        var sink = new FakeSink();
        const string unicode = "你好🌟生产唯一码WO-12345-测试";
        var gateway = new FakeGateway(m => AgentReply.Success(m.Content)); // 原样回显内容
        var agents = new Dictionary<string, IAgentGateway> { ["g"] = gateway };
        var dispatcher = new MessageDispatcher(4);
        dispatcher.DispatchAsync(MakeMessage("c", "e1", unicode), Bind("c", sink, agent: "g"), agents, null, default).Wait();
        var pass = sink.Sent.Count == 1 && sink.Sent[0].Text == unicode;
        var details = pass ? new List<string>() : new List<string> { $"Unicode 内容应完整透传，实际 [{sink.Sent.Count}] {string.Join("|", sink.Sent.Select(s => s.Text))}" };
        return new ScenarioResult("参数 Unicode 透传", pass, details);
    }

    // ---- 注册层：两个不同通道 ID，各自独立 source 且使用自身 ChannelId/EventKeys ----
    private static ScenarioResult RegistryTwoChannelsIndependentSources()
    {
        var agents = new Dictionary<string, AgentConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["wb"] = MakeAgent("workbuddy", "custom-local:gemini-3.8-flash"),
        };
        var channels = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["dingtalk-a"] = new ChannelConfig { Type = "dingtalk", Agent = "wb", Options = new Dictionary<string, string> { ["EventKeys:0"] = "k-a1", ["EventKeys:1"] = "k-a2" } },
            ["dingtalk-b"] = new ChannelConfig { Type = "dingtalk", Agent = "wb", Options = new Dictionary<string, string> { ["EventKeys:0"] = "k-b1" } },
        };
        var opts = MakeOptions(channels, agents);
        var chFactory = new FakeChannelFactory();
        var agFactory = new FakeAgentFactory();

        BridgeRegistry reg;
        try { reg = new BridgeRegistry(opts, ChDict(chFactory), AgDict(agFactory), null); }
        catch (Exception ex) { return new ScenarioResult("注册层-两通道独立source", false, new() { $"启动抛错: {ex.Message}" }); }

        var details = new List<string>();
        var pass = true;
        if (chFactory.SourceCalls.Count != 2) { pass = false; details.Add($"应建 2 个独立 source，实际 {chFactory.SourceCalls.Count}"); }
        if (chFactory.SourceCalls.Count == 2)
        {
            var a = chFactory.SourceCalls.First(x => x.ChannelId == "dingtalk-a");
            var b = chFactory.SourceCalls.First(x => x.ChannelId == "dingtalk-b");
            if (!a.EventKeys.SequenceEqual(new[] { "k-a1", "k-a2" })) { pass = false; details.Add("dingtalk-a 的 EventKeys 不符"); }
            if (!b.EventKeys.SequenceEqual(new[] { "k-b1" })) { pass = false; details.Add("dingtalk-b 的 EventKeys 不符"); }
        }
        if (reg.Channels.Count != 2) { pass = false; details.Add($"应注册 2 个通道，实际 {reg.Channels.Count}"); }
        else if (reg.Channels[0].ChannelId == reg.Channels[1].ChannelId) { pass = false; details.Add("两个通道 ChannelId 不应相同"); }
        return new ScenarioResult("注册层-两通道独立source", pass, details);
    }

    // ---- 注册层：未知通道 Type 启动即报错 ----
    private static ScenarioResult RegistryUnknownChannelTypeRejected()
    {
        var agents = new Dictionary<string, AgentConfig>(StringComparer.OrdinalIgnoreCase) { ["wb"] = MakeAgent("workbuddy", "m") };
        var channels = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = new ChannelConfig { Type = "telegram", Agent = "wb", Options = new Dictionary<string, string> { ["EventKeys:0"] = "k" } },
        };
        var opts = MakeOptions(channels, agents);
        var chFactory = new FakeChannelFactory();
        var agFactory = new FakeAgentFactory();
        var threw = false;
        try { _ = new BridgeRegistry(opts, ChDict(chFactory), AgDict(agFactory), null); }
        catch (InvalidOperationException) { threw = true; }
        catch (Exception ex) { return new ScenarioResult("注册层-未知通道Type拒绝", false, new() { $"应抛 InvalidOperationException，实际 {ex.GetType().Name}" }); }
        return new ScenarioResult("注册层-未知通道Type拒绝", threw, threw ? new() : new() { "未知通道类型未报错" });
    }

    // ---- 注册层：未知 Agent Type 启动即报错 ----
    private static ScenarioResult RegistryUnknownAgentTypeRejected()
    {
        var agents = new Dictionary<string, AgentConfig>(StringComparer.OrdinalIgnoreCase) { ["wb"] = MakeAgent("claude", "m") };
        var channels = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase)
        {
            ["x"] = new ChannelConfig { Type = "dingtalk", Agent = "wb", Options = new Dictionary<string, string> { ["EventKeys:0"] = "k" } },
        };
        var opts = MakeOptions(channels, agents);
        var chFactory = new FakeChannelFactory();
        var agFactory = new FakeAgentFactory();
        var threw = false;
        try { _ = new BridgeRegistry(opts, ChDict(chFactory), AgDict(agFactory), null); }
        catch (InvalidOperationException) { threw = true; }
        catch (Exception ex) { return new ScenarioResult("注册层-未知AgentType拒绝", false, new() { $"应抛 InvalidOperationException，实际 {ex.GetType().Name}" }); }
        return new ScenarioResult("注册层-未知AgentType拒绝", threw, threw ? new() : new() { "未知 Agent 类型未报错" });
    }

    // ---- 注册层：IM 富化器不接触 Agent 配置，无显式视觉配置时不绑定视觉能力 ----
    private static ScenarioResult RegistryEnricherDoesNotReceiveAgentConfig()
    {
        var agents = new Dictionary<string, AgentConfig>
        {
            ["wb1"] = MakeAgent("workbuddy", "model-one"),
            ["wb2"] = MakeAgent("workbuddy", "model-two"),
        };
        var channels = new Dictionary<string, ChannelConfig>
        {
            ["a"] = new() { Type = "dingtalk", Agent = "wb1" },
            ["b"] = new() { Type = "dingtalk", Agent = "wb2" },
        };
        var factory = new FakeChannelFactory();
        _ = new BridgeRegistry(MakeOptions(channels, agents), ChDict(factory), AgDict(new FakeAgentFactory()), null);
        var pass = factory.Recognizers.Count == 2 && factory.Recognizers.All(x => x is null);
        return new ScenarioResult("注册层-IM富化器不依赖Agent配置", pass, pass ? new() : new() { "无显式视觉配置却绑定了识别器" });
    }

    private static ScenarioResult RegistrySupportsMinimalAdapterImplementations()
    {
        var options = MakeOptions(
            new Dictionary<string, ChannelConfig> { ["c"] = new() { Type = "minimal", Agent = "a" } },
            new Dictionary<string, AgentConfig> { ["a"] = new() { Type = "minimal" } });
        var channel = new MinimalChannelFactory();
        var agent = new MinimalAgentFactory();
        var vision = new MinimalVisionFactory();
        try
        {
            var registry = new BridgeRegistry(options,
                new Dictionary<string, IChannelAdapterFactory> { ["minimal"] = channel },
                new Dictionary<string, IAgentAdapterFactory> { ["minimal"] = agent },
                new Dictionary<string, IVisualRecognizerFactory> { ["minimal"] = vision });
        var pass = registry.Channels.Count == 1 && registry.Agents.Count == 1 && channel.EnricherCreated && agent.Created;
            return new ScenarioResult("注册层-最小适配器实现即可接入", pass, pass ? new() : new() { "最小抽象实现未完成注册" });
        }
        catch (Exception ex) { return new ScenarioResult("注册层-最小适配器实现即可接入", false, new() { ex.Message }); }
    }

    private static AgentConfig MakeAgent(string type, string model) => new()
    {
        Type = type,
        Options = new Dictionary<string, string>
        {
            ["NodePath"] = "node.exe", ["Entry"] = "entry", ["Model"] = model,
            ["OwnerName"] = "tester", ["WorkingDirectory"] = AppContext.BaseDirectory, ["TimeoutSeconds"] = "60",
        },
    };

    private static BridgeOptions MakeOptions(
        IReadOnlyDictionary<string, ChannelConfig> channels,
        IReadOnlyDictionary<string, AgentConfig> agents) => new()
    {
        DryRun = true,
        MaxConcurrentTasks = 3,
        Channels = channels,
        Agents = agents,
        Visions = new Dictionary<string, VisionConfig>(),
    };

    // 测试用：单元素 type->factory 字典（按显式接口类型构造，满足注册层参数）
    private static Dictionary<string, IChannelAdapterFactory> ChDict(IChannelAdapterFactory factory)
        => new(StringComparer.OrdinalIgnoreCase) { ["dingtalk"] = factory };
    private static Dictionary<string, IAgentAdapterFactory> AgDict(IAgentAdapterFactory factory)
        => new(StringComparer.OrdinalIgnoreCase) { ["workbuddy"] = factory };
}

internal sealed record ScenarioResult(string Name, bool Pass, List<string> Details);

// ---- 假实现（不触发任何真实 CLI / 外部服务） ----
internal sealed class FakeSource : IMessageSource
{
    private readonly IncomingMessage[] _msgs;
    public FakeSource(params IncomingMessage[] msgs) => _msgs = msgs;
    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken ct)
    {
        foreach (var m in _msgs) { if (ct.IsCancellationRequested) yield break; yield return m; }
        await System.Threading.Tasks.Task.CompletedTask;
    }
}

internal sealed class FakeSink : IMessageSink
{
    public readonly List<(string Channel, string Target, string Text)> Sent = new();
    public System.Threading.Tasks.Task SendAsync(IncomingMessage origin, string text, System.Threading.CancellationToken ct)
    {
        Sent.Add((origin.ChannelId, origin.ReplyTarget.TargetId, text));
        return System.Threading.Tasks.Task.CompletedTask;
    }
}

internal sealed class FakeEnricher : IMessageEnricher
{
    public System.Threading.Tasks.Task<IncomingMessage> EnrichAsync(IncomingMessage message, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.FromResult(message);
}

internal sealed class FakeGateway : IAgentGateway
{
    private readonly Func<IncomingMessage, AgentReply> _fn;
    public FakeGateway(Func<IncomingMessage, AgentReply> fn) => _fn = fn;
    public System.Threading.Tasks.Task<AgentReply> AskAsync(IncomingMessage message, System.Threading.CancellationToken ct)
        => System.Threading.Tasks.Task.FromResult(_fn(message));
}

// 注册层测试用的假工厂：记录通道选项与窄视觉能力，不接触 Agent 配置
internal sealed class FakeChannelFactory : IChannelAdapterFactory
{
    public readonly List<(string ChannelId, string[] EventKeys)> SourceCalls = new();
    public readonly List<IVisualRecognizer?> Recognizers = new();
    public int SinkCalls;

    public IMessageSource CreateSource(string channelId, ChannelConfig config)
    {
        SourceCalls.Add((channelId, config.Options.Where(x => x.Key.StartsWith("EventKeys:", StringComparison.Ordinal)).OrderBy(x => x.Key).Select(x => x.Value).ToArray()));
        return new FakeSource();
    }
    public IMessageSink CreateSink(ChannelConfig config)
    {
        SinkCalls++;
        return new FakeSink();
    }
    public IMessageEnricher CreateEnricher(ChannelConfig config, IVisualRecognizer? visualRecognizer)
    {
        Recognizers.Add(visualRecognizer);
        return new FakeEnricher();
    }
}

internal sealed class FakeAgentFactory : IAgentAdapterFactory
{
    public int Calls;
    public IAgentGateway CreateGateway(string agentId, AgentConfig config)
    {
        Calls++;
        return new FakeGateway(_ => AgentReply.Success("x"));
    }
}

internal sealed class MinimalChannelFactory : IChannelAdapterFactory
{
    public bool EnricherCreated { get; private set; }
    public IMessageSource CreateSource(string channelId, ChannelConfig config) => new FakeSource();
    public IMessageSink CreateSink(ChannelConfig config) => new FakeSink();
    public IMessageEnricher CreateEnricher(ChannelConfig config, IVisualRecognizer? visualRecognizer) { EnricherCreated = true; return new FakeEnricher(); }
}

internal sealed class MinimalAgentFactory : IAgentAdapterFactory
{
    public bool Created { get; private set; }
    public IAgentGateway CreateGateway(string agentId, AgentConfig config) { Created = true; return new FakeGateway(_ => AgentReply.NoMatch); }
}

internal sealed class MinimalVisionFactory : IVisualRecognizerFactory
{
    public IVisualRecognizer Create(string visionId, VisionConfig config) => new MinimalVision();
}

internal sealed class MinimalVision : IVisualRecognizer
{
    public Task<string?> RecognizeAsync(string filePath, CancellationToken cancellationToken) => Task.FromResult<string?>(string.Empty);
}

