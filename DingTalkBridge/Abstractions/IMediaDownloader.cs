namespace DingTalkBridge.Abstractions;

/// <summary>
/// 媒体下载服务接口。
/// 单一职责：根据媒体资源凭证将远程图片/文件下载至本地临时路径。
/// </summary>
public interface IMediaDownloader
{
    Task<string?> DownloadImageAsync(string mediaId, string messageId, string conversationId, CancellationToken cancellationToken);
}
