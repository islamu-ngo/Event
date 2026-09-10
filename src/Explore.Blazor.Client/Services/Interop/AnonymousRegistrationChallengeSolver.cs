
using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Services.Interop;

public sealed class AnonymousRegistrationChallengeSolver(IJSRuntime js) : IAnonymousRegistrationChallengeSolver
{
    public async Task<string> SolveAsync(HalResourceOfAnonymousRegistrationChallengeDto challenge, Action<int> progress, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(95));
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", timeout.Token,
            "./js/anonymous-registration-challenge-interop.js");
        await using var solver = await module.InvokeAsync<IJSObjectReference>("createSolver", timeout.Token);
        using var callback = DotNetObjectReference.Create(new ProgressCallback(progress));
        try
        {
            return await solver.InvokeAsync<string>("solve", timeout.Token, challenge, callback);
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            await solver.InvokeVoidAsync("cancel", cleanup.Token);
        }
    }

    public sealed class ProgressCallback(Action<int> progress)
    {
        [JSInvokable]
        public void ReportProgress(int attempts) => progress(attempts);
    }
}
