namespace IMBridge.Abstractions;

public sealed record AgentConfig
{
    public required string Type { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
