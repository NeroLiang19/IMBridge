namespace DingTalkBridge.Infrastructure.Dws;

public sealed record DingtalkOptions
{
    public string DwsPath { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "dws.exe");
    public bool DryRun { get; init; } = true;
    public IReadOnlyList<string> EventKeys { get; init; } = ["user_im_message_receive_o2o_all", "user_im_message_receive_at"];
}
