using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services.Moderation;
using Explore.Infrastructure.Services.Moderation;
using Explore.Persistence;
using Explore.Persistence.Repositories;

namespace Event.Persistence.IntegrationTests.Fixtures;

/// <summary>
/// One genuine runtime owner across a native fixture's successive scoped contexts. The primary
/// context must itself use the fixture's real SQLite file; an unrelated file cannot attest an
/// in-memory database. Only the selected external secret resolver may be a test double.
/// </summary>
internal sealed class ModerationRecognitionKeyLifecycleFixture(
    ISecretResolver selectedSecrets, TimeProvider clock) : IDisposable
{
    private readonly ModerationRecognitionKeyLifecycleReplicaOwner replica = new();

    internal Composition Bind(ExploreDbContext context,
        IPrivacyIdentityFenceAuthority identity, IPrivacyErasureAuthority erasure)
    {
        var repository = new ModerationRecognitionKeyLifecycleRepository(context, identity, erasure, replica, clock);
        var provider = new ModerationRecognitionKeyRingProvider(selectedSecrets, clock, repository);
        return new(repository, provider, new ModerationRecognitionKeyLifecycleService(provider, repository,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)), clock));
    }

    public void Dispose() => replica.Dispose();

    internal sealed record Composition(
        ModerationRecognitionKeyLifecycleRepository Repository,
        ModerationRecognitionKeyRingProvider Provider,
        ModerationRecognitionKeyLifecycleService Lifecycle);
}
