using System.Collections.Immutable;
using System.Text.RegularExpressions;
using IMBridge.Abstractions;
using IMBridge.Domain;
using Microsoft.Extensions.Logging;

namespace IMBridge.Infrastructure.Dws;

/// <summary>
/// 钉钉消息富化器：把原本在 BridgeWorker 中的媒体处理（解析 mediaId 正则 -> 下载图片 -> 视觉识别 -> 合成文本）下沉到此处。
/// 正则、sender id、media 等钉钉专属细节只存在于 Dws 适配层，Domain/应用层保持 IM 无关。
/// </summary>
internal sealed partial class DwsMessageEnricher(
    IMediaDownloader mediaDownloader,
    IVisualRecognizer? visualRecognizer,
    ILogger<DwsMessageEnricher> logger) : IMessageEnricher
{
    [GeneratedRegex(@"mediaId=([^\)\s]+)")]
    private static partial Regex MediaIdRegex();

    public async Task<IncomingMessage> EnrichAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        // 仅在内容里出现钉钉图片 mediaId 时才做媒体处理。
        var mediaMatch = MediaIdRegex().Match(message.Content);
        if (!mediaMatch.Success || string.IsNullOrWhiteSpace(message.MessageId))
        {
            return message;
        }

        var mediaId = mediaMatch.Groups[1].Value;
        var conversationId = message.ChannelContext is not null && message.ChannelContext.TryGetValue("conversationId", out var cid)
            ? cid
            : string.Empty;

        var localImage = await mediaDownloader.DownloadImageAsync(mediaId, message.MessageId, conversationId, cancellationToken);
        if (string.IsNullOrEmpty(localImage))
        {
            return message;
        }

        if (visualRecognizer is null)
        {
            return message;
        }
        var ocrResult = await visualRecognizer.RecognizeAsync(localImage, cancellationToken);

        // 识别完成后清理临时图片文件，避免占用磁盘。
        try { File.Delete(localImage); } catch { }

        if (string.IsNullOrWhiteSpace(ocrResult))
        {
            return message;
        }

        logger.LogInformation("[msg-enriched] 图片转文本成功: {Result}", ocrResult);
        var attachment = new Attachment { Kind = "image", ResourceId = mediaId, Name = $"dingtalk-media:{mediaId}" };
        return message with
        {
            Content = ocrResult,
            Attachments = message.Attachments.Add(attachment),
        };
    }
}
