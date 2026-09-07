namespace DingTalkBridge.Domain;

/// <summary>
/// Agent 处理结果分类。协议必须明确区分三种状态，
/// 以便桥接层决定：成功才回复；无匹配与失败都保持沉默（失败绝不把错误输出发给用户）。
/// </summary>
public enum AgentOutcome
{
    /// <summary>技能成功处理，Text 为要发给用户的最终文案。</summary>
    Success,

    /// <summary>没有任何技能匹配，保持沉默，不回复。</summary>
    NoMatch,

    /// <summary>Agent 执行失败（非零退出 / 超时 / 异常），保持沉默，绝不发送错误输出。</summary>
    Failed,
}
