using IMBridge.Domain;

namespace IMBridge.Abstractions;

public interface IChannelAdapterFactory
{
    IMessageSource CreateSource(string channelId, ChannelConfig config);
    IMessageSink CreateSink(ChannelConfig config);
    IMessageEnricher CreateEnricher(ChannelConfig config, IVisualRecognizer? visualRecognizer);
}
