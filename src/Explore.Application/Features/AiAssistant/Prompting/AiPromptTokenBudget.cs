namespace Explore.Application.Features.AiAssistant.Prompting;

public sealed class AiPromptTokenBudget
{
    private AiPromptTokenBudget(int remainingTokens)
    {
        RemainingTokens = remainingTokens;
    }

    public int RemainingTokens { get; private set; }

    public static AiPromptTokenBudget Create(int maxInputTokens)
        => new(Math.Max(0, maxInputTokens));

    public bool CanFit(string? content, IAiTokenEstimator estimator)
        => estimator.CountTokens(content) <= RemainingTokens;

    public bool TryConsume(string? content, IAiTokenEstimator estimator)
    {
        int tokens = estimator.CountTokens(content);
        if (tokens > RemainingTokens)
        {
            return false;
        }

        RemainingTokens -= tokens;
        return true;
    }

    public void ConsumeBestEffort(string? content, IAiTokenEstimator estimator)
        => RemainingTokens = Math.Max(0, RemainingTokens - estimator.CountTokens(content));
}
