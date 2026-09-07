namespace DingTalkBridge.Infrastructure.WorkBuddy;

public sealed record WorkBuddyOptions
{
    public string NodePath { get; init; } = @"C:\Program Files\nodejs\node.exe";
    public string Entry { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm", "node_modules", "@tencent-ai", "codebuddy-code", "bin", "codebuddy");
    public string Model { get; init; } = "custom-local:gemini-3.8-flash";
    public string OwnerName { get; init; } = "";
    public string WorkingDirectory { get; init; } = AppContext.BaseDirectory;
    public int TimeoutSeconds { get; init; } = 180;
    public string? VisionPrompt { get; init; }
}
