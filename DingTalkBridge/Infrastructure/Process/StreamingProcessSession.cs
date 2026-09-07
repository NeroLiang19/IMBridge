using System.Text;
using DingTalkBridge.Abstractions;

namespace DingTalkBridge.Infrastructure.Process;

internal sealed class StreamingProcessSession(System.Diagnostics.Process process) : IStreamingProcessSession
{
    private int _disposed;

    public TextReader StandardOutput => process.StandardOutput;
    public TextReader StandardError => process.StandardError;
    public int ExitCode => process.HasExited ? process.ExitCode : -1;

    public Task WaitForExitAsync(CancellationToken cancellationToken = default)
        => process.WaitForExitAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        try { await process.WaitForExitAsync(); } catch (InvalidOperationException) { }
        process.Dispose();
    }
}
