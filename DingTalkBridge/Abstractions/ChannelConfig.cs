namespace DingTalkBridge.Abstractions;

public sealed record ChannelConfig
{
    public required string Type { get; init; }
    public required string Agent { get; init; }
    public string? Vision { get; init; }
    public IReadOnlyDictionary<string, string> Options { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
