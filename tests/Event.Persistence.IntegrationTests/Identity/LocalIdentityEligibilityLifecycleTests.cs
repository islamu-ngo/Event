using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentityEligibilityLifecycleTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActualActivationAndReplacementPublishOnlyTheirCurrentNonsecretPointer(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        long before;
        await using (var scope = fixture.Provider.CreateAsyncScope())
        {
            await using var source = new ManagedTenantLocalEligibilitySource(fixture.Identity(scope), fixture.Clock);
            await using var lease = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
            await Assert.That(lease.Eligibility.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
            await Assert.That(lease.Eligibility.PersonalActorId).IsEqualTo(fixture.Receipt.PersonalActorId);
            await Assert.That(lease.Eligibility.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
            await Assert.That(lease.Eligibility.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
            before = lease.Eligibility.Generation;
        }
        await fixture.IssueReadySessionAsync();
        await using var currentScope = fixture.Provider.CreateAsyncScope();
        await using var currentSource = new ManagedTenantLocalEligibilitySource(fixture.Identity(currentScope), fixture.Clock);
        await using var current = await currentSource.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(current.Eligibility.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        await Assert.That(current.Eligibility.Generation > before).IsTrue();
        await current.RequireCurrentAsync(fixture.CancellationToken);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActualAuthenticationLockoutAtomicallyDeniesNewWorkerObservation(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var observe = fixture.Provider.CreateAsyncScope();
        var identity = fixture.Identity(observe);
        var before = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        int threshold = observe.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<LocalIdentityUser>>()
            .Options.Lockout.MaxFailedAccessAttempts;
        string invalid = Guid.CreateVersion7().ToString("N");
        LocalAuthResponseDto? result = null;
        for (int attempt = 0; attempt < threshold; attempt++)
            result = await fixture.AuthenticateAsync(invalid);
        await Assert.That(result!.Failure).IsEqualTo(LocalAuthFailure.AccountLocked);
        var after = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        var deadline = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .Where(row => row.Id == fixture.Receipt.LocalSubjectId).Select(row => row.LockoutEnd)
            .SingleAsync(fixture.CancellationToken);
        await Assert.That(after.LockoutUntilUtc).IsNotNull();
        await Assert.That(after.LockoutUntilUtc).IsEqualTo(deadline!.Value.UtcDateTime);
        await Assert.That(after.Generation > before.Generation).IsTrue();
        await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        await using var source = new ManagedTenantLocalEligibilitySource(identity, fixture.Clock);
        await Assert.That(async () =>
        {
            await using var unexpected = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        }).Throws<UnauthorizedAccessException>();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task RetainedWorkerObservationBlocksAuthenticationBookkeepingAndCancellationLeavesItsImageUntouched(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var observer = fixture.Provider.CreateAsyncScope();
        string identifier = (await fixture.Identity(observer).Set<LocalIdentityUser>().AsNoTracking()
            .Where(row => row.Id == fixture.Receipt.LocalSubjectId).Select(row => row.Email)
            .SingleAsync(fixture.CancellationToken))!;
        await using var source = new ManagedTenantLocalEligibilitySource(fixture.Identity(observer), fixture.Clock);
        await using var lease = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await using var attemptScope = fixture.Provider.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var auth = fixture.Authentication(attemptScope).AuthenticateAsync(
            new LocalAuthRequestDto(identifier, Guid.CreateVersion7().ToString("N")),
            cancellation.Token);
        try
        {
            await Assert.That(auth.IsCompleted).IsFalse();
            cancellation.Cancel();
            await Assert.That(async () => await auth).Throws<OperationCanceledException>();
            await lease.RequireCurrentAsync(fixture.CancellationToken);
        }
        finally
        {
            cancellation.Cancel();
            try { await auth; }
            catch (OperationCanceledException) { }
        }
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task NonparticipantBindingTamperCannotReuseAnOtherwiseCurrentRetainedLease(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var owner = fixture.Provider.CreateAsyncScope();
        await using var source = new ManagedTenantLocalEligibilitySource(fixture.Identity(owner), fixture.Clock);
        await using var lease = await source.AcquireAsync(fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await using var nonparticipant = fixture.Provider.CreateAsyncScope();
        Guid replacement = Guid.CreateVersion7();
        int affected = await fixture.Identity(nonparticipant).Set<LocalIdentityEligibility>()
            .Where(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ExternalLoginId, replacement), fixture.CancellationToken);
        await Assert.That(affected).IsEqualTo(1);
        await Assert.That(() => lease.RequireCurrentAsync(fixture.CancellationToken)).Throws<UnauthorizedAccessException>();
    }
}
