using System.Text;

namespace DingTalkBridge.Abstractions;

/// <summary>一次性（有限时长）进程的执行规格。所有 CLI 调用统一走它，避免各处分散实现。</summary>
public sealed record ProcessSpec
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public string? WorkingDirectory { get; init; }

    /// <summary>额外环境变量（如 SERVER__PORT）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }

    /// <summary>最长执行时间；超过则取消并杀掉整个进程子树。默认 0 表示不额外加超时（仅受外部取消约束）。</summary>
    public TimeSpan Timeout { get; init; }
}

/// <summary>一次性进程的执行结果。</summary>
public sealed record ProcessResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }

    /// <summary>true = 因超时被取消（区别于外部传入的取消）。</summary>
    public required bool TimedOut { get; init; }
}

/// <summary>
/// 统一有限进程执行接口：并发读取 stdout/stderr、UTF8、支持超时与外部取消、取消/超时杀掉整个进程子树。
/// 注意：常驻流式进程（如 dws event consume）不使用本接口，本接口只面向"有限时长"的一次性 CLI 调用。
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}
