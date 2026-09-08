namespace Explore.Application.Features.AiAssistant.Prompting;

public sealed class ApproximateAiTokenEstimator : IAiTokenEstimator
{
    private const int AverageCharactersPerToken = 4;

    public bool IsTokenizerBacked => false;

    public int CountTokens(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(content.Trim().Length / (double)AverageCharactersPerToken));
    }
}
