using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant.Prompting;
using Explore.Domain.Ai;

namespace Event.Application.UnitTests.Features.AiAssistant.Prompting;

public sealed class AiProviderResponseResolverTests
{
    private const string ValidPayload =
        "{\"title\":\"Draft\",\"participationConfiguration\":{\"participationHandlingModeId\":1,\"advanceRegistrationObligationId\":1}}";

    [Test]
    [Arguments("success")]
    [Arguments("correction")]
    [Arguments("over-limit")]
    [Arguments("second-over-limit")]
    [Arguments("repeat-invalid")]
    public async Task NativeResolverBoundsEachTurnAndTheWholeCorrectionRun(string scenario)
    {
        int calls = 0;
        var provider = new ControlledProvider(() =>
        {
            calls++;
            int count = scenario == "over-limit" || scenario == "second-over-limit" && calls == 2 ? 9 : 8;
            bool valid = scenario == "success" || scenario == "correction" && calls == 2;
            var candidates = Enumerable.Range(0, count).Select(_ =>
                new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, valid ? ValidPayload : "{}")).ToArray();
            return AiChatProviderResult.Success(new AiChatResponse("Draft response", candidates, new(null, null, null)));
        });
        var resolver = new AiProviderResponseResolver(provider, new AiStructuredActionParser());

        var result = await resolver.ResolveAsync(Payload(), CancellationToken.None);

        await Assert.That(calls).IsEqualTo(scenario is "success" or "over-limit" ? 1 : 2);
        await Assert.That(result.Succeeded).IsEqualTo(scenario is "success" or "correction");
        if (result.Succeeded)
            await Assert.That(result.ParseResult!.Actions.Count).IsEqualTo(8);
        else
        {
            await Assert.That(result.ParseResult?.Actions.Count ?? 0).IsEqualTo(0);
            await Assert.That(result.FailureCode).IsEqualTo(scenario == "repeat-invalid"
                ? "missing_tool_argument" : "tool_call_limit_exceeded");
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CancellationBeforeHandoffOrAtProviderReturnStopsParsingAndCorrection(bool cancelAtReturn)
    {
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var provider = new ControlledProvider(() =>
        {
            calls++;
            cancellation.Cancel();
            return AiChatProviderResult.Success(new AiChatResponse("Uncommitted",
                [new AiProposedActionCandidate(AiProposedActionKind.CreateEventDraft, "{}")], new(null, null, null)));
        });
        if (!cancelAtReturn)
            cancellation.Cancel();
        var resolver = new AiProviderResponseResolver(provider, new AiStructuredActionParser());

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.ResolveAsync(Payload(), cancellation.Token));
        await Assert.That(calls).IsEqualTo(cancelAtReturn ? 1 : 0);
    }

    private static AiChatPayload Payload() =>
        new("native", [], null, new(4096, 512, 0m, 5, true, false));

    private sealed class ControlledProvider(Func<AiChatProviderResult> response) : IAiChatProvider
    {
        public Task<AiChatProviderResult> SendAsync(AiChatPayload request, CancellationToken cancellationToken = default) =>
            Task.FromResult(response());
    }
}
