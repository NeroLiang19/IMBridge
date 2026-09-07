namespace IMBridge.Domain;

/// <summary>
/// 通用附件契约。IM 适配层把图片/文件等资源描述成与具体平台无关的附件，
/// 其中 ResourceId 是 IM 平台的媒体标识（如钉钉 mediaId），
/// Context 携带该 IM 下载资源所需的额外上下文（键值对，平台相关，但 Domain 只当不透明字符串处理）。
/// </summary>
public sealed record Attachment
{
    /// <summary>附件种类，如 "image" / "file"。</summary>
    public required string Kind { get; init; }

    /// <summary>IM 平台的媒体资源标识（如钉钉 mediaId）。</summary>
    public required string ResourceId { get; init; }

    /// <summary>可选的原始文件名。</summary>
    public string? Name { get; init; }

    /// <summary>下载该资源所需的 IM 专属上下文（不透明键值对）。</summary>
    public IReadOnlyDictionary<string, string>? Context { get; init; }
}
