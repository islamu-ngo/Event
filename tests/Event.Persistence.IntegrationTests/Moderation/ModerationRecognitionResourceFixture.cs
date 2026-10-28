using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>
/// Resource fixtures without retained capture use the actual primary-store reader.
/// Only external key/retained-authority ports are unavailable doubles; a reference-bearing
/// fixture must supply its genuine retained authority rather than accepting a mock receipt.
/// </summary>
internal static class ModerationRecognitionResourceFixture
{
    internal static IModerationRecognitionContinuityReader Reader(ExploreDbContext context)
    {
        var keys = Substitute.For<IModerationRecognitionKeyRingProvider>();
        keys.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<ModerationRecognitionKeyRing>(
                new InvalidOperationException("Terminal continuity must not recompute provider identifiers.")));
        var retained = Substitute.For<IPrivacyErasureAuthority>();
        retained.ReadAfterAsync(Arg.Any<long>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException<IReadOnlyList<PrivacyErasureIntent>>(
                new InvalidOperationException("A capture-bearing fixture requires its real retained authority.")));
        var identity = Substitute.For<IPrivacyIdentityFenceAuthority>();
        return new ModerationRecognitionContinuity(
            new ModerationRecognitionErasureRepository(context, keys, TimeProvider.System,
                identity, retained, new ModerationRecognitionKeyLifecycleRepository(context, identity, retained,
                    new ModerationRecognitionKeyLifecycleReplicaOwner(), TimeProvider.System)),
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
    }
}
