using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Services.Ai;

public readonly struct AiRunStatusResult
{
    public HalResourceOfAiRunDto? Run { get; }
    public bool IsUnauthorized { get; }
    public bool Success => Run is not null;

    private AiRunStatusResult(HalResourceOfAiRunDto? run, bool isUnauthorized)
    {
        Run = run;
        IsUnauthorized = isUnauthorized;
    }

    public static AiRunStatusResult Ok(HalResourceOfAiRunDto run) => new(run, false);
    public static AiRunStatusResult NotFound() => new(null, false);
    public static AiRunStatusResult Unauthorized() => new(null, true);
}
