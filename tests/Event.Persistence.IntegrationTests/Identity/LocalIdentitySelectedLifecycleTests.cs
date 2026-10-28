using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Identity;

[NotInParallel]
public sealed class LocalIdentitySelectedLifecycleTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActualAuthenticationBorrowsTheSelectedFenceAndCommitsBookkeepingWithoutReleasingTheOuterOwner(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var execution = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var identity = execution.Identity!;
        var before = await identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        long generation = await identity.Set<LocalIdentityEligibility>().Where(row => row.LocalSubjectId == before.Id)
            .Select(row => row.Generation).SingleAsync(fixture.CancellationToken);
        var states = CreateStates(fixture, selected.Services, execution);
        var authentication = fixture.Authentication(selected.Services, states);

        var result = await authentication.AuthenticateAsync(new LocalAuthRequestDto(before.Email!, Guid.CreateVersion7().ToString("N")),
            fixture.CancellationToken);
        await Assert.That(result.Failure).IsEqualTo(LocalAuthFailure.InvalidCredentials);
        int attempts = await identity.Set<LocalIdentityUser>().AsNoTracking().Where(row => row.Id == before.Id)
            .Select(row => row.AccessFailedCount).SingleAsync(fixture.CancellationToken);
        await Assert.That(attempts).IsEqualTo(before.AccessFailedCount + 1);
        var after = await identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == before.Id, fixture.CancellationToken);
        await Assert.That(after.Generation > generation).IsTrue();
        await Assert.That(after.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.Ready);
        RequireOuter(selected.Services);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ActualSupervisedResetPublishesTheNewPointerUnderTheSameRetainedSelectedFence(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var execution = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var operation = await execution.Identity!.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.OperationId, fixture.CancellationToken);
        Guid resetId = Guid.CreateVersion7();
        var result = await CreateStates(fixture, selected.Services, execution).ResetAsync(new LocalCredentialResetRequest(
            operationId: resetId, initiatingApplicationUserId: fixture.Receipt.InitiatingApplicationUserId,
            localSubjectId: fixture.Receipt.LocalSubjectId, expectedCurrentOperationId: operation.Id,
            expectedCurrentOperationConcurrencyStamp: operation.ConcurrencyStamp, reason: "Supervised recovery"), fixture.CancellationToken);
        await Assert.That(result.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        var after = await execution.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(after.CurrentOperationId).IsEqualTo(resetId);
        await Assert.That(after.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
        await Assert.That(after.ExternalLoginId).IsEqualTo(fixture.Receipt.ExternalLoginId);
        RequireOuter(selected.Services);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task RealLifecycleIntakeAndDeliveryAdmissionShareTheSelectedStateStoreFence(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var execution = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        var states = CreateStates(fixture, selected.Services, execution);
        var manager = selected.Services.GetRequiredService<UserManager<LocalIdentityUser>>();
        var user = await execution.Identity!.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        var fence = NativeIdentityRecognitionBoundary.For(execution.Application!, fixture.Clock);
        var lifecycle = new LocalIdentityLifecycleStore(execution.Identity, execution.Application!, manager, fixture.Clock, states, fence);
        var pointer = await lifecycle.BeginAsync(new LocalIdentityLifecycleRequest(user.Id, fixture.Receipt.PersonalActorId,
            fixture.Receipt.ExternalLoginId, LocalIdentityLifecyclePurpose.PasswordRecovery, user.Email!), fixture.CancellationToken);
        await Assert.That(pointer).IsNotNull();
        RequireOuter(selected.Services);
        var delivery = new LocalIdentityLifecycleDeliveryStore(execution.Identity, execution.Application!, states, fixture.Clock, fence);
        Guid? claim = await delivery.TryAdmitAsync(pointer!, fixture.CancellationToken);
        await Assert.That(claim).IsNotNull();
        var row = await execution.Identity.Set<LocalIdentityLifecycleOperation>().AsNoTracking()
            .SingleAsync(value => value.Id == pointer!.OperationId, fixture.CancellationToken);
        await Assert.That(row.DeliveryAttemptCount).IsEqualTo(1);
        await Assert.That(row.DeliveryAdmittedAt).IsNotNull();
        RequireOuter(selected.Services);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, false)]
    [Arguments(IdentityDatabaseTopology.Colocated, true)]
    [Arguments(IdentityDatabaseTopology.External, false)]
    [Arguments(IdentityDatabaseTopology.External, true)]
    public async Task MissingOrDifferentOriginalPairCannotBorrowAValidSelectedLifecycleOwner(
        IdentityDatabaseTopology topology, bool differentPair)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology);
        await using var request = provider.CreateAsyncScope();
        await using var selected = await request.ServiceProvider.GetRequiredService<ILocalIdentityExecutionScopeFactory>()
            .CreateAsync(fixture.CancellationToken);
        var valid = selected.Services.GetRequiredService<LocalIdentityExecutionLease>();
        await using var other = fixture.Provider.CreateAsyncScope();
        var application = differentPair ? other.ServiceProvider.GetRequiredService<ExploreDbContext>() : valid.Application!;
        var identity = differentPair ? fixture.Identity(other) : valid.Identity!;
        var states = new LocalIdentityCredentialStateStore(identity, application,
            (differentPair ? other.ServiceProvider : selected.Services).GetRequiredService<UserManager<LocalIdentityUser>>(),
            fixture.Clock, NativeIdentityRecognitionBoundary.For(application, fixture.Clock),
            differentPair ? valid : new LocalIdentityExecutionLease());
        await Assert.That(async () =>
        {
            await using var unexpected = await states.EnterLifecycleAsync(identity, application, fixture.CancellationToken);
        }).Throws<UnauthorizedAccessException>();
        RequireOuter(selected.Services);
        var row = await valid.Identity!.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(value => value.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(row.CurrentOperationId).IsEqualTo(fixture.Receipt.OperationId);
        await Assert.That(row.State).IsEqualTo(LocalIdentityEligibilityState.ChangeRequired);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LostHumanCustodyAfterActualIdentityWriteMustRefuseBeforeCommitAndRollbackBookkeeping(
        IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalCredentialFirstUseTests.Fixture.CreateAsync(topology);
        await fixture.IssueReadySessionAsync();
        var loss = new ReleaseHumanAfterIdentitySave();
        await using var provider = await LocalIdentityExecutionScopeFactoryTests.ComposeAsync(fixture, topology, [loss]);
        await using var pair = await provider.GetRequiredService<LocalIdentityWriterDbContextFactory>()
            .CreateAsync(null, fixture.CancellationToken);
        await using var selected = provider.CreateAsyncScope();
        var execution = selected.ServiceProvider.GetRequiredService<LocalIdentityExecutionLease>();
        execution.Bind(pair.Application, pair.Identity);
        selected.ServiceProvider.GetRequiredService<ExploreDbContextExecutionLease>().Bind(pair.Application, null);
        // Human cleanup must release only its gate, not close the active bookkeeping transaction.
        await pair.Application.Database.OpenConnectionAsync(fixture.CancellationToken);
        await using var human = new HumanSecuritySourceGate(pair.Application);
        await using var humanLease = await human.AcquireAsync(fixture.CancellationToken);
        await using var local = await LocalIdentityLifecycleGateCustody.EnterAsync(pair.Identity, fixture.CancellationToken);
        pair.TransferApplicationOwnership(selected.ServiceProvider.GetRequiredService<ExploreDbContext>());
        if (pair.Identity is ExternalIdentityDbContext)
            pair.TransferIdentityOwnership(selected.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>());
        var beforeUser = await pair.Identity.Set<LocalIdentityUser>().AsNoTracking()
            .SingleAsync(row => row.Id == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        var before = await pair.Identity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == fixture.Receipt.LocalSubjectId, fixture.CancellationToken);
        var authentication = fixture.Authentication(selected.ServiceProvider,
            CreateStates(fixture, selected.ServiceProvider, execution));
        loss.Arm(pair.Identity, human);

        await Assert.That(async () => await authentication.AuthenticateAsync(
            new LocalAuthRequestDto(beforeUser.Email!, Guid.CreateVersion7().ToString("N")), fixture.CancellationToken))
            .Throws<InvalidOperationException>();
        await Assert.That(loss.Reached).IsTrue();
        await Assert.That(loss.OriginalTransactionSurvivedHumanRelease).IsTrue();
        await Assert.That(pair.Identity.Database.CurrentTransaction).IsNull();
        LocalIdentityLifecycleGateCustody.RequireHeld(pair.Identity);
        await using var observer = fixture.Provider.CreateAsyncScope();
        var observedIdentity = fixture.Identity(observer);
        int attempts = await observedIdentity.Set<LocalIdentityUser>().AsNoTracking()
            .Where(row => row.Id == beforeUser.Id).Select(row => row.AccessFailedCount).SingleAsync(fixture.CancellationToken);
        var after = await observedIdentity.Set<LocalIdentityEligibility>().AsNoTracking()
            .SingleAsync(row => row.LocalSubjectId == before.LocalSubjectId, fixture.CancellationToken);
        await Assert.That(attempts).IsEqualTo(beforeUser.AccessFailedCount);
        await Assert.That(after.Generation).IsEqualTo(before.Generation);
        await Assert.That(after.LockoutUntilUtc).IsEqualTo(before.LockoutUntilUtc);
        await Assert.That(after.CurrentOperationId).IsEqualTo(before.CurrentOperationId);
        await Assert.That(after.PersonalActorId).IsEqualTo(before.PersonalActorId);
        await Assert.That(after.ExternalLoginId).IsEqualTo(before.ExternalLoginId);
        await Assert.That(after.State).IsEqualTo(before.State);
    }

    private sealed class ReleaseHumanAfterIdentitySave : SaveChangesInterceptor
    {
        private DbContext? identity;
        private HumanSecuritySourceGate? human;
        private bool armed;
        internal bool Reached { get; private set; }
        internal bool OriginalTransactionSurvivedHumanRelease { get; private set; }

        internal void Arm(DbContext originalIdentity, HumanSecuritySourceGate originalHuman)
        {
            identity = originalIdentity;
            human = originalHuman;
            armed = true;
        }

        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (!armed || !ReferenceEquals(eventData.Context, identity)) return result;
            armed = false;
            var original = identity!.Database.CurrentTransaction
                ?? throw new InvalidOperationException("The custody breaker requires actual transactional identity bookkeeping.");
            await human!.DisposeAsync();
            LocalIdentityLifecycleGateCustody.RequireHeld(identity);
            OriginalTransactionSurvivedHumanRelease = ReferenceEquals(identity.Database.CurrentTransaction, original)
                && identity.Database.GetDbConnection().State == ConnectionState.Open;
            Reached = true;
            return result;
        }
    }

    private static LocalIdentityCredentialStateStore CreateStates(LocalCredentialFirstUseTests.Fixture fixture,
        IServiceProvider services, LocalIdentityExecutionLease execution) => new(execution.Identity!, execution.Application!,
        services.GetRequiredService<UserManager<LocalIdentityUser>>(), fixture.Clock,
        NativeIdentityRecognitionBoundary.For(execution.Application!, fixture.Clock), execution);

    private static void RequireOuter(IServiceProvider services)
    {
        var execution = services.GetRequiredService<LocalIdentityExecutionLease>();
        execution.RequireCurrent(execution.Application!, execution.Identity!);
        HumanSecuritySourceGate.RequireCurrent(execution.Application!);
        LocalIdentityLifecycleGateCustody.RequireHeld(execution.Identity!);
        if (execution.Application!.Database.CurrentTransaction is not null || execution.Identity!.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("A settled lifecycle attempt retained its transaction.");
    }
}
