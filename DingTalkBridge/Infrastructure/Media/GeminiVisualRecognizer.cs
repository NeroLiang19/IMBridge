using DingTalkBridge.Abstractions;
using DingTalkBridge.Infrastructure.WorkBuddy;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Infrastructure.Media;

/// <summary>
/// 基于 WorkBuddy 多模态模型（Gemini 3.8 Flash）实现的视觉识别器（钉钉专属）。
/// 单一职责：从本地图片提取文本与工单号。统一进程执行走 IProcessRunner；
/// 沿用既有 -y（无头模式跳过权限确认），不新增权限绕过、不扩展现有权限范围。
/// </summary>
internal sealed class GeminiVisualRecognizer(WorkBuddyOptions config, IProcessRunner processRunner, ILogger<GeminiVisualRecognizer> logger) : IVisualRecognizer
{
    public async Task<string?> RecognizeAsync(string imagePath, CancellationToken cancellationToken)
    {
        if (!File.Exists(imagePath))
        {
            return null;
        }

        var prompt =
            config.VisionPrompt ??
            $"请仔细阅读并识别本地图片文件：{imagePath}\n" +
            "任务要求：\n" +
            "1. 重点提取其中的生产唯一码、工单码或订单流水号（如 WO-xxxxx 格式编码）。\n" +
            "2. 如果提取到了 WO- 编码，请只返回所有编码（空格分隔），不要输出其他多余文字。\n" +
            "3. 如果没有检测到明确的单号，简要描述图片中的主要文字内容。";

        var args = new List<string>
        {
            config.Entry,
            "--model", config.Model,
            "-p",
            "-y",
            "--output-format", "text",
            prompt,
        };

        logger.LogInformation("[ocr] 调用视觉模型识别图片: {Path}", imagePath);

        var result = await processRunner.RunAsync(new ProcessSpec
        {
            FileName = config.NodePath,
            Arguments = args,
            WorkingDirectory = config.WorkingDirectory,
            Environment = new Dictionary<string, string> { ["SERVER__PORT"] = "0" },
            Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
        }, cancellationToken);

        var text = result.StandardOutput.Trim();
        if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(text))
        {
            logger.LogInformation("[ocr] 识图成功: {Result}", text);
            return text;
        }

        logger.LogWarning("[ocr] 识图退出码 {Code}: {Out}", result.ExitCode, text);
        return null;
    }
}
