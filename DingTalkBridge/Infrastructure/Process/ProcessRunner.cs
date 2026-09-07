using System.Diagnostics;
using System.Text;
using DingTalkBridge.Abstractions;

namespace DingTalkBridge.Infrastructure.Process;

/// <summary>
/// 统一有限进程执行器：并发读取 stdout/stderr、UTF8、超时 + 外部取消、取消时杀掉整个进程子树。
/// 所有一次性 CLI 调用（dws 发送/下载、WorkBuddy 处理、视觉识别）都复用它，避免权限/编码/超时逻辑散落各处。
/// 注意：本实现不新增任何 -y 之类的权限绕过参数，也不扩展调用方既有的权限范围。
/// </summary>
internal sealed class ProcessRunner : IProcessRunner, IStreamingProcessRunner
{
    public IStreamingProcessSession StartStreaming(ProcessSpec spec) => new StreamingProcessSession(StartProcess(spec));

    private System.Diagnostics.Process StartProcess(ProcessSpec spec)
    {
        var psi = new ProcessStartInfo
        {
            FileName = spec.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (spec.WorkingDirectory is not null)
        {
            psi.WorkingDirectory = spec.WorkingDirectory;
        }
        foreach (var arg in spec.Arguments)
        {
            psi.ArgumentList.Add(arg);
        }
        // 默认仅传递启动 CLI 所需的最小宿主环境，避免将敏感变量泄露给 Agent/视觉进程。
        psi.Environment.Clear();
        foreach (var key in new[] { "PATH", "SystemRoot", "WINDIR", "ComSpec", "TEMP", "TMP", "USERPROFILE" })
        {
            var value = System.Environment.GetEnvironmentVariable(key);
            if (value is not null)
                psi.Environment[key] = value;
        }
        if (spec.Environment is not null)
        {
            foreach (var (key, value) in spec.Environment)
            {
                psi.Environment[key] = value;
            }
        }
        return System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException($"无法启动进程: {spec.FileName}");
    }

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // 超时与外部取消共用一个链接令牌；超时单独标记以便区分 TimedOut。
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (spec.Timeout > TimeSpan.Zero)
        {
            cts.CancelAfter(spec.Timeout);
        }

        using var process = StartProcess(spec);

        // 并发读取两条流，直至 EOF（进程退出/被杀后管道关闭即结束）。
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // 等待进程退出（受超时/取消约束）。
        var exitTask = process.WaitForExitAsync(cts.Token);
        try
        {
            await Task.WhenAny(exitTask, Task.Delay(Timeout.Infinite, cts.Token));
            // 观察 WaitForExitAsync 的异常，避免丢失底层进程错误。
            if (exitTask.IsFaulted)
                await exitTask;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
            }
        }

        if (!process.HasExited)
        {
            // 超时或外部取消：杀掉整个子树（已启动的子进程一并清理）。
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // 进程恰好此时退出，忽略。
            }
            // 不带令牌等待真正退出，确保后续读取能拿到完整输出。
            await process.WaitForExitAsync();
        }

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;

        var timedOut = cts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
        cancellationToken.ThrowIfCancellationRequested();
        return new ProcessResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = standardOutput,
            StandardError = standardError,
            TimedOut = timedOut,
        };
    }
}
