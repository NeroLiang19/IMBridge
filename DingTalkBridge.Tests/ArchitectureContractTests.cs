using System.Text.RegularExpressions;
using DingTalkBridge.Abstractions;
using DingTalkBridge.Domain;
using DingTalkBridge.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;

namespace DingTalkBridge.Tests;

internal static class ArchitectureContractTests
{
    public static void Run()
    {
        ConfigurationInheritance();
        var project = LocateProject();
        foreach (var layer in new[] { "Domain", "Application", "Abstractions" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(project, layer), "*.cs", SearchOption.AllDirectories))
            {
                var code = StripComments(File.ReadAllText(file));
                if (code.Contains("using DingTalkBridge.Infrastructure", StringComparison.Ordinal) ||
                    code.Contains("System.Diagnostics.Process", StringComparison.Ordinal) ||
                    code.Contains("using Microsoft.Extensions.Configuration", StringComparison.Ordinal))
                    throw new InvalidOperationException($"内层反向依赖基础设施: {file}");
            }
        }
        Console.WriteLine("  [PASS] Domain/Application/Abstractions 无基础设施与配置读取依赖");

        AssertProperties(typeof(AgentConfig), "Type", "Options");
        AssertProperties(typeof(ChannelConfig), "Type", "Agent", "Vision", "Options");
        var enricher = typeof(IChannelAdapterFactory).GetMethod("CreateEnricher")!;
        if (enricher.GetParameters().Any(p => p.ParameterType == typeof(AgentConfig)))
            throw new InvalidOperationException("IM 工厂仍依赖 Agent 配置");
        if (typeof(IProcessRunner).GetMethods().Any(m => m.Name != "RunAsync"))
            throw new InvalidOperationException("有限进程端口混入流式进程职责");
        Console.WriteLine("  [PASS] 通用配置不含厂商字段；IM工厂与有限进程接口隔离");

        foreach (var file in Directory.EnumerateFiles(Path.Combine(project, "Infrastructure", "Dws"), "*.cs", SearchOption.AllDirectories))
        {
            var code = StripComments(File.ReadAllText(file));
            if (code.Contains("new GeminiVisualRecognizer", StringComparison.Ordinal) ||
                code.Contains("using DingTalkBridge.Infrastructure.WorkBuddy", StringComparison.Ordinal) ||
                code.Contains("AgentConfig", StringComparison.Ordinal))
                throw new InvalidOperationException($"IM 适配器越界了解 Agent 实现: {file}");
        }
        Console.WriteLine("  [PASS] 钉钉适配器不构造或依赖 WorkBuddy 视觉实现");
    }

    private static void ConfigurationInheritance()
    {
        var values = new Dictionary<string, string?>
        {
            ["Bridge:DwsPath"] = "root-dws",
            ["Bridge:DryRun"] = "false",
            ["Bridge:EventKeys:0"] = "root-event-0",
            ["Bridge:EventKeys:1"] = "root-event-1",
            ["Bridge:Channels:own:Type"] = "DingTalk",
            ["Bridge:Channels:own:DwsPath"] = "channel-dws",
            ["Bridge:Channels:own:DryRun"] = "true",
            ["Bridge:Channels:own:EventKeys:0"] = "channel-event",
            ["Bridge:Channels:inherited:Type"] = "dingtalk",
            ["Bridge:Channels:other:Type"] = "other",
        };
        var options = BridgeConfiguration.Load(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        var own = options.Channels["own"].Options;
        Require(own["DwsPath"] == "channel-dws" && own["DryRun"] == "true", "Explicit channel options must override legacy defaults");
        Require(own["EventKeys:0"] == "channel-event" && !own.ContainsKey("EventKeys:1"), "Channel event lists must replace rather than merge root lists");
        var inherited = options.Channels["inherited"].Options;
        Require(inherited["DwsPath"] == "root-dws" && inherited["DryRun"] == "false", "Missing channel options must inherit legacy defaults");
        Require(inherited.TryGetValue("EventKeys:0", out var first) && first == "root-event-0" && inherited["EventKeys:1"] == "root-event-1", "Missing event list must inherit root events");
        var other = options.Channels["other"].Options;
        Require(!other.ContainsKey("DwsPath") && !other.ContainsKey("DryRun") && !other.Keys.Any(k => k.StartsWith("EventKeys:", StringComparison.OrdinalIgnoreCase)), "Legacy defaults must not leak into unrelated adapters");
        Console.WriteLine("  [PASS] Channel overrides, legacy defaults and adapter isolation");

        var legacy = BridgeConfiguration.Load(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Bridge:DwsPath"] = "legacy-dws",
            ["Bridge:DryRun"] = "false",
            ["Bridge:EventKeys:0"] = "legacy-event",
            ["Bridge:OwnerName"] = "owner",
            ["Bridge:MaxConcurrentTasks"] = "7",
            ["Bridge:WorkBuddy:Model"] = "legacy-model",
            ["Bridge:Visions:vision:Type"] = "workbuddy",
            ["Bridge:Visions:vision:Token"] = "test-only-value",
        }).Build());
        var migrated = legacy.Channels["dingtalk"].Options;
        Require(migrated.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(new[] { "DwsPath", "DryRun", "EventKeys:0" }), "Legacy migration must copy only DingTalk options");
        Require(migrated["DwsPath"] == "legacy-dws" && migrated["DryRun"] == "false" && migrated["EventKeys:0"] == "legacy-event", "Legacy migration must preserve option values");
        Require(legacy.Agents["workbuddy"].Options["Model"] == "legacy-model" && legacy.Agents["workbuddy"].Options["OwnerName"] == "owner", "Legacy agent migration must remain intact");
        Require(legacy.MaxConcurrentTasks == 7 && !legacy.DryRun && legacy.Visions.ContainsKey("vision"), "Shared configuration must remain intact");
        var empty = BridgeConfiguration.Load(new ConfigurationBuilder().Build());
        Require(empty.DryRun && empty.Channels["dingtalk"].Options.Count == 0, "Empty configuration must preserve safe defaults");
        Console.WriteLine("  [PASS] Legacy migration whitelist and safe defaults");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static string StripComments(string code)
    {
        code = Regex.Replace(code, @"/\\*[\\s\\S]*?\\*/", "", RegexOptions.CultureInvariant);
        code = Regex.Replace(code, @"//[^\\r\\n]*", "", RegexOptions.CultureInvariant);
        return code;
    }

    private static void AssertProperties(Type type, params string[] expected)
    {
        if (!type.GetProperties().Select(p => p.Name).ToHashSet().SetEquals(expected))
            throw new InvalidOperationException($"{type.Name} 出现未约定的通用配置字段");
    }

    private static string LocateProject()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var project = Path.Combine(dir.FullName, "DingTalkBridge");
            if (File.Exists(Path.Combine(project, "DingTalkBridge.csproj"))) return project;
        }
        throw new DirectoryNotFoundException("架构源码约束测试需在包含 DingTalkBridge 源码的工作区运行");
    }
}
