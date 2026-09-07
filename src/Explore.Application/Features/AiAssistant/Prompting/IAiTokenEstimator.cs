namespace Explore.Application.Features.AiAssistant.Prompting;

public interface IAiTokenEstimator
{
    bool IsTokenizerBacked { get; }

    int CountTokens(string? content);
}
