using DingTalkBridge.Abstractions;
using DingTalkBridge.Application;
using DingTalkBridge.Infrastructure.Media;
using DingTalkBridge.Infrastructure.Process;
using DingTalkBridge.Infrastructure.Dws;
using DingTalkBridge.Infrastructure.Configuration;
using DingTalkBridge.Infrastructure.WorkBuddy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// ContentRoot 固定为 exe 所在目录，保证 appsettings.json 不受启动时 cwd 影响
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// 配置（手动读取，兼容旧 Bridge 扁平配置，迁移为 Channels/Agents 命名实例 + 显式 Type）
var options = BridgeConfiguration.Load(builder.Configuration);
builder.Services.AddSingleton(options);

builder.Services.AddSingleton(new DingtalkOptions());
builder.Services.AddSingleton<IProcessRunner, ProcessRunner>();
builder.Services.AddSingleton<IStreamingProcessRunner>(sp => (IStreamingProcessRunner)sp.GetRequiredService<IProcessRunner>());

// 适配工厂：按配置显式 Type 映射。新增 IM / Agent 只需在此登记对应工厂；
// 配置中出现未登记的 Type 时，BridgeRegistry 会在启动时报错。
builder.Services.AddSingleton<IReadOnlyDictionary<string, IChannelAdapterFactory>>(sp => new Dictionary<string, IChannelAdapterFactory>(StringComparer.OrdinalIgnoreCase)
{
    ["dingtalk"] = new DingtalkChannelAdapterFactory(
        sp.GetRequiredService<IProcessRunner>(),
        sp.GetRequiredService<IStreamingProcessRunner>(),
        sp.GetRequiredService<ILoggerFactory>()),
});
builder.Services.AddSingleton<IReadOnlyDictionary<string, IAgentAdapterFactory>>(sp => new Dictionary<string, IAgentAdapterFactory>(StringComparer.OrdinalIgnoreCase)
{
    ["workbuddy"] = new WorkBuddyAgentAdapterFactory(
        sp.GetRequiredService<IProcessRunner>(),
        sp.GetRequiredService<ILoggerFactory>()),
});

builder.Services.AddSingleton<IReadOnlyDictionary<string, IVisualRecognizerFactory>>(sp => new Dictionary<string, IVisualRecognizerFactory>(StringComparer.OrdinalIgnoreCase) { ["workbuddy"] = new WorkBuddyVisualRecognizerFactory(sp.GetRequiredService<IProcessRunner>(), sp.GetRequiredService<ILoggerFactory>()) });
builder.Services.AddSingleton<BridgeRegistry>();
builder.Services.AddSingleton<IReadOnlyList<ChannelBinding>>(sp => sp.GetRequiredService<BridgeRegistry>().Channels);
builder.Services.AddSingleton<IReadOnlyDictionary<string, IAgentGateway>>(sp => sp.GetRequiredService<BridgeRegistry>().Agents);

// 应用层调度核心 + 常驻主循环
builder.Services.AddSingleton<MessageDispatcher>(_ => new MessageDispatcher(options.MaxConcurrentTasks));
builder.Services.AddHostedService<BridgeWorker>();

var host = builder.Build();
await host.RunAsync();
