namespace IMBridge.Domain;

/// <summary>Agent 处理完一条消息后的结果，明确区分成功 / 无匹配 / 失败。</summary>
public sealed record AgentReply
{
    public required AgentOutcome Outcome { get; init; }

    /// <summary>
    /// 最终回复文案。仅当 Outcome == Success 时有效且会被发送；
    /// NoMatch / Failed 时必须为 Empty（失败绝不外发错误输出）。
    /// </summary>
    public required string Text { get; init; }

    public static readonly AgentReply NoMatch = new() { Outcome = AgentOutcome.NoMatch, Text = string.Empty };
    public static readonly AgentReply Failed = new() { Outcome = AgentOutcome.Failed, Text = string.Empty };

    public static AgentReply Success(string text) => new() { Outcome = AgentOutcome.Success, Text = text };
}
