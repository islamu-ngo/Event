using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using TUnit.Core;

namespace Event.Application.UnitTests.Contracts;

[Category("EventLocationPrivacy")]
public sealed class UserLocationPrivacyErasureRepositoryContractTests
{
    [Test]
    public async Task GlobalErasureRead_ReturnsLocationEntitiesInsteadOfQueryableOrDto()
    {
        var method = typeof(ILocationRepository).GetMethod(
            nameof(ILocationRepository.GetOwnedPrivateHomesForGlobalErasureAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task<List<Location>>));
        await Assert.That(typeof(IQueryable).IsAssignableFrom(method.ReturnType)).IsFalse();
    }

    [Test]
    public async Task ProviderCandidateRead_ReturnsBoundedTypedCandidatesInsteadOfQueryable()
    {
        var method = typeof(IUserPrivacyErasureRepository).GetMethod(
            nameof(IUserPrivacyErasureRepository.GetProviderCandidatesAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType)
            .IsEqualTo(typeof(Task<IReadOnlyList<PrivacyErasureProviderCandidate>>));
        await Assert.That(typeof(IQueryable).IsAssignableFrom(method.ReturnType)).IsFalse();
    }

    [Test]
    public async Task AiConversationGraphDelete_ReturnsTaskAndNotQueryable()
    {
        var method = typeof(IAiConversationRepository).GetMethod(
            nameof(IAiConversationRepository.HardDeleteUserConversationGraphAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task<int>));
        await Assert.That(typeof(IQueryable).IsAssignableFrom(method.ReturnType)).IsFalse();
    }

    [Test]
    public async Task ProviderBackedLocalMetadataWrite_ReturnsTaskAndNotQueryable()
    {
        var method = typeof(IUserPrivacyErasureRepository).GetMethod(
            nameof(IUserPrivacyErasureRepository.EraseProviderBackedLocalUserMetadataAsync));

        await Assert.That(method).IsNotNull();
        await Assert.That(method!.ReturnType).IsEqualTo(typeof(Task));
        await Assert.That(typeof(IQueryable).IsAssignableFrom(method.ReturnType)).IsFalse();
    }
}
