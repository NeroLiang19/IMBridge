using DingTalkBridge.Abstractions;
using DingTalkBridge.Infrastructure.Process;
using DingTalkBridge.Infrastructure.WorkBuddy;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Infrastructure.Media;

public sealed class WorkBuddyVisualRecognizerFactory(IProcessRunner runner, ILoggerFactory logs) : IVisualRecognizerFactory
{
    public IVisualRecognizer Create(string visionId, VisionConfig config)
    {
        string Get(string key, string fallback) => config.Options.TryGetValue(key, out var v) ? v : fallback;
        return new GeminiVisualRecognizer(new WorkBuddyOptions
        {
            NodePath = Get("NodePath", @"C:\Program Files\nodejs\node.exe"),
            Entry = Get("Entry", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@tencent-ai", "codebuddy-code", "bin", "codebuddy")),
            Model = Get("Model", "custom-local:gemini-3.8-flash"),
            WorkingDirectory = Get("WorkingDirectory", AppContext.BaseDirectory),
            TimeoutSeconds = int.TryParse(Get("TimeoutSeconds", "180"), out var t) ? t : 180,
            VisionPrompt = config.Options.TryGetValue("Prompt", out var p) ? p : null,
        }, runner, logs.CreateLogger<GeminiVisualRecognizer>());
    }
}
