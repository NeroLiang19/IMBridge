using System.Diagnostics;
using DingTalkBridge.Abstractions;
using DingTalkBridge.Infrastructure.Dws;
using DingTalkBridge.Infrastructure.Process;
using Microsoft.Extensions.Logging.Abstractions;

namespace DingTalkBridge.Tests;

public static class ProcessContractTests
{
    public static async Task RunAsync()
    {
        await RunUnicodeAndExitAsync();
        await RunEnvironmentIsolationAsync();
        await RunCancellationDisposesTreeAsync();
        await DwsReconnectsAndStopsAsync();
    }

    private static async Task RunUnicodeAndExitAsync()
    {
        var result = await new ProcessRunner().RunAsync(new ProcessSpec
        {
            FileName = "powershell.exe", Arguments = ["-NoProfile", "-Command", "$OutputEncoding=[Console]::OutputEncoding=[Text.UTF8Encoding]::new(); Write-Output '你好-流式'"],
        }, CancellationToken.None);
        if (result.ExitCode != 0 || !result.StandardOutput.Contains("你好-流式"))
            throw new InvalidOperationException("真实进程 Unicode/退出契约失败");
        Console.WriteLine("  [PASS] 真实进程 Unicode 与退出码");
    }

    private static async Task RunEnvironmentIsolationAsync()
    {
        const string sentinel = "DINGTALKBRIDGE_SECRET_SENTINEL";
        var old = Environment.GetEnvironmentVariable(sentinel);
        Environment.SetEnvironmentVariable(sentinel, "must-not-inherit");
        try
        {
            var result = await new ProcessRunner().RunAsync(new ProcessSpec
            {
                FileName = "powershell.exe",
                Arguments = ["-NoProfile", "-Command", "$env:DINGTALKBRIDGE_SECRET_SENTINEL + '|' + $env:PATH + '|' + $env:EXPLICIT_TEST_VAR"],
                Environment = new Dictionary<string, string> { ["EXPLICIT_TEST_VAR"] = "explicit-value" }
            }, CancellationToken.None);
            var parts = result.StandardOutput.Trim().Split('|');
            if (parts.Length < 3 || !string.IsNullOrEmpty(parts[0]) || string.IsNullOrEmpty(parts[1]) || parts[2] != "explicit-value")
                throw new InvalidOperationException("进程环境继承契约失败");
            Console.WriteLine("  [PASS] 最小环境继承与显式变量");
        }
        finally { Environment.SetEnvironmentVariable(sentinel, old); }
    }

    private static async Task RunCancellationDisposesTreeAsync()
    {
        var pidFile = Path.Combine(Path.GetTempPath(), $"bridge-pid-{Guid.NewGuid():N}.txt");
        var script = "$p=Start-Process powershell.exe '-NoProfile -Command Start-Sleep -Seconds 30' -PassThru; Set-Content -Path '" + pidFile.Replace("'", "''") + "' -Value ($PID.ToString()+','+$p.Id.ToString()); Start-Sleep -Seconds 30";
        var session = new ProcessRunner().StartStreaming(new ProcessSpec
        {
            FileName = "powershell.exe", Arguments = ["-NoProfile", "-Command", script],
        });
        while (!File.Exists(pidFile)) await Task.Delay(20);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { await session.WaitForExitAsync(cts.Token); throw new InvalidOperationException("取消未触发"); }
        catch (OperationCanceledException) { }
        await session.DisposeAsync();
        await session.DisposeAsync();
        var pids = (await File.ReadAllTextAsync(pidFile)).Split(',').Select(int.Parse).ToArray();
        if (pids.Any(pid => Process.GetProcesses().Any(p => p.Id == pid)))
            throw new InvalidOperationException("Dispose 后进程树仍存活");
        File.Delete(pidFile);
        Console.WriteLine("  [PASS] 取消与 Dispose 回收进程树");
    }

    private static async Task DwsReconnectsAndStopsAsync()
    {
        var runner = new FakeStreamingRunner();
        var source = new DwsMessageSource("dws", "test", ["event"], runner, NullLogger<DwsMessageSource>.Instance);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1200));
        try
        {
            await foreach (var _ in source.ReadMessagesAsync(cts.Token)) { }
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
        if (runner.Starts < 2 || runner.Disposals < 1)
            throw new InvalidOperationException("Dws 异常退出未重连或会话未释放");
        Console.WriteLine("  [PASS] Dws 异常退出重连与取消停止");
    }

    private sealed class FakeStreamingRunner : IStreamingProcessRunner
    {
        public int Starts;
        public int Disposals;
        public IStreamingProcessSession StartStreaming(ProcessSpec spec)
        {
            Starts++;
            if (Starts == 1) throw new IOException("模拟启动失败");
            return new FakeSession(() => Disposals++);
        }
    }

    private sealed class FakeSession(Action disposed) : IStreamingProcessSession
    {
        private readonly StringReader _stdout = new("状态行\n");
        private readonly StringReader _stderr = new("stderr\n");
        public TextReader StandardOutput => _stdout;
        public TextReader StandardError => _stderr;
        public int ExitCode => 1;
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() { disposed(); return ValueTask.CompletedTask; }
    }
}
