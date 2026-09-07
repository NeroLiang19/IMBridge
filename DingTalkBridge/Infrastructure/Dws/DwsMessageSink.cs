using DingTalkBridge.Abstractions;
using DingTalkBridge.Domain;
using DingTalkBridge.Infrastructure.Process;
using Microsoft.Extensions.Logging;
using System.Text;

namespace DingTalkBridge.Infrastructure.Dws;

/// <summary>
/// 基于 dws chat +messages-send 的消息出口（钉钉专属适配层），以本人身份发送。
/// 通用 ReplyTarget 在这里翻译成钉钉参数：Group -> --group，Direct -> --open-dingtalk-id。
/// </summary>
internal sealed class DwsMessageSink(DingtalkOptions options, IProcessRunner processRunner, ILogger<DwsMessageSink> logger) : IMessageSink
{
    public async Task SendAsync(IncomingMessage origin, string text, CancellationToken cancellationToken)
    {
        var args = new List<string>
        {
            "chat", "+messages-send", "--as", "user", "--yes",
            "--text", text,
            "--uuid", BuildUuid(origin.ChannelId, origin.EventId), // 幂等键防重发
        };
        if (origin.ReplyTarget.Type == ConversationType.Group)
        {
            args.AddRange(["--group", origin.ReplyTarget.TargetId]);
        }
        else
        {
            args.AddRange(["--open-dingtalk-id", origin.ReplyTarget.TargetId]);
        }

        if (options.DryRun)
        {
            logger.LogInformation("[dry-run] 应回复 {Target}: {Text}",
                origin.ReplyTarget.Type == ConversationType.Group ? $"群 {origin.ReplyTarget.TargetId}" : origin.SenderName, text);
            return;
        }

        var result = await processRunner.RunAsync(new ProcessSpec
        {
            FileName = options.DwsPath,
            Arguments = args,
            Timeout = TimeSpan.FromSeconds(60),
        }, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"dws 发送失败 exit={result.ExitCode}, correlation={BuildCorrelation(origin.ChannelId, origin.EventId)}");
        }
        logger.LogInformation("已回复 {Target}", origin.SenderName);
    }

    private static string BuildUuid(string channelId, string eventId)
    {
        var channel = Normalize(channelId);
        var eventKey = Normalize(eventId);
        var payload = $"{channelId.Length}:{channelId}|{eventId.Length}:{eventId}";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16].ToLowerInvariant();
        return $"wb-{channel}-{eventKey}-{hash}";
    }

    private static string BuildCorrelation(string channelId, string eventId)
        => $"{Normalize(channelId)}-{Normalize(eventId)}";

    private static string Normalize(string value)
    {
        var chars = value.Where(char.IsLetterOrDigit).Take(80).ToArray();
        return chars.Length == 0 ? "unknown" : new string(chars);
    }
}
