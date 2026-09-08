// ABOUTME: Verifies the native Blazor-to-worker bridge forwards generated work and numeric progress.
// ABOUTME: Uses exact invocation signals to prove worker cleanup finishes before success or failure escapes.

using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Services.Interop;
using Microsoft.JSInterop;

namespace Explore.Blazor.Client.Tests.Services;

public sealed class AnonymousRegistrationChallengeSolverTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task WorkerResultOrFailureCannotEscapeBeforeCleanup(bool fail)
    {
        using var context = new BlazorTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var module = context.JSInterop.SetupModule("./js/anonymous-registration-challenge-interop.js");
        var worker = module.SetupModule("createSolver", _ => true);
        HalResourceOfAnonymousRegistrationChallengeDto? submitted = null;
        DotNetObjectReference<AnonymousRegistrationChallengeSolver.ProgressCallback>? callback = null;
        var solve = worker.Setup<string>("solve", invocation =>
        {
            submitted = (HalResourceOfAnonymousRegistrationChallengeDto)invocation.Arguments[0]!;
            callback = (DotNetObjectReference<AnonymousRegistrationChallengeSolver.ProgressCallback>)invocation.Arguments[1]!;
            entered.TrySetResult();
            return true;
        });
        var cleanup = worker.SetupVoid("cancel", _ => { cleanupEntered.TrySetResult(); return true; });
        var challenge = new HalResourceOfAnonymousRegistrationChallengeDto
        {
            ProtectedChallenge = Guid.NewGuid().ToString("N"), ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2), Difficulty = 18, Version = 1
        };
        var progress = new List<int>();
        var operation = new AnonymousRegistrationChallengeSolver(context.JSInterop.JSRuntime)
            .SolveAsync(challenge, progress.Add, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callback!.Value.ReportProgress(1024);
        await Assert.That(ReferenceEquals(submitted, challenge)).IsTrue();
        await Assert.That(progress.Single()).IsEqualTo(1024);
        if (fail) solve.SetException(new JSException("challenge-unavailable"));
        else solve.SetResult("0000000000000000");
        await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(operation.IsCompleted).IsFalse();
        cleanup.SetVoidResult();
        if (fail) await Assert.ThrowsAsync<JSException>(async () => await operation);
        else await Assert.That((await operation) == "0000000000000000").IsTrue();
    }
}
