namespace DingTalkBridge.Abstractions;

/// <summary>
/// 视觉/多模态识别服务接口。
/// 单一职责：根据本地图片提取文本或关键业务单号信息（OCR/视觉理解）。
/// </summary>
public interface IVisualRecognizer
{
    Task<string?> RecognizeAsync(string imagePath, CancellationToken cancellationToken);
}
