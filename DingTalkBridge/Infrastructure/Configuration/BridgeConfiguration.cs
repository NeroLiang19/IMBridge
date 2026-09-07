using DingTalkBridge.Abstractions;
using Microsoft.Extensions.Configuration;

namespace DingTalkBridge.Infrastructure.Configuration;

public static class BridgeConfiguration
{
    public static BridgeOptions Load(IConfiguration config)
    {
        var root = config.GetSection("Bridge");
        var agents = ReadAgents(root.GetSection("Agents"));
        if (agents.Count == 0) agents = ReadLegacyAgent(root);
        var channels = ReadChannels(root.GetSection("Channels"), agents, root);
        return new BridgeOptions
        {
            DryRun = !bool.TryParse(root["DryRun"], out var dryRun) || dryRun,
            MaxConcurrentTasks = int.TryParse(root["MaxConcurrentTasks"], out var max) ? Math.Max(1, max) : 3,
            Agents = agents,
            Channels = channels,
            Visions = ReadVisions(root.GetSection("Visions")),
        };
    }

    private static Dictionary<string, AgentConfig> ReadAgents(IConfigurationSection section)
    {
        var result = new Dictionary<string, AgentConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
            result[child.Key] = new AgentConfig { Type = child["Type"] ?? "workbuddy", Options = Flatten(child) };
        return result;
    }

    private static Dictionary<string, AgentConfig> ReadLegacyAgent(IConfigurationSection root)
    {
        var wb = root.GetSection("WorkBuddy");
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["workbuddy"] = new AgentConfig { Type = wb["Type"] ?? "workbuddy", Options = Flatten(wb, root, "OwnerName") },
        };
    }

    private static Dictionary<string, ChannelConfig> ReadChannels(IConfigurationSection section, IReadOnlyDictionary<string, AgentConfig> agents, IConfigurationSection root)
    {
        var result = new Dictionary<string, ChannelConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
        {
            var type = child["Type"] ?? "dingtalk";
            var options = Flatten(child);
            if (type.Equals("dingtalk", StringComparison.OrdinalIgnoreCase))
                ApplyLegacyDingtalkDefaults(root, options);
            result[child.Key] = new ChannelConfig
            {
                Type = type,
                Agent = child["Agent"] ?? agents.Keys.FirstOrDefault() ?? "workbuddy",
                Vision = child["Vision"],
                Options = options,
            };
        }
        if (result.Count == 0)
        {
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ApplyLegacyDingtalkDefaults(root, options);
            result["dingtalk"] = new ChannelConfig { Type = "dingtalk", Agent = agents.Keys.FirstOrDefault() ?? "workbuddy", Options = options };
        }
        return result;
    }

    private static Dictionary<string, VisionConfig> ReadVisions(IConfigurationSection section)
    {
        var result = new Dictionary<string, VisionConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in section.GetChildren())
            result[child.Key] = new VisionConfig { Type = child["Type"] ?? "workbuddy", Options = Flatten(child) };
        return result;
    }

    private static void ApplyLegacyDingtalkDefaults(IConfigurationSection root, Dictionary<string, string> options)
    {
        foreach (var key in new[] { "DwsPath", "DryRun" })
            if (root[key] is { } value) options.TryAdd(key, value);

        if (!options.Keys.Any(key => key.Equals("EventKeys", StringComparison.OrdinalIgnoreCase) || key.StartsWith("EventKeys:", StringComparison.OrdinalIgnoreCase)))
            foreach (var item in Flatten(root.GetSection("EventKeys")))
                options[$"EventKeys:{item.Key}"] = item.Value;
    }

    private static Dictionary<string, string> Flatten(IConfigurationSection section, IConfigurationSection? extra = null, string? extraKey = null)
    {
        var prefix = section.Path + ":";
        var values = section.AsEnumerable()
            .Where(item => item.Value is not null && item.Key != section.Path)
            .ToDictionary(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? item.Key[prefix.Length..] : item.Key, item => item.Value!, StringComparer.OrdinalIgnoreCase);
        if (extra is not null && extraKey is not null && extra[extraKey] is { } value) values[extraKey] = value;
        return values;
    }
}
