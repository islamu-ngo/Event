using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Domain.Constants;
using Microsoft.Extensions.DependencyInjection;
using Wire = ISLAMU.Wire.Contracts.Learning;

namespace Event.Persistence.IntegrationTests.Learning;

[NotInParallel("Environment")]
public sealed class LearningNativeSettlementTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalHumanPreferencesCompleteAndPersistOneRevision(bool embeddedPrivacyAuthority)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = timeout.Token;
        await using var authentication = await AuthenticatedSettingsFixture.CreateSqliteAsync(
            embeddedPrivacyAuthority: embeddedPrivacyAuthority);
        Guid tenant = PlatformDefaults.DefaultTenantId;
        await authentication.RunAsync(tenant, async session =>
        {
            var preparation = session.Services.GetRequiredService<IHumanLearningMutationPreparation>();
            var result = await preparation.ExecuteAsync(new UpdateLearningPreferencesCommand(
                new Wire.LearningUpdatePreferencesRequest
                {
                    ExpectedScopeStamp = LearningAccessPolicy.ScopeStamp(tenant, session.ActorId),
                    ExpectedRevision = 0,
                    SuppressAutomaticTours = true,
                    HideLearning = false,
                    PinLearning = true
                }), token);
            await Assert.That(result.State!.Revision).IsEqualTo(1);
            return true;
        }, cancellationToken: token);

        await authentication.RunAsync(tenant, async session =>
        {
            var state = await session.Services.GetRequiredService<ILearningRepository>()
                .GetStateAsync(tenant, session.ActorId, tracked: false, token);
            await Assert.That(state).IsNotNull();
            await Assert.That(state!.Revision).IsEqualTo(1);
            await Assert.That(state.SuppressAutomaticTours).IsTrue();
            await Assert.That(state.IsPinned).IsTrue();
            return true;
        }, cancellationToken: token);
    }
}
