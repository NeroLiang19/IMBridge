namespace IMBridge.Abstractions;

public sealed record VisionConfig
{
    public required string Type { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record BridgeOptions
{
    public bool DryRun { get; init; } = true;
    public int MaxConcurrentTasks { get; init; } = 3;
    public required IReadOnlyDictionary<string, ChannelConfig> Channels { get; init; }
    public required IReadOnlyDictionary<string, AgentConfig> Agents { get; init; }
    public required IReadOnlyDictionary<string, VisionConfig> Visions { get; init; }
}
