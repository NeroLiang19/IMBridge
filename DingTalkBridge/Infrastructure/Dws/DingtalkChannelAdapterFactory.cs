using DingTalkBridge.Abstractions;
using DingTalkBridge.Domain;
using DingTalkBridge.Infrastructure.Media;
using DingTalkBridge.Infrastructure.Process;
using Microsoft.Extensions.Logging;

namespace DingTalkBridge.Infrastructure.Dws;

public sealed class DingtalkChannelAdapterFactory(IProcessRunner processRunner, IStreamingProcessRunner streamingProcessRunner, ILoggerFactory loggerFactory) : IChannelAdapterFactory
{
    static DingtalkOptions GetOptions(ChannelConfig config) => new()
    {
        DwsPath = config.Options.TryGetValue("DwsPath", out var path) ? path : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "dws.exe"),
        DryRun = config.Options.TryGetValue("DryRun", out var dry) && bool.TryParse(dry, out var value) ? value : true,
        EventKeys = config.Options.Where(x => x.Key.StartsWith("EventKeys:", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => ParseEventKeyIndex(x.Key)).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Value).ToArray() is { Length: > 0 } keys ? keys : ["user_im_message_receive_o2o_all", "user_im_message_receive_at"]
    };

    static int ParseEventKeyIndex(string key)
        => int.TryParse(key[(key.IndexOf(':') + 1)..], out var index) ? index : int.MaxValue;

    public IMessageSource CreateSource(string channelId, ChannelConfig config)
    {
        var options = GetOptions(config);
        return new DwsMessageSource(options.DwsPath, channelId, options.EventKeys, streamingProcessRunner, loggerFactory.CreateLogger<DwsMessageSource>());
    }
    public IMessageSink CreateSink(ChannelConfig config) => new DwsMessageSink(GetOptions(config), processRunner, loggerFactory.CreateLogger<DwsMessageSink>());
    public IMessageEnricher CreateEnricher(ChannelConfig config, IVisualRecognizer? visualRecognizer)
        => new DwsMessageEnricher(new DwsMediaDownloader(GetOptions(config), processRunner, loggerFactory.CreateLogger<DwsMediaDownloader>()), visualRecognizer, loggerFactory.CreateLogger<DwsMessageEnricher>());
}
