using IMBridge.Abstractions;
using IMBridge.Infrastructure.Dws;
using Microsoft.Extensions.Logging;

namespace IMBridge.Infrastructure.Media;

/// <summary>
/// 基于 dws chat message download-media 实现的媒体下载器（钉钉专属）。
/// 单一职责：根据 mediaId 与会话上下文下载图片至临时目录。统一进程执行走 IProcessRunner。
/// </summary>
internal sealed class DwsMediaDownloader(DingtalkOptions options, IProcessRunner processRunner, ILogger<DwsMediaDownloader> logger) : IMediaDownloader
{
    public async Task<string?> DownloadImageAsync(
        string mediaId,
        string messageId,
        string conversationId,
        CancellationToken cancellationToken)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "IMBridge_Media");
        Directory.CreateDirectory(tempDir);
        var outputPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.png");

        var args = new List<string>
        {
            "chat", "message", "download-media",
            "--type", "mediaId",
            "--resource-id", mediaId,
            "--message-id", messageId,
            "--open-conversation-id", conversationId,
            "--output", outputPath,
        };

        logger.LogInformation("[media] 开始下载媒体文件: resource={MediaId}, msg={MsgId}", mediaId, messageId);

        var result = await processRunner.RunAsync(new ProcessSpec
        {
            FileName = options.DwsPath,
            Arguments = args,
            Timeout = TimeSpan.FromSeconds(120),
        }, cancellationToken);

        if (result.ExitCode == 0 && File.Exists(outputPath))
        {
            logger.LogInformation("[media] 媒体下载成功 -> {Path}", outputPath);
            return outputPath;
        }

        logger.LogWarning("[media] 下载媒体退出码 {Code}: stdout={Out}, stderr={Err}", result.ExitCode, result.StandardOutput, result.StandardError);
        return null;
    }
}
