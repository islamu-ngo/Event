// ABOUTME: Browser-only proof computation boundary for the generated anonymous challenge contract.
// ABOUTME: Progress contains work counts only and a local solution never grants allocation authority.

using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services;

public interface IAnonymousRegistrationChallengeSolver
{
    Task<string> SolveAsync(HalResourceOfAnonymousRegistrationChallengeDto challenge, Action<int> progress, CancellationToken cancellationToken);
}
