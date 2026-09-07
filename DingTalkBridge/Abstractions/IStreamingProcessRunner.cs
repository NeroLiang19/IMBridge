namespace DingTalkBridge.Abstractions;

/// <summary>常驻流式进程执行接口。</summary>
public interface IStreamingProcessRunner
{
    IStreamingProcessSession StartStreaming(ProcessSpec spec);
}

/// <summary>隐藏底层进程对象的流式会话。</summary>
public interface IStreamingProcessSession : IAsyncDisposable
{
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken = default);
}
