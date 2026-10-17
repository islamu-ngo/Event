using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationProviderSubscriptionCustodyTests(PostgreSqlContainerFixture database)
{
    [Test]
    [Arguments(RegistrationProviderSubscriptionOperation.Renewal)]
    [Arguments(RegistrationProviderSubscriptionOperation.Sweep)]
    public async Task NaturalExpiryDuringNativeBindingLookupPreventsHandoffWithoutRowReplacement(
        RegistrationProviderSubscriptionOperation operation)
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, operation);
        RegistrationProviderSubscriptionClaim claim = (await fixture.ClaimAsync(operation)).Single();
        Guid stamp = (await fixture.ReadAsync(claim)).ConcurrencyStamp;
        var barrier = fixture.SuspendBindingLookup();
        Task<bool> work = fixture.ProcessAsync(claim, operation);
        await barrier.Entered;
        fixture.Clock.Set(claim.LeaseExpiresAt);
        barrier.Release();

        await Assert.That(await work).IsFalse();
        await Assert.That(fixture.Adapter.Handoffs).IsEqualTo(0);
        var state = await fixture.ReadAsync(claim);
        await Assert.That(state.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(state.LastRenewalSuccessAt).IsNull();
        await Assert.That(state.LastSweepSuccessAt).IsNull();
        await fixture.RequireNoStagingAsync(claim.TenantId);
    }

    [Test]
    [Arguments(RegistrationProviderSubscriptionOperation.Renewal)]
    [Arguments(RegistrationProviderSubscriptionOperation.Sweep)]
    public async Task ReplacementAfterClaimMetadataAcquisitionCannotUseOldBindingOrSettle(
        RegistrationProviderSubscriptionOperation operation)
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, operation);
        RegistrationProviderSubscriptionClaim original = (await fixture.ClaimAsync(operation)).Single();
        var barrier = fixture.SuspendBindingLookup();
        Task<bool> work = fixture.ProcessAsync(original, operation);
        await barrier.Entered;
        fixture.Clock.Set(original.LeaseExpiresAt);
        RegistrationProviderSubscriptionClaim replacement = (await fixture.ClaimAsync(operation)).Single();
        barrier.Release();

        await Assert.That(await work).IsFalse();
        await Assert.That(fixture.Adapter.Handoffs).IsEqualTo(0);
        await Assert.That(replacement.LeaseToken).IsNotEqualTo(original.LeaseToken);
        await Assert.That(replacement.ProcessingGeneration).IsEqualTo(original.ProcessingGeneration + 1);
        var state = await fixture.ReadAsync(replacement);
        await Assert.That(state.LeaseToken).IsEqualTo(replacement.LeaseToken);
        await Assert.That(state.LastRenewalSuccessAt).IsNull();
        await Assert.That(state.LastSweepSuccessAt).IsNull();
        await fixture.RequireNoStagingAsync(original.TenantId);
    }

    [Test]
    [Arguments(RegistrationProviderSubscriptionOperation.Renewal)]
    [Arguments(RegistrationProviderSubscriptionOperation.Sweep)]
    public async Task ExpiryDuringProviderAwaitRejectsProtectedStagingAndSuccessSettlement(
        RegistrationProviderSubscriptionOperation operation)
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, operation);
        RegistrationProviderSubscriptionClaim claim = (await fixture.ClaimAsync(operation)).Single();
        var barrier = fixture.Adapter.SuspendHandoff();
        Task<bool> work = fixture.ProcessAsync(claim, operation);
        await barrier.Entered;
        fixture.Clock.Set(claim.LeaseExpiresAt);
        barrier.Release();

        await Assert.That(await work).IsFalse();
        await Assert.That(fixture.Adapter.Handoffs).IsEqualTo(1);
        var state = await fixture.ReadAsync(claim);
        await Assert.That(state.LastRenewalSuccessAt).IsNull();
        await Assert.That(state.LastSweepSuccessAt).IsNull();
        if (operation == RegistrationProviderSubscriptionOperation.Renewal)
            await Assert.That(state.FailureCategory).IsEqualTo(RegistrationProviderSubscriptionState.RenewalInDoubtFailureCategory);
        await fixture.RequireNoStagingAsync(claim.TenantId);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task TwoClaimOwnersUseIndependentRuntimeScopesWithoutGlobalPrivateReads(bool boundCaller)
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(
            database, RegistrationProviderSubscriptionOperation.Sweep, tenantCount: 2);
        await fixture.RequireGlobalPrivateDenialsAsync();
        await Assert.That(await fixture.DrainAsync(boundCaller)).IsEqualTo(2);
        await Assert.That(fixture.Adapter.Tenants.Distinct().Count()).IsEqualTo(2);
        await Assert.That(fixture.ResolvedTenantLeases.Distinct().Count()).IsEqualTo(2);
        await fixture.RequireSuccessfulSweepsAsync();
        await fixture.RequireGlobalPrivateDenialsAsync();
    }

    [Test]
    public async Task ExpiryAfterNativeResponseInsertRollsBackMessagePointerAndCheckpointTogether()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Sweep);
        var claim = (await fixture.ClaimAsync(RegistrationProviderSubscriptionOperation.Sweep)).Single();
        var barrier = fixture.SuspendResponseStaging();
        Task<bool> work = fixture.ProcessAsync(claim, RegistrationProviderSubscriptionOperation.Sweep);
        await barrier.Entered;
        fixture.Clock.Set(claim.LeaseExpiresAt);
        barrier.Release();

        await Assert.That(await work).IsFalse();
        await Assert.That(fixture.Adapter.Handoffs).IsEqualTo(1);
        var state = await fixture.ReadAsync(claim);
        await Assert.That(state.LastSweepSuccessAt).IsNull();
        await Assert.That(state.ResponseCheckpoint).IsNull();
        await fixture.RequireNoStagingAsync(claim.TenantId);
    }

    [Test]
    public async Task ClaimTupleSubstitutionCannotAuthorizeRenewalButOriginalTupleCan()
    {
        await using var fixture = await SubscriptionClaimNativeFixture.CreateAsync(database, RegistrationProviderSubscriptionOperation.Renewal);
        var claim = (await fixture.ClaimAsync(RegistrationProviderSubscriptionOperation.Renewal)).Single();
        foreach (var changed in new[]
        {
            claim with { LeaseToken = Guid.CreateVersion7() },
            claim with { ProcessingGeneration = claim.ProcessingGeneration + 1 },
            claim with { LeaseExpiresAt = claim.LeaseExpiresAt.AddMinutes(1) },
            claim with { RegistrationProviderBindingId = Guid.CreateVersion7() },
            claim with { TenantId = Guid.CreateVersion7() }
        })
            await Assert.That(await fixture.ProcessAsync(changed, RegistrationProviderSubscriptionOperation.Renewal)).IsFalse();
        await Assert.That(fixture.Adapter.Handoffs).IsEqualTo(0);
        await Assert.That(await fixture.ProcessAsync(claim, RegistrationProviderSubscriptionOperation.Renewal)).IsTrue();
        var state = await fixture.ReadAsync(claim);
        await Assert.That(state.WatchId).IsEqualTo("renewed-watch");
        await Assert.That(state.LastRenewalSuccessAt).IsEqualTo(fixture.Clock.GetUtcNow().UtcDateTime);
        await Assert.That(state.LeaseToken).IsNull();
    }
}
