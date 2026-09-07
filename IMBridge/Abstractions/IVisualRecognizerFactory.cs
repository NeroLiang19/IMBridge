namespace IMBridge.Abstractions;

public interface IVisualRecognizerFactory
{
    IVisualRecognizer Create(string visionId, VisionConfig config);
}
