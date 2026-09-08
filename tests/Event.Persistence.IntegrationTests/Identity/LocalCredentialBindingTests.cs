
using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Application.Features.Authentication.Local.Handlers.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalCredentialBindingTests
{
    public enum RejectedAuthority
    {
        TenantAdministrator,
        RevokedPlatformAdministrator
    }

    public enum ActivationWriteBoundary
    {
        OperationUpdated,
        TokenUpdated
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReconciliationBindsExactGraphAndReplaysWithoutChangingCredentials(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();

        BaseCommandResponse<Guid> first = await fixture.ReconcileAsync();

        await Assert.That(first.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser activated = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(activated.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(activated.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        BaseCommandResponse<Guid> replay = await fixture.ReconcileAsync();
        await Assert.That(replay.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, activated.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, activated.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ApplicationGraphFailureRollsBackWithoutAdvancingIdentity(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        fixture.Faults.RejectGraph = true;

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ReconcileAsync());

        await Assert.That(fixture.Faults.GraphFaultObserved).IsTrue();
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task CommittedApplicationGraphSurvivesActivationFailureAndFreshRetry(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        fixture.ActivationCommitFault.Enabled = true;

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ReconcileAsync());

        await Assert.That(fixture.ActivationCommitFault.Observed).IsTrue();
        await Assert.That(fixture.ActivationCommitFault.CommitsObserved).IsEqualTo(2);
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser failed = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(failed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(failed.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(failed.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        BaseCommandResponse<Guid> retry = await fixture.ReconcileAsync();
        await Assert.That(retry.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That((await fixture.ReconcileAsync()).IsSuccess).IsTrue();
        LocalIdentityUser replayed = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(replayed.SecurityStamp, after.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(replayed.ConcurrencyStamp, after.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(replayed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PendingCredentialCannotActivateBeforeExactApplicationBinding(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        LocalIdentityCredentialStateStore store = fixture.StateStore(scope);
        LocalCredentialProvisioningSnapshot snapshot = (await store.ReadProvisioningAsync(
            fixture.Receipt.OperationId, fixture.CancellationToken))!;

        LocalCredentialActivationOutcome result = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp), fixture.CancellationToken);

        await Assert.That(result).IsEqualTo(LocalCredentialActivationOutcome.BindingIncomplete);
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, RejectedAuthority.TenantAdministrator)]
    [Arguments(IdentityDatabaseTopology.External, RejectedAuthority.TenantAdministrator)]
    [Arguments(IdentityDatabaseTopology.Colocated, RejectedAuthority.RevokedPlatformAdministrator)]
    [Arguments(IdentityDatabaseTopology.External, RejectedAuthority.RevokedPlatformAdministrator)]
    public async Task ReconciliationRequiresCurrentPlatformRoleDespiteTenantAuthorityOrCachedApproval(
        IdentityDatabaseTopology topology, RejectedAuthority authority)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        AdminContext admin = fixture.Admin(request);
        if (authority == RejectedAuthority.RevokedPlatformAdministrator)
        {
            await Assert.That(await admin.IsInstanceAdminAsync(fixture.CancellationToken)).IsTrue();
        }
        await fixture.RemovePlatformRoleAsync();
        if (authority == RejectedAuthority.TenantAdministrator)
        {
            Guid tenantId = await fixture.GrantTenantAdministratorAsync();
            await Assert.That(await admin.IsTenantAdminAsync(tenantId, fixture.CancellationToken)).IsTrue();
        }
        else
        {
            await Assert.That(await admin.IsInstanceAdminAsync(fixture.CancellationToken)).IsTrue();
        }

        BaseCommandResponse<Guid> result = await fixture.Handler(request, admin).Handle(
            new ReconcileLocalCredentialOperationCommand(operationId: fixture.Receipt.OperationId), fixture.CancellationToken);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }

    [Test]
    public async Task RemovedExternalLoginCannotRetainCachedAdministratorIdentity()
    {
        await using Fixture fixture = await Fixture.CreateAsync(IdentityDatabaseTopology.Colocated);
        const string issuer = "https://identity.example.test/realms/native";
        string subject = Guid.CreateVersion7().ToString("D");
        Guid externalLoginId = Guid.CreateVersion7();
        await using (AsyncServiceScope seed = fixture.Provider.CreateAsyncScope())
        {
            ExploreDbContext application = fixture.Application(seed);
            application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = externalLoginId,
                UserId = fixture.InitiatorId,
                User = await application.Users.SingleAsync(user => user.Id == fixture.InitiatorId, fixture.CancellationToken),
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(issuer: issuer, subject: subject).Value,
                CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(fixture.CancellationToken);
        }
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim("sub", subject), new Claim("iss", issuer), new Claim("auth_provider", "keycloak")
        ], "Bearer"));
        AdminContext admin = fixture.Admin(request, principal);
        await Assert.That(await admin.ResolveUserIdAsync(fixture.CancellationToken)).IsEqualTo(fixture.InitiatorId);
        await using (AsyncServiceScope revoke = fixture.Provider.CreateAsyncScope())
        {
            ExploreDbContext application = fixture.Application(revoke);
            application.UserExternalLogins.Remove(await application.UserExternalLogins.SingleAsync(
                login => login.Id == externalLoginId, fixture.CancellationToken));
            await application.SaveChangesAsync(fixture.CancellationToken);
            await Assert.That(await new PlatformUserRoleRepository(application).IsUserPlatformAdmin(fixture.InitiatorId)).IsTrue();
        }

        Guid? resolved = await admin.ResolveUserIdAsync(fixture.CancellationToken);
        BaseCommandResponse<Guid> result = await fixture.Handler(request, admin).Handle(
            new ReconcileLocalCredentialOperationCommand(operationId: fixture.Receipt.OperationId), fixture.CancellationToken);

        await Assert.That(resolved).IsNull();
        await Assert.That(result.FailureCode).IsEqualTo(FailureCodes.AdminRequired);
        await fixture.AssertNoGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task MatchingExternalEmailDoesNotReplaceExactLocalCredentialBinding(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalIdentityUser credential = await fixture.ReadIdentityUserAsync();
        Guid externalUserId = Guid.CreateVersion7();
        Guid externalActorId = Guid.CreateVersion7();
        Guid externalLoginId = Guid.CreateVersion7();
        string externalKey = PlatformIdentityPrincipalExtensions.CreateOidcAccountKey(
            issuer: "https://external.example.test/realms/native", subject: Guid.CreateVersion7().ToString("D")).Value;
        await using (AsyncServiceScope seed = fixture.Provider.CreateAsyncScope())
        {
            ExploreDbContext application = fixture.Application(seed);
            var user = new User
            {
                Id = externalUserId,
                Pii = new UserPii { Email = credential.Email!, FirstName = "External", LastName = "Owner" },
                EmailVerified = true, CreatedAt = DateTime.UtcNow
            };
            application.Actors.Add(new Actor
            {
                Id = externalActorId, UserId = user.Id, User = user,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "External owner" }, CreatedAt = DateTime.UtcNow
            });
            application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = externalLoginId, UserId = user.Id, User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak, AuthenticationProvider = null!,
                ProviderKey = externalKey, CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(fixture.CancellationToken);
        }

        BaseCommandResponse<Guid> result = await fixture.ReconcileAsync();

        await Assert.That(result.IsSuccess).IsTrue();
        await fixture.AssertExactGraphAsync();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        await using AsyncServiceScope observer = fixture.Provider.CreateAsyncScope();
        ExploreDbContext stored = fixture.Application(observer);
        UserExternalLogin external = await stored.UserExternalLogins.SingleAsync(
            login => login.Id == externalLoginId, fixture.CancellationToken);
        await Assert.That(external.UserId).IsEqualTo(externalUserId);
        await Assert.That(external.ProviderKey).IsEqualTo(externalKey);
        await Assert.That((await stored.Actors.SingleAsync(actor => actor.Id == externalActorId, fixture.CancellationToken)).UserId)
            .IsEqualTo(externalUserId);
        await Assert.That(await stored.Users.CountAsync(user => user.Pii.Email == credential.Email, fixture.CancellationToken))
            .IsEqualTo(2);
        await Assert.That(fixture.Receipt.LocalSubjectId == externalUserId).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task StaleOperationStampAndSupersededTokenOwnerCannotActivatePendingCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        LocalIdentityCredentialStateStore store = fixture.StateStore(request);
        LocalCredentialProvisioningSnapshot snapshot = (await store.ReadProvisioningAsync(
            fixture.Receipt.OperationId, fixture.CancellationToken))!;
        Guid currentStamp = Guid.CreateVersion7();
        await using (AsyncServiceScope writer = fixture.Provider.CreateAsyncScope())
        {
            int changed = await fixture.Identity(writer).Set<LocalIdentityCredentialOperation>()
                .Where(operation => operation.Id == snapshot.Receipt.OperationId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(operation => operation.ConcurrencyStamp, currentStamp),
                    fixture.CancellationToken);
            await Assert.That(changed).IsEqualTo(1);
        }

        LocalCredentialActivationOutcome stale = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp), fixture.CancellationToken);

        await Assert.That(stale).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
        await fixture.AssertStateAsync(LocalCredentialState.ProvisioningPending);
        Guid replacementOperationId = Guid.CreateVersion7();
        await using (AsyncServiceScope writer = fixture.Provider.CreateAsyncScope())
        {
            var manager = writer.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D")))!;
            IdentityResult changed = await manager.SetAuthenticationTokenAsync(user,
                LocalCredentialStateMetadata.TokenLoginProvider, LocalCredentialStateMetadata.TokenName,
                JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                    version: LocalCredentialStateMetadata.CurrentVersion, state: LocalCredentialState.ProvisioningPending,
                    operationId: replacementOperationId, applicationUserId: fixture.Receipt.LocalSubjectId)));
            await Assert.That(changed.Succeeded).IsTrue();
        }
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();

        LocalCredentialActivationOutcome superseded = await store.ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: snapshot.Receipt.OperationId,
                expectedOperationConcurrencyStamp: currentStamp), fixture.CancellationToken);

        await Assert.That(superseded).IsEqualTo(LocalCredentialActivationOutcome.Conflict);
        await using AsyncServiceScope observer = fixture.Provider.CreateAsyncScope();
        LocalIdentityUser observedUser = await fixture.ReadIdentityUserAsync();
        LocalCredentialStateMetadata? retained = await fixture.StateStore(observer)
            .ReadAsync(localSubjectId: fixture.Receipt.LocalSubjectId, expectedSecurityStamp: observedUser.SecurityStamp!,
                cancellationToken: fixture.CancellationToken);
        await Assert.That(retained!.OperationId).IsEqualTo(replacementOperationId);
        await Assert.That(retained.State).IsEqualTo(LocalCredentialState.ProvisioningPending);
        LocalIdentityUser after = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ActivationWriteBoundary.OperationUpdated)]
    [Arguments(IdentityDatabaseTopology.Colocated, ActivationWriteBoundary.TokenUpdated)]
    [Arguments(IdentityDatabaseTopology.External, ActivationWriteBoundary.OperationUpdated)]
    [Arguments(IdentityDatabaseTopology.External, ActivationWriteBoundary.TokenUpdated)]
    public async Task FailureBetweenActivationWritesRollsBackEveryIdentityAuthorityRow(
        IdentityDatabaseTopology topology, ActivationWriteBoundary boundary)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser beforeUser = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation beforeOperation = await fixture.ReadOperationAsync();
        string? beforeToken = await fixture.ReadTokenValueAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(request);
        Type targetType = boundary == ActivationWriteBoundary.OperationUpdated
            ? typeof(LocalIdentityCredentialOperation) : typeof(IdentityUserToken<Guid>);
        fixture.ActivationWriteFault.Arm(identity: identity,
            table: identity.Model.FindEntityType(targetType)!.GetTableName()!);

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.StateStore(request).ActivateChangeRequiredAsync(
            new LocalCredentialActivationRequest(operationId: beforeOperation.Id,
                expectedOperationConcurrencyStamp: beforeOperation.ConcurrencyStamp), fixture.CancellationToken));

        await Assert.That(fixture.ActivationWriteFault.Observed).IsTrue();
        await Assert.That(fixture.ActivationWriteFault.AffectedRows).IsEqualTo(1);
        LocalIdentityCredentialOperation afterOperation = await fixture.ReadOperationAsync();
        LocalIdentityUser afterUser = await fixture.ReadIdentityUserAsync();
        await Assert.That(afterOperation.Stage).IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
        await Assert.That(afterOperation.ConcurrencyStamp == beforeOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(afterOperation.UpdatedAt).IsEqualTo(beforeOperation.UpdatedAt);
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), beforeToken, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.PasswordHash, beforeUser.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.SecurityStamp, beforeUser.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterUser.ConcurrencyStamp, beforeUser.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentActivationConvergesWithoutRotatingStampsAgainOnReplay(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation pending = await fixture.ReadOperationAsync();
        var request = new LocalCredentialActivationRequest(operationId: pending.Id,
            expectedOperationConcurrencyStamp: pending.ConcurrencyStamp);

        LocalCredentialActivationOutcome[] outcomes = await fixture.ActivateConcurrentlyAsync(request);

        await Assert.That(outcomes.Count(outcome => outcome == LocalCredentialActivationOutcome.Activated)).IsEqualTo(1);
        await Assert.That(outcomes.All(outcome => outcome is LocalCredentialActivationOutcome.Activated
            or LocalCredentialActivationOutcome.AlreadyActivated or LocalCredentialActivationOutcome.Conflict)).IsTrue();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser activated = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation activatedOperation = await fixture.ReadOperationAsync();
        string? activatedToken = await fixture.ReadTokenValueAsync();
        await Assert.That(string.Equals(activated.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(activated.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(activated.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();

        LocalCredentialActivationOutcome replay = await fixture.ActivateAsync(request);

        await Assert.That(replay).IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
        LocalIdentityUser afterReplay = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(afterReplay.SecurityStamp, activated.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterReplay.ConcurrencyStamp, activated.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterReplay.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp == activatedOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), activatedToken, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task LostIdentityCommitAcknowledgementReplaysWithoutSecondStampRotation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        await fixture.SeedExactGraphAsync();
        LocalIdentityUser before = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation pending = await fixture.ReadOperationAsync();
        var request = new LocalCredentialActivationRequest(operationId: pending.Id,
            expectedOperationConcurrencyStamp: pending.ConcurrencyStamp);
        fixture.ActivationCommitFault.LoseNextAcknowledgement = true;

        await Assert.ThrowsAsync<InjectedBindingFailure>(() => fixture.ActivateAsync(request));

        await Assert.That(fixture.ActivationCommitFault.LostAcknowledgementObserved).IsTrue();
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        LocalIdentityUser committed = await fixture.ReadIdentityUserAsync();
        LocalIdentityCredentialOperation committedOperation = await fixture.ReadOperationAsync();
        string? committedToken = await fixture.ReadTokenValueAsync();
        await Assert.That(committedOperation.Stage).IsEqualTo(LocalCredentialOperationStage.ChangeRequired);
        await Assert.That(string.Equals(committed.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(committed.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(committed.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();

        LocalCredentialActivationOutcome retry = await fixture.ActivateAsync(request);

        await Assert.That(retry).IsEqualTo(LocalCredentialActivationOutcome.AlreadyActivated);
        LocalIdentityUser afterRetry = await fixture.ReadIdentityUserAsync();
        await Assert.That(string.Equals(afterRetry.SecurityStamp, committed.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterRetry.ConcurrencyStamp, committed.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(afterRetry.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
        await Assert.That((await fixture.ReadOperationAsync()).ConcurrencyStamp == committedOperation.ConcurrencyStamp).IsTrue();
        await Assert.That(string.Equals(await fixture.ReadTokenValueAsync(), committedToken, StringComparison.Ordinal)).IsTrue();
        await fixture.AssertExactGraphAsync();
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"binding-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"binding-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private ServiceProvider? _provider;
        private IdentityDatabaseTopology _topology;
        internal ServiceProvider Provider => _provider!;
        internal Guid InitiatorId { get; } = Guid.CreateVersion7();
        internal CancellationToken CancellationToken => _timeout.Token;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal BindingFaultInterceptor Faults { get; } = new();
        internal ActivationCommitFaultInterceptor ActivationCommitFault { get; } = new();
        internal ActivationWriteFaultInterceptor ActivationWriteFault { get; } = new();

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology)
        {
            var fixture = new Fixture { _topology = topology };
            try
            {
                await fixture.InitializeAsync();
                return fixture;
            }
            catch
            {
                await fixture.DisposeAsync();
                throw;
            }
        }

        internal ExploreDbContext Application(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>() : Application(scope);
        internal LocalIdentityCredentialStateStore StateStore(AsyncServiceScope scope) => new(
            identityDbContext: Identity(scope), applicationDbContext: Application(scope),
            userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), timeProvider: TimeProvider.System);

        internal AdminContext Admin(AsyncServiceScope scope, ClaimsPrincipal? principal = null)
        {
            ExploreDbContext application = Application(scope);
            return new AdminContext(
                httpContextAccessor: new HttpContextAccessor
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = principal ?? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", InitiatorId.ToString("D"))], "Bearer"))
                    }
                },
                platformUserRoleRepository: new PlatformUserRoleRepository(application),
                tenantAdminRepo: new TenantUserRoleGrantRepository(application),
                orgMemberRepo: new OrganizationMemberRepository(application),
                groupMemberRepo: new GroupMemberRepository(application),
                userExternalLoginRepository: new UserExternalLoginRepository(application),
                cache: _cache,
                logger: NullLogger<AdminContext>.Instance);
        }

        internal ReconcileLocalCredentialOperationCommandHandler Handler(AsyncServiceScope scope, AdminContext? admin = null) => new(
            adminContext: admin ?? Admin(scope),
            platformUserRoles: new PlatformUserRoleRepository(Application(scope)),
            credentialAdministration: StateStore(scope),
            unitOfWork: new EfCoreUnitOfWork(Application(scope)),
            userRepository: new UserRepository(Application(scope)),
            actorRepository: new ActorRepository(Application(scope)),
            externalLoginRepository: new UserExternalLoginRepository(Application(scope)));

        internal async Task<BaseCommandResponse<Guid>> ReconcileAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Handler(scope).Handle(new ReconcileLocalCredentialOperationCommand(operationId: Receipt.OperationId), CancellationToken);
        }

        internal async Task<LocalIdentityUser> ReadIdentityUserAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(user => user.Id == Receipt.LocalSubjectId, CancellationToken);
        }

        internal async Task<LocalIdentityCredentialOperation> ReadOperationAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(operation => operation.Id == Receipt.OperationId, CancellationToken);
        }

        internal async Task<string?> ReadTokenValueAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await Identity(scope).Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(token => token.UserId == Receipt.LocalSubjectId
                    && token.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && token.Name == LocalCredentialStateMetadata.TokenName)
                .Select(token => token.Value).SingleAsync(CancellationToken);
        }

        internal async Task<LocalCredentialActivationOutcome> ActivateAsync(LocalCredentialActivationRequest request)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await StateStore(scope).ActivateChangeRequiredAsync(request, CancellationToken);
        }

        internal async Task<LocalCredentialActivationOutcome[]> ActivateConcurrentlyAsync(LocalCredentialActivationRequest request)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LocalCredentialActivationOutcome> Run(TaskCompletionSource ready) => Task.Run(async () =>
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                LocalIdentityCredentialStateStore store = StateStore(scope);
                ready.SetResult();
                await start.Task.WaitAsync(CancellationToken);
                return await store.ActivateChangeRequiredAsync(request, CancellationToken);
            }, CancellationToken);
            Task<LocalCredentialActivationOutcome> first = Run(firstReady);
            Task<LocalCredentialActivationOutcome> second = Run(secondReady);
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(CancellationToken);
            start.SetResult();
            return await Task.WhenAll(first, second).WaitAsync(CancellationToken);
        }

        internal async Task AssertStateAsync(LocalCredentialState expected)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            LocalIdentityUser user = await ReadIdentityUserAsync();
            LocalCredentialStateMetadata? metadata = await StateStore(scope).ReadAsync(
                localSubjectId: Receipt.LocalSubjectId, expectedSecurityStamp: user.SecurityStamp!,
                cancellationToken: CancellationToken);
            await Assert.That(metadata).IsNotNull();
            await Assert.That(metadata!.State).IsEqualTo(expected);
            await Assert.That(metadata.OperationId).IsEqualTo(Receipt.OperationId);
            await Assert.That(metadata.ApplicationUserId).IsEqualTo(Receipt.LocalSubjectId);
        }

        internal async Task AssertNoGraphAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            await Assert.That(await application.Users.AnyAsync(user => user.Id == Receipt.LocalSubjectId, CancellationToken)).IsFalse();
            await Assert.That(await application.Actors.AnyAsync(actor => actor.Id == Receipt.PersonalActorId, CancellationToken)).IsFalse();
            await Assert.That(await application.UserExternalLogins.AnyAsync(login => login.Id == Receipt.ExternalLoginId, CancellationToken)).IsFalse();
        }

        internal async Task AssertExactGraphAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            User user = await application.Users.SingleAsync(row => row.Id == Receipt.LocalSubjectId, CancellationToken);
            Actor actor = await application.Actors.SingleAsync(row => row.Id == Receipt.PersonalActorId, CancellationToken);
            UserExternalLogin login = await application.UserExternalLogins.SingleAsync(row => row.Id == Receipt.ExternalLoginId, CancellationToken);
            LocalIdentityUser identity = await Identity(scope).Set<LocalIdentityUser>().SingleAsync(row => row.Id == Receipt.LocalSubjectId, CancellationToken);
            await Assert.That(user.Email).IsEqualTo(identity.Email);
            await Assert.That(user.EmailVerified).IsEqualTo(true);
            await Assert.That(user.IsDeleted).IsFalse();
            await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.User);
            await Assert.That(actor.UserId).IsEqualTo(user.Id);
            await Assert.That(actor.IsDeleted || actor.IsSuspended).IsFalse();
            await Assert.That(login.UserId).IsEqualTo(user.Id);
            await Assert.That(login.AuthenticationProviderId).IsEqualTo((int)AuthenticationProviderKind.Local);
            await Assert.That(login.ProviderKey).IsEqualTo(Receipt.LocalSubjectId.ToString("D"));
            await Assert.That(await application.Actors.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
            await Assert.That(await application.UserExternalLogins.CountAsync(row => row.UserId == user.Id, CancellationToken)).IsEqualTo(1);
        }

        internal async Task SeedExactGraphAsync()
        {
            LocalIdentityUser credential = await ReadIdentityUserAsync();
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            var user = new User
            {
                Id = Receipt.LocalSubjectId,
                Pii = new UserPii { Email = credential.Email!, FirstName = credential.FirstName, LastName = credential.LastName },
                EmailVerified = true, CreatedAt = DateTime.UtcNow
            };
            application.Actors.Add(new Actor
            {
                Id = Receipt.PersonalActorId, UserId = user.Id, User = user,
                ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                Pii = new ActorPii { DisplayName = "Local credential owner" }, CreatedAt = DateTime.UtcNow
            });
            application.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Receipt.ExternalLoginId, UserId = user.Id, User = user,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                ProviderKey = Receipt.LocalSubjectId.ToString("D"), CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(CancellationToken);
        }

        internal async Task RemovePlatformRoleAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            application.PlatformUserRoles.RemoveRange(await application.PlatformUserRoles
                .Where(role => role.UserId == InitiatorId).ToListAsync(CancellationToken));
            await application.SaveChangesAsync(CancellationToken);
        }

        internal async Task<Guid> GrantTenantAdministratorAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            ExploreDbContext application = Application(scope);
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(), FullName = "Binding authority tenant", Slug = $"binding-{Guid.CreateVersion7():N}",
                TenantStatusId = (int)TenantStatusEnum.Active, TenantStatus = null!, CreatedAt = DateTime.UtcNow
            };
            var membership = new TenantUser
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                UserId = InitiatorId, User = await application.Users.SingleAsync(user => user.Id == InitiatorId, CancellationToken),
                StatusId = (int)TenantUserStatusEnum.Active, CreatedAt = DateTime.UtcNow
            };
            application.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(), TenantId = tenant.Id, Tenant = tenant,
                TenantUserId = membership.Id, TenantUser = membership,
                RoleId = (int)RoleEnum.TenantAdmin, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
            });
            await application.SaveChangesAsync(CancellationToken);
            return tenant.Id;
        }

        private async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath));
            IdentityBuilder identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>();
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            _provider = services.BuildIsolatedServiceProvider();
            await using (AsyncServiceScope seed = Provider.CreateAsyncScope())
            {
                ExploreDbContext application = Application(seed);
                await application.Database.EnsureCreatedAsync(CancellationToken);
                if (_topology == IdentityDatabaseTopology.External)
                    await Identity(seed).Database.EnsureCreatedAsync(CancellationToken);
                await LookupTableSeeder.SeedAsync(application, CancellationToken);
                var initiator = new User
                {
                    Id = InitiatorId,
                    Pii = new UserPii { Email = $"admin-{InitiatorId:N}@example.test", FirstName = "Instance", LastName = "Administrator" },
                    EmailVerified = true, CreatedAt = DateTime.UtcNow
                };
                Role role = await application.Set<Role>().SingleAsync(row => row.MasterCode == "platform.admin", CancellationToken);
                application.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(), UserId = initiator.Id, User = initiator,
                    RoleId = role.Id, Role = role, GrantedAt = DateTime.UtcNow
                });
                await application.SaveChangesAsync(CancellationToken);
            }
            await using AsyncServiceScope create = Provider.CreateAsyncScope();
            LocalCredentialCreateResult result = await StateStore(create).CreatePendingAsync(new LocalCredentialCreateRequest(
                operationId: Guid.CreateVersion7(), initiatingApplicationUserId: InitiatorId,
                email: $"binding-{Guid.CreateVersion7():N}@example.test", firstName: "Credential", lastName: "Owner"), CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
            Receipt = result.Receipt!;
        }

        private void Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseSnakeCaseNamingConvention().AddInterceptors(
                SqliteNamedLockTransactionInterceptor.Instance, Faults, ActivationCommitFault, ActivationWriteFault);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_provider is not null) await _provider.DisposeAsync();
            }
            finally
            {
                _cache.Dispose();
                foreach (string path in new[] { _applicationPath, _identityPath })
                {
                    File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
                }
                _timeout.Dispose();
            }
        }
    }

    private sealed class InjectedBindingFailure : Exception { }

    private sealed class BindingFaultInterceptor : SaveChangesInterceptor
    {
        internal bool RejectGraph { get; set; }
        internal bool GraphFaultObserved { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (RejectGraph && eventData.Context!.ChangeTracker.Entries<Actor>().Any(entry => entry.State == EntityState.Added))
            {
                GraphFaultObserved = true;
                throw new InjectedBindingFailure();
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ActivationCommitFaultInterceptor : DbTransactionInterceptor
    {
        internal bool Enabled { get; set; }
        internal bool Observed { get; private set; }
        internal int CommitsObserved { get; private set; }
        internal bool LoseNextAcknowledgement { get; set; }
        internal bool LostAcknowledgementObserved { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && ++CommitsObserved == 2)
            {
                Enabled = false;
                Observed = true;
                throw new InjectedBindingFailure();
            }
            return base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
        }

        public override Task TransactionCommittedAsync(
            DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (LoseNextAcknowledgement)
            {
                LoseNextAcknowledgement = false;
                LostAcknowledgementObserved = true;
                throw new InjectedBindingFailure();
            }
            return Task.CompletedTask;
        }
    }

    private sealed class ActivationWriteFaultInterceptor : DbCommandInterceptor
    {
        private DbContext? _identity;
        private string? _table;
        internal bool Observed { get; private set; }
        internal int AffectedRows { get; private set; }

        internal void Arm(DbContext identity, string table)
        {
            _identity = identity;
            _table = table;
        }

        public override ValueTask<int> NonQueryExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, int result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _table is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(_table, StringComparison.Ordinal))
            {
                _table = null;
                Observed = true;
                AffectedRows = result;
                throw new InjectedBindingFailure();
            }
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
