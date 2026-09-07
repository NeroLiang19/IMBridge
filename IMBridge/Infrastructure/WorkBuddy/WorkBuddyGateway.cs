using IMBridge.Abstractions;
using IMBridge.Domain;
using IMBridge.Infrastructure.Process;
using Microsoft.Extensions.Logging;

namespace IMBridge.Infrastructure.WorkBuddy;

/// <summary>
/// 通过 codebuddy CLI 无头模式（-p）把消息交给 WorkBuddy。
/// 约定协议：WorkBuddy 有技能能处理 -> 输出最终回复文案；无技能匹配 -> 只输出 NO_SKILL_MATCHED；
/// 其它（非零退出 / 超时 / 异常）一律视为 Failed，保持沉默且不外发任何错误输出。
/// 统一进程执行走 IProcessRunner；沿用既有 -y（无头模式跳过权限确认），不新增权限绕过、不扩展现有权限范围。
/// </summary>
internal sealed class WorkBuddyGateway(WorkBuddyOptions config, IProcessRunner processRunner, ILogger<WorkBuddyGateway> logger) : IAgentGateway
{
    private const string NoMatchMarker = "NO_SKILL_MATCHED";

    public async Task<AgentReply> AskAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var scene = message.ReplyTarget.Type == ConversationType.Group ? "群聊中@我" : "私聊";
        var prompt =
            $"发送人：{message.SenderName}（{scene}）\n" +
            $"消息内容：{message.Content}\n\n" +
            "请检查你的技能（skills）中是否有能处理这条消息的：\n" +
            "- 如果有：执行对应技能，完成后只输出要回复给发送人的最终文案（不要解释执行过程）。\n" +
            $"- 如果没有任何技能适用：只输出 {NoMatchMarker}，不要输出其他任何内容。";

        var args = new List<string>
        {
            config.Entry,
            "--model", config.Model,
            "-p",
            "-y", // 沿用既有：无头模式无法弹窗审批，跳过权限确认（仅限 MCP 只读查询，不扩展权限）
            "--output-format", "text",
            prompt,
        };

        logger.LogInformation("[gateway] 转发给 WorkBuddy: {Content}", message.Content[..Math.Min(message.Content.Length, 60)]);

        var result = await processRunner.RunAsync(new ProcessSpec
        {
            FileName = config.NodePath,
            Arguments = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = new Dictionary<string, string> { ["SERVER__PORT"] = "0" },
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        }, cancellationToken);

        if (result.TimedOut)
        {
            logger.LogWarning("[gateway] WorkBuddy 处理超时（{Timeout}s），终止", config.TimeoutSeconds);
            return AgentReply.Failed;
        }
        if (result.ExitCode != 0)
        {
            logger.LogWarning("[gateway] codebuddy 退出码 {Code}", result.ExitCode);
            return AgentReply.Failed;
        }

        var text = result.StandardOutput.Trim();
        if (text.Length == 0 || string.Equals(text, NoMatchMarker, StringComparison.Ordinal))
        {
            logger.LogInformation("[gateway] 无技能匹配，保持沉默");
            return AgentReply.NoMatch;
        }
        return AgentReply.Success(text);
    }
}
