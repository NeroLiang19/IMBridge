using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Text.Json;
using DingTalkBridge.Abstractions;
using DingTalkBridge.Domain;
using DingTalkBridge.Infrastructure.Process;
using DingTalkBridge.Serialization;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Infrastructure.Dws;

/// <summary>
/// 基于 dws event consume 的消息来源（钉钉专属适配层）。
/// 单进程同时订阅全部事件 key（共享 bus，避免多 consumer 互拖），进程退出后指数退避自动重连。
/// </summary>
internal sealed class DwsMessageSource(
    string dwsPath,
    string channelId,
    IReadOnlyList<string> eventKeys,
    IStreamingProcessRunner processRunner,
    ILogger<DwsMessageSource> logger) : IMessageSource
{
    private const string GroupAtEventType = "user_im_message_receive_at";

    public async IAsyncEnumerable<IncomingMessage> ReadMessagesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var backoffMs = 1000;
        while (!cancellationToken.IsCancellationRequested)
        {
            var channel = Channel.CreateUnbounded<IncomingMessage>();
            var sessionTask = RunSessionAsync(channel.Writer, cancellationToken);
            await foreach (var message in channel.Reader.ReadAllAsync(cancellationToken))
            {
                backoffMs = 1000;
                yield return message;
            }

            var failure = await sessionTask;
            if (cancellationToken.IsCancellationRequested)
                yield break;
            if (failure is not null)
                logger.LogWarning(failure, "dws 事件流启动或读取失败，{Backoff}ms 后重连", backoffMs);
            else
                logger.LogWarning("dws 事件流中断，{Backoff}ms 后重连", backoffMs);
            await Task.Delay(backoffMs, cancellationToken);
            backoffMs = Math.Min(backoffMs * 2, 60_000);
        }
    }

    private async Task<Exception?> RunSessionAsync(ChannelWriter<IncomingMessage> writer, CancellationToken cancellationToken)
    {
        var args = new List<string> { "event", "consume" };
        args.AddRange(eventKeys);
        args.AddRange(["--flatten", "-f", "ndjson", "--yes"]);
        logger.LogInformation("启动事件订阅({Channel}): dws {Args}", channelId, string.Join(' ', args));

        IStreamingProcessSession? process = null;
        Task? stderrTask = null;
        Exception? failure = null;
        try
        {
            process = processRunner.StartStreaming(new ProcessSpec { FileName = dwsPath, Arguments = args });
            stderrTask = ConsumeStdErrAsync(process.StandardError, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failure = ex;
                    break;
                }
                if (line is null) break;
                var message = Parse(line, channelId);
                if (message is not null)
                    await writer.WriteAsync(message, cancellationToken);
            }
            if (!cancellationToken.IsCancellationRequested)
                await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            if (stderrTask is not null)
            {
                try { await stderrTask; }
                catch (Exception ex) { failure ??= ex; }
            }
            if (process is not null)
                await process.DisposeAsync();
            writer.TryComplete();
        }
        return failure;
    }

    private IncomingMessage? Parse(string line, string channelId)
    {
        if (!line.StartsWith('{')) return null;
        DwsEvent? evt;
        try { evt = JsonSerializer.Deserialize(line, BridgeJsonContext.Default.DwsEvent); }
        catch (JsonException ex)
        {
            logger.LogWarning("NDJSON 解析失败: {Error} | 原始内容: {Line}", ex.Message, line[..Math.Min(line.Length, 300)]);
            return null;
        }
        if (string.IsNullOrWhiteSpace(evt?.EventId) || string.IsNullOrWhiteSpace(evt.SenderOpenDingTalkId) || string.IsNullOrWhiteSpace(evt.MessageId)) return null;
        var isGroupAt = evt.Type == GroupAtEventType;
        if (isGroupAt && string.IsNullOrWhiteSpace(evt.ConversationId)) return null;
        var replyTarget = isGroupAt
            ? new ReplyTarget { Type = ConversationType.Group, TargetId = evt.ConversationId! }
            : new ReplyTarget { Type = ConversationType.Direct, TargetId = evt.SenderOpenDingTalkId };
        var channelContext = new Dictionary<string, string>
        {
            ["conversationId"] = evt.ConversationId ?? string.Empty,
            ["messageId"] = evt.MessageId ?? string.Empty,
        };
        return new IncomingMessage
        {
            ChannelId = channelId, EventId = evt.EventId, MessageId = evt.MessageId ?? string.Empty,
            EventType = evt.Type ?? string.Empty, SenderName = evt.Sender ?? "同事", Content = evt.Content ?? string.Empty,
            ReplyTarget = replyTarget, ChannelContext = channelContext, Attachments = ImmutableArray<Attachment>.Empty,
        };
    }

    private async Task ConsumeStdErrAsync(TextReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
                logger.LogDebug("[dws] {Line}", line);
        }
        catch (OperationCanceledException) { }
    }
}
