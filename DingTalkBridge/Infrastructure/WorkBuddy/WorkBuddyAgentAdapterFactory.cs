using DingTalkBridge.Abstractions;
using DingTalkBridge.Infrastructure.Process;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Infrastructure.WorkBuddy;

/// <summary>WorkBuddy Agent 适配工厂（Agent 类型 "workbuddy"）。按配置创建 WorkBuddy 网关实例。</summary>
public sealed class WorkBuddyAgentAdapterFactory(IProcessRunner processRunner, ILoggerFactory loggerFactory) : IAgentAdapterFactory
{
    public IAgentGateway CreateGateway(string agentId, AgentConfig config)
        => new WorkBuddyGateway(new WorkBuddyOptions
        {
            NodePath = Get(config, "NodePath", @"C:\Program Files\nodejs\node.exe"),
            Entry = Get(config, "Entry", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@tencent-ai", "codebuddy-code", "bin", "codebuddy")),
            Model = Get(config, "Model", "custom-local:gemini-3.8-flash"),
            OwnerName = Get(config, "OwnerName", ""),
            WorkingDirectory = Get(config, "WorkingDirectory", AppContext.BaseDirectory),
            TimeoutSeconds = int.TryParse(Get(config, "TimeoutSeconds", "180"), out var t) ? t : 180,
        }, processRunner, loggerFactory.CreateLogger<WorkBuddyGateway>());

    static string Get(AgentConfig config, string key, string fallback) => config.Options.TryGetValue(key, out var value) ? value : fallback;
}
