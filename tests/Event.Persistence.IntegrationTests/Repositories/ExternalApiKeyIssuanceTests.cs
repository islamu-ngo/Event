using System.Data.Common;
using System.Data;
using System.Security.Claims;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application;
using Explore.Application.Authentication;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.DTOs.ExternalApiKey;
using Explore.Application.Exceptions;
using Explore.Application.Features.ExternalApiKeys.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Telemetry;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Constants;
using Explore.Infrastructure;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Secrets.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Data.SqlClient;
using MySqlConnector;
using Npgsql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Event.Persistence.IntegrationTests.Repositories;

[Category("Sqlite")]
[NotInParallel("ExternalApiKeyIssuanceSqlite")]
public sealed class ExternalApiKeyIssuanceTests
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(30);
    private static readonly AsyncLocal<Guid?> Invocation = new();

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    [Arguments(ExternalApiKeyOwnerType.Organization)]
    [Arguments(ExternalApiKeyOwnerType.Group)]
    public Task SameOperationRecoversOnlyCommittedMetadata(ExternalApiKeyOwnerType owner) =>
        ReplayAsync(false, owner);

    internal static async Task ReplayAsync(bool runtime, ExternalApiKeyOwnerType owner)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(runtime);
        var command = Command(owner);
        if (owner is ExternalApiKeyOwnerType.Organization or ExternalApiKeyOwnerType.Group)
        {
            Guid ownerId = await fixture.SeedOwnerAsync(owner);
            command = command with
            {
                ExternalApiKeyDto = command.ExternalApiKeyDto with
                {
                    OrganizationId = owner == ExternalApiKeyOwnerType.Organization ? ownerId : null,
                    GroupId = owner == ExternalApiKeyOwnerType.Group ? ownerId : null
                }
            };
        }
        var issued = await fixture.ExecuteAsync(command);
        await AssertIssuedAsync(issued);
        var recovered = await fixture.ExecuteAsync(command);
        await AssertRecoveredAsync(recovered, issued.Id);
        await Assert.That(recovered.KeyId).IsEqualTo(issued.KeyId);
        await fixture.AssertPairCountAsync(1);
        await using var observer = fixture.Open();
        var receipt = await observer.Context.Set<ExternalApiKeyIssuanceReceipt>()
            .IgnoreQueryFilters().AsNoTracking().SingleAsync(value => value.ExternalApiKeyId == issued.Id);
        await Assert.That(receipt.TenantId).IsEqualTo(
            owner == ExternalApiKeyOwnerType.InstanceAdmin ? (Guid?)null : fixture.TenantId);
        await Assert.That(receipt.OperationFingerprint.Length).IsEqualTo(64);
        await Assert.That(receipt.InputDigest.Length).IsEqualTo(64);
        // Never render a raw credential in failing assertion output.
        await Assert.That(JsonSerializer.Serialize(receipt).Contains(issued.ApiKey!, StringComparison.Ordinal)).IsFalse();
        var key = await observer.Context.ExternalApiKeys.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == issued.Id);
        await Assert.That(key.SecretHash.Contains(issued.ApiKey!, StringComparison.Ordinal)).IsFalse();
    }

    [Test]
    [Arguments("scopes")]
    [Arguments("description")]
    [Arguments("expiry")]
    [Arguments("credits")]
    public async Task SameOperationWithChangedAcceptedPolicyConflicts(string field)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        var command = Command(ExternalApiKeyOwnerType.InstanceAdmin);
        var issued = await fixture.ExecuteAsync(command);
        await AssertIssuedAsync(issued);
        var dto = command.ExternalApiKeyDto;
        var changed = command with
        {
            ExternalApiKeyDto = field switch
            {
                "scopes" => dto with { Scopes = ["events:write"] },
                "description" => dto with { Description = "Different accepted policy" },
                "expiry" => dto with { ExpiresAt = DateTime.UtcNow.AddDays(2) },
                "credits" => dto with { CreditLimit = 25 },
                _ => throw new ArgumentOutOfRangeException(nameof(field))
            }
        };
        var result = await CaptureAsync(() => fixture.ExecuteAsync(changed));
        await Assert.That(result.Error is ConcurrencyConflictException
            || result.Response is { IsSuccess: false, FailureCode: FailureCodes.ConcurrencyConflict }).IsTrue();
        await Assert.That(result.Response?.ApiKey is null).IsTrue();
        await fixture.AssertPairCountAsync(1);
        await AssertRecoveredAsync(await fixture.ExecuteAsync(command), issued.Id);
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public Task IdenticalOperationsOverlapAtTheProviderWriteBoundary(ExternalApiKeyOwnerType owner) =>
        IdenticalRaceAsync(false, owner);

    internal static async Task IdenticalRaceAsync(bool runtime, ExternalApiKeyOwnerType owner)
    {
        var boundary = new ProviderBoundary();
        await using var fixture = await IssuanceFixture.CreateAsync(runtime, boundary, boundary.Transactions);
        await using var winnerScope = fixture.Open();
        await using var contenderScope = fixture.Open();
        await Assert.That(winnerScope.Context.ContextId.InstanceId)
            .IsNotEqualTo(contenderScope.Context.ContextId.InstanceId);
        var command = Command(owner);
        await boundary.SelectGrantAsync(winnerScope.Context, owner, fixture.UserId, fixture.TenantId);
        boundary.Arm(winnerScope.Id, contenderScope.Id, BoundaryPoint.KeyInsert);
        Task arrived = boundary.WinnerArrived.Task.WaitAsync(Deadline);
        Task contenderArrived = boundary.ContenderArrived.Task.WaitAsync(Deadline);
        Task<CreateExternalApiKeyCommandResponse> winner = winnerScope.ExecuteAsync(command);
        Task<CreateExternalApiKeyCommandResponse>? contender = null;
        try
        {
            await ReachBoundaryAsync(arrived, winner);
            contender = Task.Run(() => contenderScope.ExecuteAsync(command));
            await ReachBoundaryAsync(contenderArrived, contender);
            await Assert.That(boundary.ContenderExcluded).IsTrue();
            await Assert.That(winner.IsCompleted).IsFalse();
            boundary.ReleaseWinner.TrySetResult();
            var issued = await winner.WaitAsync(Deadline);
            var recovered = await contender.WaitAsync(Deadline);
            await AssertIssuedAsync(issued);
            await AssertRecoveredAsync(recovered, issued.Id);
            await fixture.AssertPairCountAsync(1);
        }
        finally
        {
            boundary.ReleaseWinner.TrySetResult();
            await Task.WhenAll(winner, contender ?? Task.CompletedTask).WaitAsync(Deadline);
        }
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    public Task RevocationCommittedAfterInitialAuthorityReadDeniesFinalDecision(ExternalApiKeyOwnerType owner) =>
        RevocationBeforeDecisionAsync(false, owner);

    internal static async Task RevocationBeforeDecisionAsync(bool runtime, ExternalApiKeyOwnerType owner)
    {
        var boundary = new ProviderBoundary();
        await using var fixture = await IssuanceFixture.CreateAsync(runtime, boundary, boundary.Transactions);
        await using var request = fixture.Open();
        // The interceptor observes the handler's real grant SELECT, then stops its next
        // provider action, after that reader has been consumed but before the write fence.
        boundary.Arm(request.Id, null, BoundaryPoint.AfterInitialAuthorityRead);
        Task arrived = boundary.WinnerArrived.Task.WaitAsync(Deadline);
        var command = Command(owner);
        var pending = CaptureAsync(() => request.ExecuteAsync(command));
        try
        {
            await ReachBoundaryAsync(arrived, pending);
            await fixture.RevokeAsync(owner);
        }
        finally
        {
            boundary.ReleaseWinner.TrySetResult();
        }
        await AssertDeniedAsync(await pending.WaitAsync(Deadline));
        await fixture.AssertPairCountAsync(0);
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    public Task IssuanceOrderedBeforeRevocationCommitsButCannotRecoverAfterward(ExternalApiKeyOwnerType owner) =>
        IssuanceBeforeRevocationAsync(false, owner);

    [Test]
    [Arguments(ExternalApiKeyOwnerType.Organization, false)]
    [Arguments(ExternalApiKeyOwnerType.Organization, true)]
    [Arguments(ExternalApiKeyOwnerType.Group, false)]
    [Arguments(ExternalApiKeyOwnerType.Group, true)]
    public async Task PlacementSuspendedAfterInitialAuthorityDeniesCreationAndRecovery(
        ExternalApiKeyOwnerType owner, bool replay)
    {
        var boundary = new ProviderBoundary();
        await using var fixture = await IssuanceFixture.CreateAsync(false, boundary, boundary.Transactions);
        Guid ownerId = await fixture.SeedOwnerAsync(owner);
        var command = Command(owner);
        command = command with
        {
            ExternalApiKeyDto = command.ExternalApiKeyDto with
            {
                OrganizationId = owner == ExternalApiKeyOwnerType.Organization ? ownerId : null,
                GroupId = owner == ExternalApiKeyOwnerType.Group ? ownerId : null
            }
        };
        if (replay)
            await AssertIssuedAsync(await fixture.ExecuteAsync(command));
        await using var request = fixture.Open();
        boundary.Arm(request.Id, null, BoundaryPoint.BeforeIssuanceTransaction);
        Task arrived = boundary.WinnerArrived.Task.WaitAsync(Deadline);
        var pending = CaptureAsync(() => request.ExecuteAsync(command));
        try
        {
            await ReachBoundaryAsync(arrived, pending);
            await using var suspender = fixture.Open();
            if (owner == ExternalApiKeyOwnerType.Organization)
            {
                var placement = await suspender.Context.OrganizationTenants
                    .SingleAsync(value => value.OrganizationId == ownerId && value.TenantId == fixture.TenantId);
                placement.IsSuspended = true;
            }
            else
            {
                var placement = await suspender.Context.GroupTenants
                    .SingleAsync(value => value.GroupId == ownerId && value.TenantId == fixture.TenantId);
                placement.IsSuspended = true;
            }
            await suspender.Context.SaveChangesAsync();
        }
        finally
        {
            boundary.ReleaseWinner.TrySetResult();
        }
        await AssertDeniedAsync(await pending.WaitAsync(Deadline));
        await fixture.AssertPairCountAsync(replay ? 1 : 0);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task ProviderAccountChangedAfterResolutionDeniesCreationAndRecovery(bool reassign, bool replay)
    {
        var boundary = new ProviderBoundary();
        await using var fixture = await IssuanceFixture.CreateAsync(false, boundary, boundary.Transactions);
        Guid otherUser = await fixture.SeedOtherUserAsync();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", fixture.UserId.ToString("D")),
             new Claim("auth_provider", "local"),
             new Claim("iss", LocalIdentityOptions.Issuer)], "fixture"));
        var accountKey = principal.GetProviderIdentity()!.AccountKey;
        Guid bindingId = Guid.CreateVersion7();
        await using (var seed = fixture.Open())
        {
            seed.Context.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = bindingId,
                UserId = fixture.UserId,
                User = null!,
                AuthenticationProviderId = (int)accountKey.ProviderKind,
                AuthenticationProvider = null!,
                ProviderKey = accountKey.Value,
                CreatedAt = DateTime.UtcNow
            });
            // Another binding for this user must not substitute for the authenticated account.
            seed.Context.UserExternalLogins.Add(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.UserId,
                User = null!,
                AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
                AuthenticationProvider = null!,
                ProviderKey = $"unrelated-{Guid.CreateVersion7():N}",
                CreatedAt = DateTime.UtcNow
            });
            await seed.Context.SaveChangesAsync();
        }
        var command = Command(ExternalApiKeyOwnerType.InstanceAdmin);
        await using var request = fixture.Open();
        request.Services.GetRequiredService<RequestIdentity>().HttpContext!.User = principal;
        if (replay)
            await AssertIssuedAsync(await request.ExecuteAsync(command));
        var stale = await request.Context.UserExternalLogins.SingleAsync(value => value.Id == bindingId);
        boundary.Arm(request.Id, null, BoundaryPoint.BeforeIssuanceTransaction);
        Task arrived = boundary.WinnerArrived.Task.WaitAsync(Deadline);
        var pending = CaptureAsync(() => request.ExecuteAsync(command));
        try
        {
            await ReachBoundaryAsync(arrived, pending);
            await using var changer = fixture.Open();
            var binding = await changer.Context.UserExternalLogins.SingleAsync(value => value.Id == bindingId);
            if (reassign)
            {
                // UserId belongs to an alternate key, so reassignment is a real delete/insert.
                changer.Context.UserExternalLogins.Remove(binding);
                await changer.Context.SaveChangesAsync();
                changer.Context.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = bindingId,
                    UserId = otherUser,
                    User = null!,
                    AuthenticationProviderId = binding.AuthenticationProviderId,
                    AuthenticationProvider = null!,
                    ProviderKey = binding.ProviderKey,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                changer.Context.UserExternalLogins.Remove(binding);
            }
            await changer.Context.SaveChangesAsync();
        }
        finally
        {
            boundary.ReleaseWinner.TrySetResult();
        }
        await Assert.That(request.Context.Entry(stale).State).IsEqualTo(EntityState.Unchanged);
        await AssertDeniedAsync(await pending.WaitAsync(Deadline));
        await fixture.AssertPairCountAsync(replay ? 1 : 0);
    }

    internal static async Task IssuanceBeforeRevocationAsync(bool runtime, ExternalApiKeyOwnerType owner)
    {
        var boundary = new ProviderBoundary();
        await using var fixture = await IssuanceFixture.CreateAsync(runtime, boundary, boundary.Transactions);
        await using var request = fixture.Open();
        await using var revoker = fixture.Open();
        await Assert.That(request.Context.ContextId.InstanceId)
            .IsNotEqualTo(revoker.Context.ContextId.InstanceId);
        await boundary.SelectGrantAsync(request.Context, owner, fixture.UserId, fixture.TenantId);
        boundary.Arm(request.Id, revoker.Id, BoundaryPoint.KeyInsert);
        Task arrived = boundary.WinnerArrived.Task.WaitAsync(Deadline);
        Task revokerArrived = boundary.ContenderArrived.Task.WaitAsync(Deadline);
        var command = Command(owner);
        var pending = request.ExecuteAsync(command);
        Task? revocation = null;
        try
        {
            await ReachBoundaryAsync(arrived, pending);
            revocation = Task.Run(() => fixture.RevokeAsync(owner, revoker));
            await ReachBoundaryAsync(revokerArrived, revocation);
            await Assert.That(boundary.ContenderExcluded).IsTrue();
            await Assert.That(pending.IsCompleted).IsFalse();
            boundary.ReleaseWinner.TrySetResult();
            var issued = await pending.WaitAsync(Deadline);
            await revocation.WaitAsync(Deadline);
            await AssertIssuedAsync(issued);
            await fixture.AssertPairCountAsync(1);
            await AssertDeniedAsync(await CaptureAsync(() => fixture.ExecuteAsync(command)));
        }
        finally
        {
            boundary.ReleaseWinner.TrySetResult();
            await Task.WhenAll(pending, revocation ?? Task.CompletedTask).WaitAsync(Deadline);
        }
    }

    [Test]
    public Task FailureBeforeCommitLeavesNeitherKeyNorReceipt() => FailureBeforeCommitAsync(false);

    internal static async Task FailureBeforeCommitAsync(bool runtime)
    {
        var fault = new CommitFault(afterCommit: false);
        await using var fixture = await IssuanceFixture.CreateAsync(runtime, fault);
        await using var request = fixture.Open();
        fault.Arm(request.Id);
        var command = Command(ExternalApiKeyOwnerType.InstanceAdmin);
        var result = await CaptureAsync(() => request.ExecuteAsync(command));
        await Assert.That(fault.Observed).IsTrue();
        await Assert.That(result.Error is InjectedCommitFailure).IsTrue();
        await Assert.That(result.Response?.ApiKey is null).IsTrue();
        await fixture.AssertPairCountAsync(0);
        await AssertIssuedAsync(await fixture.ExecuteAsync(command));
        await fixture.AssertPairCountAsync(1);
    }

    [Test]
    public Task ActualCommitWithLostAcknowledgementCanOnlyRecoverMetadata() => LostAcknowledgementAsync(false);

    internal static async Task LostAcknowledgementAsync(bool runtime)
    {
        var fault = new CommitFault(afterCommit: true);
        await using var fixture = await IssuanceFixture.CreateAsync(runtime, fault);
        await using var request = fixture.Open();
        fault.Arm(request.Id);
        var command = Command(ExternalApiKeyOwnerType.InstanceAdmin);
        var uncertain = await CaptureAsync(() => request.ExecuteAsync(command));
        await Assert.That(fault.Observed).IsTrue();
        await Assert.That(uncertain.Error is null or InjectedCommitFailure).IsTrue();
        await Assert.That(uncertain.Response?.ApiKey is null).IsTrue();
        await fixture.AssertPairCountAsync(1);
        var recovered = await fixture.ExecuteAsync(command);
        await Assert.That(recovered.IsSuccess).IsTrue();
        await AssertRecoveredAsync(recovered, recovered.Id);
        if (uncertain.Response is not null)
            await Assert.That(uncertain.Response.Id).IsEqualTo(recovered.Id);
        await fixture.AssertPairCountAsync(1);
    }

    [Test]
    public async Task UnchangedTrackedGrantCannotRestoreCommittedRevocation()
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        await using var request = fixture.Open();
        var stale = await request.Context.PlatformUserRoles
            .SingleAsync(value => value.UserId == fixture.UserId);
        await fixture.RevokeAsync(ExternalApiKeyOwnerType.InstanceAdmin);
        await Assert.That(request.Context.Entry(stale).State).IsEqualTo(EntityState.Unchanged);
        await AssertDeniedAsync(await CaptureAsync(() =>
            request.ExecuteAsync(Command(ExternalApiKeyOwnerType.InstanceAdmin))));
        await fixture.AssertPairCountAsync(0);
    }

    [Test]
    [Arguments(EntityState.Added)]
    [Arguments(EntityState.Modified)]
    [Arguments(EntityState.Deleted)]
    public async Task PendingForeignTrackedChangesCannotBeFlushedByIssuance(EntityState state)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        Guid foreignTenant = await fixture.SeedOtherTenantAsync();
        Guid foreignUser = await fixture.SeedOtherUserAsync();
        await fixture.SeedMembershipAsync(foreignTenant, foreignUser);
        await using var request = fixture.Open();
        var tracked = await request.Context.TenantUsers.IgnoreQueryFilters()
            .SingleAsync(value => value.TenantId == foreignTenant && value.UserId == foreignUser);
        DateTime? original = tracked.JoinedAt;
        Guid pendingId = tracked.Id;
        if (state == EntityState.Added)
        {
            var added = new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = foreignTenant,
                Tenant = null!,
                UserId = fixture.UserId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            pendingId = added.Id;
            request.Context.TenantUsers.Add(added);
        }
        else if (state == EntityState.Modified)
        {
            tracked.JoinedAt = tracked.JoinedAt!.Value.AddDays(1);
        }
        else
        {
            request.Context.TenantUsers.Remove(tracked);
        }
        var result = await CaptureAsync(() =>
            request.ExecuteAsync(Command(ExternalApiKeyOwnerType.InstanceAdmin)));
        await Assert.That(result.Error is InvalidOperationException).IsTrue();
        await Assert.That(result.Response?.ApiKey is null).IsTrue();
        await fixture.AssertPairCountAsync(0);
        await using var observer = fixture.Open();
        var persisted = await observer.Context.TenantUsers.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(value => value.Id == tracked.Id);
        await Assert.That(persisted.JoinedAt).IsEqualTo(original);
        await Assert.That(persisted.IsDeleted).IsFalse();
        if (state == EntityState.Added)
            await Assert.That(await observer.Context.TenantUsers.IgnoreQueryFilters()
                .AnyAsync(value => value.Id == pendingId)).IsFalse();
    }

    [Test]
    public async Task ExistingAmbientTransactionCannotDiscloseOrCommit()
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        await using var request = fixture.Open();
        using (var ambient = new System.Transactions.TransactionScope(
            System.Transactions.TransactionScopeAsyncFlowOption.Enabled))
        {
            var result = await CaptureAsync(() =>
                request.ExecuteAsync(Command(ExternalApiKeyOwnerType.InstanceAdmin)));
            await Assert.That(result.Error is InvalidOperationException).IsTrue();
            await Assert.That(result.Response?.ApiKey is null).IsTrue();
        }
        await fixture.AssertPairCountAsync(0);
    }

    [Test]
    public async Task UnchangedForeignTrackedKeyCannotSubstituteForExactRecovery()
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        Guid foreignUser = await fixture.SeedOtherUserAsync();
        var command = Command(ExternalApiKeyOwnerType.User);
        await using var foreign = fixture.Open(foreignUser);
        var foreignKey = await foreign.ExecuteAsync(command);
        await AssertIssuedAsync(foreignKey);
        await using var current = fixture.Open();
        var tracked = await current.Context.ExternalApiKeys.IgnoreQueryFilters()
            .SingleAsync(value => value.Id == foreignKey.Id);
        await Assert.That(current.Context.Entry(tracked).State).IsEqualTo(EntityState.Unchanged);
        var own = await current.ExecuteAsync(command);
        await AssertIssuedAsync(own);
        await Assert.That(own.Id).IsNotEqualTo(foreignKey.Id);
        await AssertRecoveredAsync(await fixture.ExecuteAsync(command), own.Id);
        await fixture.AssertPairCountAsync(2);
    }

    [Test]
    public Task OperationIdentitySeparatesPrincipalTenantAndOwnerIncludingNullTenant() =>
        IsolationAsync(false);

    internal static async Task IsolationAsync(bool runtime)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(runtime);
        Guid otherUser = await fixture.SeedOtherUserAsync();
        Guid otherTenant = await fixture.SeedOtherTenantAsync();
        await fixture.SeedMembershipAsync(otherTenant, fixture.UserId);
        var command = Command(ExternalApiKeyOwnerType.User);
        var personal = await fixture.ExecuteAsync(command);
        await using var otherPrincipal = fixture.Open(otherUser);
        var otherPersonal = await otherPrincipal.ExecuteAsync(command);
        await using var otherScope = fixture.Open(tenantId: otherTenant);
        var otherTenantKey = await otherScope.ExecuteAsync(command);
        var tenant = await fixture.ExecuteAsync(command with
        {
            ExternalApiKeyDto = command.ExternalApiKeyDto with { ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.Tenant }
        });
        var global = await fixture.ExecuteAsync(command with
        {
            ExternalApiKeyDto = command.ExternalApiKeyDto with { ExternalApiKeyOwnerTypeId = (int)ExternalApiKeyOwnerType.InstanceAdmin }
        });
        foreach (var response in new[] { personal, otherPersonal, otherTenantKey, tenant, global })
            await AssertIssuedAsync(response);
        await Assert.That(new[] { personal.Id, otherPersonal.Id, otherTenantKey.Id, tenant.Id, global.Id }
            .Distinct().Count()).IsEqualTo(5);
        await fixture.AssertPairCountAsync(5);
        await using var observer = fixture.Open();
        var ids = new[] { personal.Id, otherPersonal.Id, otherTenantKey.Id, tenant.Id, global.Id };
        var receipts = await observer.Context.Set<ExternalApiKeyIssuanceReceipt>()
            .IgnoreQueryFilters().AsNoTracking().Where(value => ids.Contains(value.ExternalApiKeyId)).ToListAsync();
        await Assert.That(receipts.Select(value => value.OperationFingerprint).Distinct().Count()).IsEqualTo(5);
        await Assert.That(receipts.Count(value => value.TenantId is null)).IsEqualTo(1);
        await AssertRecoveredAsync(await fixture.ExecuteAsync(command), personal.Id);
    }

    [Test]
    [Arguments(ExternalApiKeyOwnerType.Organization)]
    [Arguments(ExternalApiKeyOwnerType.Group)]
    public Task SameOperationSeparatesTwoOwnersAndRejectsForeignTenantRecovery(ExternalApiKeyOwnerType owner) =>
        OwnerIsolationAsync(false, owner);

    internal static async Task OwnerIsolationAsync(bool runtime, ExternalApiKeyOwnerType owner)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(runtime);
        Guid firstOwner = await fixture.SeedOwnerAsync(owner);
        Guid secondOwner = await fixture.SeedOwnerAsync(owner);
        var command = Command(owner);
        CreateExternalApiKeyCommand ForOwner(Guid id) => command with
        {
            ExternalApiKeyDto = command.ExternalApiKeyDto with
            {
                OrganizationId = owner == ExternalApiKeyOwnerType.Organization ? id : null,
                GroupId = owner == ExternalApiKeyOwnerType.Group ? id : null
            }
        };
        var first = await fixture.ExecuteAsync(ForOwner(firstOwner));
        var second = await fixture.ExecuteAsync(ForOwner(secondOwner));
        await AssertIssuedAsync(first);
        await AssertIssuedAsync(second);
        await Assert.That(first.Id).IsNotEqualTo(second.Id);
        await AssertRecoveredAsync(await fixture.ExecuteAsync(ForOwner(firstOwner)), first.Id);
        await AssertRecoveredAsync(await fixture.ExecuteAsync(ForOwner(secondOwner)), second.Id);

        Guid otherTenant = await fixture.SeedOtherTenantAsync();
        await fixture.SeedMembershipAsync(otherTenant, fixture.UserId);
        await using var foreign = fixture.Open(tenantId: otherTenant);
        var denied = await CaptureAsync(() => foreign.ExecuteAsync(ForOwner(firstOwner)));
        await Assert.That(denied.Error is AuthorizationException or NotFoundException
            || denied.Response is { IsSuccess: false }).IsTrue();
        await Assert.That(denied.Response?.ApiKey is null).IsTrue();
        await Assert.That(denied.Response?.KeyId is null).IsTrue();
        await fixture.AssertPairCountAsync(2);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RetainedErasureAuthorityDeniesLocalPrincipalBeforeIssuanceAndRecovery(bool replay)
    {
        await using var fixture = await IssuanceFixture.CreateAsync(false);
        var command = Command(ExternalApiKeyOwnerType.InstanceAdmin);
        if (replay)
            await AssertIssuedAsync(await fixture.ExecuteAsync(command));
        await using (var eraser = fixture.Open())
        {
            await eraser.Services.GetRequiredService<IPrivacyErasureAuthority>().AppendAsync(
                PrivacyErasureRequest.Create(Guid.CreateVersion7(), PrivacyErasureSubjectKind.User,
                    fixture.UserId, PrivacyErasureReasonCode.AccountDeletion, 1), CancellationToken.None);
        }
        await AssertDeniedAsync(await CaptureAsync(() => fixture.ExecuteAsync(command)));
        await fixture.AssertPairCountAsync(replay ? 1 : 0);
    }

    private static CreateExternalApiKeyCommand Command(ExternalApiKeyOwnerType owner) => new()
    {
        OperationKey = Guid.CreateVersion7().ToString("N"),
        ExternalApiKeyDto = new()
        {
            Name = $"Native issuance {Guid.CreateVersion7():N}",
            ExternalApiKeyOwnerTypeId = (int)owner,
            Scopes = ["events:read"]
        }
    };

    private static async Task AssertIssuedAsync(CreateExternalApiKeyCommandResponse response)
    {
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id != Guid.Empty).IsTrue();
        await Assert.That(!string.IsNullOrWhiteSpace(response.ApiKey)).IsTrue();
    }

    private static async Task AssertRecoveredAsync(CreateExternalApiKeyCommandResponse response, Guid id)
    {
        await Assert.That(response.IsSuccess).IsTrue();
        await Assert.That(response.Id).IsEqualTo(id);
        await Assert.That(response.ApiKey is null).IsTrue();
        var wire = JsonSerializer.SerializeToElement(response);
        await Assert.That(wire.TryGetProperty("DisclosureStatus", out var status)
            && status.ValueKind == JsonValueKind.String && status.GetString() == "PreviouslyIssued").IsTrue();
    }

    private sealed record Attempt(CreateExternalApiKeyCommandResponse? Response, Exception? Error);

    private static async Task ReachBoundaryAsync(Task arrival, Task operation)
    {
        if (await Task.WhenAny(arrival, operation) == operation && !arrival.IsCompleted)
        {
            await operation;
            throw new InvalidOperationException("The native operation completed without reaching its required provider boundary.");
        }
        await arrival;
    }

    private static async Task<Attempt> CaptureAsync(Func<Task<CreateExternalApiKeyCommandResponse>> action)
    {
        try
        {
            return new(await action().WaitAsync(Deadline), null);
        }
        catch (Exception exception) when (exception is AuthorizationException
            or NotFoundException or ConcurrencyConflictException or InvalidOperationException or InjectedCommitFailure)
        {
            return new(null, exception);
        }
    }

    private static async Task AssertDeniedAsync(Attempt result)
    {
        await Assert.That(result.Error is AuthorizationException or NotFoundException
            || result.Response is { IsSuccess: false, FailureCode: FailureCodes.NotFound }).IsTrue();
        await Assert.That(result.Response?.ApiKey is null).IsTrue();
        await Assert.That(result.Response?.KeyId is null).IsTrue();
        await Assert.That(result.Response is null || result.Response.Id == Guid.Empty).IsTrue();
    }

    private static Tenant NewTenant() => new()
    {
        Id = Guid.CreateVersion7(),
        FullName = "Native issuance tenant",
        Slug = $"issuance-{Guid.CreateVersion7():N}",
        TenantStatusId = (int)TenantStatusEnum.Active,
        TenantStatus = null!
    };

    private sealed class RequestIdentity(Guid tenantId, ClaimsPrincipal principal) : ITenantContext, IHttpContextAccessor
    {
        public Guid TenantId { get; set; } = tenantId;
        public HttpContext? HttpContext { get; set; } = new DefaultHttpContext { User = principal };
    }

    private sealed class RequestScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope;
        private readonly IssuanceFixture _fixture;
        internal Guid Id { get; } = Guid.CreateVersion7();
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal ExploreDbContext Context => Services.GetRequiredService<ExploreDbContext>();

        internal RequestScope(AsyncServiceScope scope, Guid userId, Guid tenantId, IssuanceFixture fixture)
        {
            _scope = scope;
            _fixture = fixture;
            var identity = Services.GetRequiredService<RequestIdentity>();
            identity.TenantId = tenantId;
            identity.HttpContext!.User = Principal(userId);
        }

        internal async Task<CreateExternalApiKeyCommandResponse> ExecuteAsync(CreateExternalApiKeyCommand command)
        {
            using var timeout = new CancellationTokenSource(Deadline);
            Guid? previous = Invocation.Value;
            Invocation.Value = Id;
            try
            {
                var identity = Services.GetRequiredService<RequestIdentity>();
                var principal = Services.GetRequiredService<IUserContext>().GetRequiredUserId();
                var owner = (ExternalApiKeyOwnerType)command.ExternalApiKeyDto.ExternalApiKeyOwnerTypeId;
                Guid? tenant = owner == ExternalApiKeyOwnerType.InstanceAdmin ? null : identity.TenantId;
                Guid ownerId = owner switch
                {
                    ExternalApiKeyOwnerType.Tenant => identity.TenantId,
                    ExternalApiKeyOwnerType.Organization => command.ExternalApiKeyDto.OrganizationId!.Value,
                    ExternalApiKeyOwnerType.Group => command.ExternalApiKeyDto.GroupId!.Value,
                    _ => principal
                };
                string fingerprint = ExternalApiKeyIssuanceReceipt.ComputeOperationFingerprint(
                    principal, tenant, owner, ownerId, command.OperationKey);
                lock (_fixture._fingerprints) _fixture._fingerprints.Add(fingerprint);
                return await Services.GetRequiredService<
                    ICommandHandler<CreateExternalApiKeyCommand, CreateExternalApiKeyCommandResponse>>()
                    .ExecuteAsync(command, timeout.Token);
            }
            finally
            {
                Invocation.Value = previous;
            }
        }

        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }

    private static ClaimsPrincipal Principal(Guid id) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.NameIdentifier, id.ToString())], "fixture"));

    private sealed class AuthorityFactory(DbContextOptions<EmbeddedPrivacyErasureAuthorityDbContext> options)
        : IDbContextFactory<EmbeddedPrivacyErasureAuthorityDbContext>
    {
        public EmbeddedPrivacyErasureAuthorityDbContext CreateDbContext() => new(options);
    }

    private sealed class IssuanceFixture : IAsyncDisposable
    {
        private readonly DirectoryInfo _authorityDirectory = Directory.CreateTempSubdirectory("api-key-authority-");
        private EventVisitorCapabilitySqliteFixture? _sqlite;
        private ServiceProvider? _runtime;
        private readonly HashSet<Guid> _users = [];
        internal readonly HashSet<string> _fingerprints = [];
        internal Guid UserId { get; private set; } = Guid.CreateVersion7();
        internal Guid TenantId { get; private set; } = Guid.CreateVersion7();

        internal static async Task<IssuanceFixture> CreateAsync(bool runtime, params IInterceptor[] interceptors)
        {
            var fixture = new IssuanceFixture();
            var authorityOptions = TestDbContextOptions.Create<EmbeddedPrivacyErasureAuthorityDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(fixture._authorityDirectory.FullName, "authority.db"),
                    Pooling = false
                }.ToString())
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(interceptors)
                .Options;
            var authorityFactory = new AuthorityFactory(authorityOptions);
            // Same real retained-authority setup as RetainedIdentityFenceTests. No generated
            // migration or internal authority substitute is involved.
            await using (var authority = authorityFactory.CreateDbContext())
                await authority.Database.EnsureCreatedAsync();

            void Configure(IServiceCollection services, Guid tenant, ClaimsPrincipal principal)
            {
                services.AddScoped(_ => new RequestIdentity(tenant, principal));
                services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<RequestIdentity>());
                services.AddScoped<IHttpContextAccessor>(provider => provider.GetRequiredService<RequestIdentity>());
                services.ConfigureDbContext<ExploreDbContext>(options => options.AddInterceptors(interceptors));
                services.AddScoped(provider => new EmbeddedPrivacyErasureAuthorityRepository(
                    authorityFactory, TimeProvider.System, Options.Create(new PrivacyErasureOptions())));
                services.AddScoped<IPrivacyIdentityFenceAuthority>(provider =>
                    provider.GetRequiredService<EmbeddedPrivacyErasureAuthorityRepository>());
                services.AddScoped<IPrivacyErasureAuthority>(provider =>
                    provider.GetRequiredService<EmbeddedPrivacyErasureAuthorityRepository>());
            }

            if (!runtime)
            {
                fixture._sqlite = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
                {
                    var tenant = (ITenantContext)services.Last(value => value.ServiceType == typeof(ITenantContext))
                        .ImplementationInstance!;
                    var http = (IHttpContextAccessor)services.Last(value => value.ServiceType == typeof(IHttpContextAccessor))
                        .ImplementationInstance!;
                    Configure(services, tenant.TenantId, http.HttpContext!.User);
                });
                fixture.UserId = fixture._sqlite.UserId;
                fixture.TenantId = fixture._sqlite.TenantId;
            }
            else
            {
                // Binding fails on absent structured inputs. This lane never skips, provisions
                // containers, applies migrations, or falls back to the SQLite fast fixture.
                var providerFixture = PrimaryDatabaseProviderBehaviorFixture.Create();
                await providerFixture.PrepareAsync();
                var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
                var services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                {
                    DisableDefaults = true,
                    EnvironmentName = Environments.Production
                }).Services;
                services.AddSingleton<IConfiguration>(configuration);
                services.AddLogging();
                services.AddHybridCache();
                services.AddMetrics();
                services.AddSingleton<BusinessMetrics>();
                services.AddSingleton<ProjectionMetrics>();
                services.ConfigureApplicationServices(configuration);
                services.ConfigureInfrastructureServices(configuration);
                services.AddSecretProvider(configuration);
                services.AddSecretResolution();
                services.ConfigurePersistenceServices(configuration, skipLookupCacheInitializer: true);
                Configure(services, fixture.TenantId, Principal(fixture.UserId));
                fixture._runtime = services.BuildIsolatedServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                await using var seed = fixture.Open();
                var tenant = NewTenant();
                tenant.Id = fixture.TenantId;
                seed.Context.Tenants.Add(tenant);
                seed.Context.Users.Add(NewUser(fixture.UserId));
                await seed.Context.SaveChangesAsync();
                await fixture.SeedMembershipAsync(fixture.TenantId, fixture.UserId);
            }

            fixture._users.Add(fixture.UserId);
            await using var grant = fixture.Open();
            grant.Context.PlatformUserRoles.Add(new PlatformUserRole
            {
                Id = Guid.CreateVersion7(),
                UserId = fixture.UserId,
                User = null!,
                RoleId = (int)RoleEnum.Admin,
                Role = null!,
                GrantedAt = DateTime.UtcNow
            });
            var membership = await grant.Context.TenantUsers.SingleAsync(value => value.UserId == fixture.UserId);
            grant.Context.TenantUserRoleGrants.Add(new TenantUserRoleGrant
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                Tenant = null!,
                TenantUserId = membership.Id,
                TenantUser = null!,
                RoleId = (int)RoleEnum.TenantAdmin,
                Role = null!,
                RoleScopeId = (int)RoleScopeEnum.Tenant,
                GrantedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await grant.Context.SaveChangesAsync();
            return fixture;
        }

        internal RequestScope Open(Guid? userId = null, Guid? tenantId = null) =>
            new(_sqlite?.CreateScope() ?? _runtime!.CreateAsyncScope(), userId ?? UserId, tenantId ?? TenantId, this);

        internal async Task<CreateExternalApiKeyCommandResponse> ExecuteAsync(CreateExternalApiKeyCommand command)
        {
            await using var request = Open();
            return await request.ExecuteAsync(command);
        }

        internal async Task AssertPairCountAsync(int expected)
        {
            await using var observer = Open();
            var users = _users.ToArray();
            var keys = observer.Context.ExternalApiKeys.IgnoreQueryFilters().AsNoTracking()
                .Where(value => value.CreatedBy != null && users.Contains(value.CreatedBy.Value));
            var ids = await keys.Select(value => value.Id).ToArrayAsync();
            string[] fingerprints;
            lock (_fingerprints) fingerprints = _fingerprints.ToArray();
            await Assert.That(ids.Length).IsEqualTo(expected);
            // Receipt cardinality is a transaction invariant, not a replacement for querying
            // the public command result. Count all fixture scopes, including null tenant.
            var receipts = await observer.Context.Set<ExternalApiKeyIssuanceReceipt>().IgnoreQueryFilters()
                .AsNoTracking().Where(value => ids.Contains(value.ExternalApiKeyId)
                    || fingerprints.Contains(value.OperationFingerprint)).ToListAsync();
            await Assert.That(receipts.Count).IsEqualTo(expected);
            await Assert.That(receipts.Select(value => value.ExternalApiKeyId).Distinct().Count()).IsEqualTo(expected);
        }

        internal async Task RevokeAsync(ExternalApiKeyOwnerType owner, RequestScope? supplied = null)
        {
            await using var owned = supplied is null ? Open() : null;
            var scope = supplied ?? owned!;
            Guid? previous = Invocation.Value;
            Invocation.Value = scope.Id;
            try
            {
                using var timeout = new CancellationTokenSource(Deadline);
                if (owner == ExternalApiKeyOwnerType.InstanceAdmin)
                    await scope.Context.PlatformUserRoles.Where(value => value.UserId == UserId)
                        .ExecuteDeleteAsync(timeout.Token);
                else
                    await scope.Context.TenantUserRoleGrants.Where(value => value.TenantId == TenantId)
                        .ExecuteUpdateAsync(update => update.SetProperty(value => value.RevokedAt, DateTime.UtcNow),
                            timeout.Token);
                var admin = scope.Services.GetRequiredService<IAdminContext>();
                await Assert.That(owner == ExternalApiKeyOwnerType.InstanceAdmin
                    ? await admin.IsInstanceAdminAsync()
                    : await admin.IsTenantAdminAsync(TenantId)).IsFalse();
            }
            finally
            {
                Invocation.Value = previous;
            }
        }

        internal async Task<Guid> SeedOtherUserAsync()
        {
            Guid id = Guid.CreateVersion7();
            await using var seed = Open();
            seed.Context.Users.Add(NewUser(id));
            await seed.Context.SaveChangesAsync();
            await SeedMembershipAsync(TenantId, id);
            _users.Add(id);
            return id;
        }

        internal async Task<Guid> SeedOtherTenantAsync()
        {
            await using var seed = Open();
            var tenant = NewTenant();
            seed.Context.Tenants.Add(tenant);
            await seed.Context.SaveChangesAsync();
            return tenant.Id;
        }

        internal async Task SeedMembershipAsync(Guid tenantId, Guid userId)
        {
            await using var seed = Open(userId, tenantId);
            seed.Context.TenantUsers.Add(new TenantUser
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                Tenant = null!,
                UserId = userId,
                User = null!,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            });
            await seed.Context.SaveChangesAsync();
        }

        internal async Task<Guid> SeedOwnerAsync(ExternalApiKeyOwnerType owner)
        {
            Guid id = Guid.CreateVersion7();
            Guid placement = Guid.CreateVersion7();
            await using var seed = Open();
            int roleId = owner == ExternalApiKeyOwnerType.Organization ? (int)RoleEnum.OrgAdmin : (int)RoleEnum.GroupAdmin;
            string permissionCode = owner == ExternalApiKeyOwnerType.Organization
                ? PermissionCodes.OrganizationManage : PermissionCodes.GroupManage;
            var permission = await seed.Context.Permissions
                .SingleOrDefaultAsync(value => value.MasterCode == permissionCode);
            if (permission is null)
            {
                permission = new Permission
                {
                    Id = (await seed.Context.Permissions.Select(value => (int?)value.Id).MaxAsync() ?? 0) + 1,
                    MasterCode = permissionCode,
                    FullName = "Manage issuance owner",
                    ResourceKind = owner == ExternalApiKeyOwnerType.Organization ? "organization" : "group",
                    Action = "manage",
                    GroupName = "Issuance owners",
                    RoleScopeId = owner == ExternalApiKeyOwnerType.Organization
                        ? (int)RoleScopeEnum.Organization : (int)RoleScopeEnum.Group,
                    RoleScope = null!,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                seed.Context.Permissions.Add(permission);
            }
            int permissionId = permission.Id;
            if (!await seed.Context.RolePermissions.AnyAsync(mapping =>
                    mapping.RoleId == roleId && mapping.PermissionId == permissionId))
                seed.Context.RolePermissions.Add(new RolePermission
                {
                    RoleId = roleId,
                    Role = null!,
                    PermissionId = permissionId,
                    Permission = null!,
                    GrantedAt = DateTime.UtcNow
                });
            if (owner == ExternalApiKeyOwnerType.Organization)
            {
                seed.Context.Organizations.Add(new Organization
                {
                    Id = id,
                    Pii = new OrganizationPii { FullName = "Issuance organization" },
                    CreatedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                seed.Context.OrganizationTenants.Add(new OrganizationTenant
                {
                    Id = placement,
                    OrganizationId = id,
                    Organization = null!,
                    TenantId = TenantId,
                    Tenant = null!,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                    ApprovalStatus = null!,
                    IsVisible = true,
                    IsOrganizerEligible = true,
                    CreatedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                seed.Context.OrganizationMembers.Add(new OrganizationMember
                {
                    Id = Guid.CreateVersion7(),
                    OrganizationTenantId = placement,
                    OrganizationTenant = null!,
                    TenantId = TenantId,
                    Tenant = null!,
                    UserId = UserId,
                    User = null!,
                    RoleId = (int)RoleEnum.OrgAdmin,
                    Role = null!,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                seed.Context.Groups.Add(new Group
                {
                    Id = id,
                    FullName = "Issuance group",
                    CreatedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                seed.Context.GroupTenants.Add(new GroupTenant
                {
                    Id = placement,
                    GroupId = id,
                    Group = null!,
                    TenantId = TenantId,
                    Tenant = null!,
                    ApprovalStatusId = (int)ApprovalStatusEnum.Approved,
                    ApprovalStatus = null!,
                    IsVisible = true,
                    IsOrganizerEligible = true,
                    CreatedAt = DateTime.UtcNow,
                    ConcurrencyStamp = Guid.CreateVersion7()
                });
                seed.Context.GroupMembers.Add(new GroupMember
                {
                    Id = Guid.CreateVersion7(),
                    GroupTenantId = placement,
                    GroupTenant = null!,
                    TenantId = TenantId,
                    Tenant = null!,
                    UserId = UserId,
                    User = null!,
                    RoleId = (int)RoleEnum.GroupAdmin,
                    Role = null!,
                    CreatedAt = DateTime.UtcNow
                });
            }
            await seed.Context.SaveChangesAsync();
            return id;
        }

        private static User NewUser(Guid id) => new()
        {
            Id = id,
            Pii = new UserPii { Email = string.Empty, FirstName = "Issuance", LastName = "Principal" },
            CreatedAt = DateTime.UtcNow
        };

        public async ValueTask DisposeAsync()
        {
            if (_sqlite is not null) await _sqlite.DisposeAsync();
            if (_runtime is not null) await _runtime.DisposeAsync();
            _authorityDirectory.Delete(recursive: true);
        }
    }

    private enum BoundaryPoint { AfterInitialAuthorityRead, BeforeIssuanceTransaction, KeyInsert }

    private sealed class ProviderBoundary : DbCommandInterceptor
    {
        private Guid _winner;
        private Guid? _contender;
        private BoundaryPoint _point;
        private int _winnerArmed;
        private int _contenderArmed;
        private int _initialAuthorityRead;
        private Type? _grantType;
        private Guid _grantId;
        internal bool ContenderExcluded { get; private set; }
        internal TaskCompletionSource WinnerArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ContenderArrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseWinner { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal DbTransactionInterceptor Transactions => new TransactionBoundary(this);

        internal async Task SelectGrantAsync(
            ExploreDbContext context, ExternalApiKeyOwnerType owner, Guid userId, Guid tenantId)
        {
            if (owner == ExternalApiKeyOwnerType.InstanceAdmin)
            {
                _grantType = typeof(PlatformUserRole);
                _grantId = await context.PlatformUserRoles.AsNoTracking()
                    .Where(value => value.UserId == userId).Select(value => value.Id).SingleAsync();
            }
            else if (owner == ExternalApiKeyOwnerType.Tenant)
            {
                _grantType = typeof(TenantUserRoleGrant);
                _grantId = await context.TenantUserRoleGrants.AsNoTracking()
                    .Where(value => value.TenantId == tenantId).Select(value => value.Id).SingleAsync();
            }
        }

        internal void Arm(Guid winner, Guid? contender, BoundaryPoint point)
        {
            _winner = winner;
            _contender = contender;
            _point = point;
            Interlocked.Exchange(ref _winnerArmed, 1);
            Interlocked.Exchange(ref _contenderArmed, 1);
        }

        private async Task ObserveAsync(
            DbCommand? command, DbContext? context, CancellationToken token, DbConnection? connection = null)
        {
            if (Invocation.Value == _contender && _contender is not null
                && (command is null && context is EmbeddedPrivacyErasureAuthorityDbContext
                    || command is not null && context is ExploreDbContext application && _grantType is not null
                    && command.CommandText.Contains(application.Model.FindEntityType(_grantType)!.GetTableName()!,
                        StringComparison.Ordinal)
                    && (command.CommandText.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase)
                        || command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)
                        // SQL Server's initial authority read itself conflicts with the held grant write.
                        || command.Connection is SqlConnection))
                && Interlocked.Exchange(ref _contenderArmed, 0) == 1)
            {
                // Attempt the exact native fence on the contender's own connection. NOWAIT/BUSY
                // proves exclusion before signalling; no test gate holds this invocation back.
                await Assert.That(WinnerArrived.Task.IsCompleted && !ReleaseWinner.Task.IsCompleted).IsTrue();
                await AssertExcludedAsync(command?.Connection ?? connection!, context!, token);
                ContenderExcluded = true;
                Console.WriteLine(context is EmbeddedPrivacyErasureAuthorityDbContext
                    ? "Contention witnessed: retained-authority BEGIN IMMEDIATE returned SQLITE_BUSY."
                    : $"Contention witnessed: {context.Database.ProviderName} grant mutation fence excluded contender.");
                ContenderArrived.TrySetResult();
            }
            bool selected = _point == BoundaryPoint.BeforeIssuanceTransaction
                && command is null && context is ExploreDbContext
                || _point == BoundaryPoint.AfterInitialAuthorityRead && Volatile.Read(ref _initialAuthorityRead) == 1
                || command is not null && context is ExploreDbContext
                && _point == BoundaryPoint.KeyInsert
                && command.CommandText.Contains("INSERT INTO", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(context.Model.FindEntityType(typeof(ExternalApiKey))!.GetTableName()!,
                    StringComparison.Ordinal);
            if (Invocation.Value == _winner && selected && Interlocked.Exchange(ref _winnerArmed, 0) == 1)
            {
                WinnerArrived.TrySetResult();
                await ReleaseWinner.Task.WaitAsync(Deadline, token);
            }
        }

        private async Task AssertExcludedAsync(DbConnection connection, DbContext context, CancellationToken token)
        {
            if (connection is SqliteConnection sqlite)
            {
                int busyTimeout = checked(sqlite.DefaultTimeout * 1000);
                SQLitePCL.raw.sqlite3_busy_timeout(sqlite.Handle, 0);
                int result;
                try
                {
                    result = SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "BEGIN IMMEDIATE");
                    if (result == SQLitePCL.raw.SQLITE_OK)
                        SQLitePCL.raw.sqlite3_exec(sqlite.Handle, "ROLLBACK");
                }
                finally
                {
                    SQLitePCL.raw.sqlite3_busy_timeout(sqlite.Handle, busyTimeout);
                }
                await Assert.That(result & 255).IsEqualTo(SQLitePCL.raw.SQLITE_BUSY);
                return;
            }

            // Same exact-row native NOWAIT witness as EventDiscoveryWriterProviderTests.
            // The normal handler/revoker operation follows immediately, with no contender gate.
            var entity = context.Model.FindEntityType(_grantType!)!;
            var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
            var property = entity.FindProperty(nameof(PlatformUserRole.Id))!;
            var sql = context.GetService<ISqlGenerationHelper>();
            string table = sql.DelimitIdentifier(store.Name, store.Schema);
            string key = sql.DelimitIdentifier(property.GetColumnName(store)!);
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
            await using var probe = connection.CreateCommand();
            probe.Transaction = transaction;
            probe.CommandTimeout = 5;
            probe.CommandText = connection is SqlConnection
                ? $"SELECT {key} FROM {table} WITH (XLOCK, ROWLOCK, NOWAIT) WHERE {key} = @id"
                : $"SELECT {key} FROM {table} WHERE {key} = @id FOR UPDATE NOWAIT";
            probe.Parameters.Add(property.GetRelationalTypeMapping().CreateParameter(probe, "@id", _grantId));
            bool excluded = false;
            try
            {
                await probe.ExecuteScalarAsync(token);
            }
            catch (PostgresException exception) when (exception.SqlState == "55P03") { excluded = true; }
            catch (SqlException exception) when (exception.Number == 1222) { excluded = true; }
            catch (MySqlException exception) when (exception.Number is 3572 or 1205) { excluded = true; }
            finally
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            await Assert.That(excluded).IsTrue();
        }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, eventData.Context, cancellationToken);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await ObserveAsync(command, eventData.Context, cancellationToken);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (Invocation.Value == _winner && _point == BoundaryPoint.AfterInitialAuthorityRead
                && eventData.Context is ExploreDbContext context
                && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                && (command.CommandText.Contains(context.Model.FindEntityType(typeof(PlatformUserRole))!.GetTableName()!,
                        StringComparison.Ordinal)
                    || command.CommandText.Contains(context.Model.FindEntityType(typeof(TenantUserRoleGrant))!.GetTableName()!,
                        StringComparison.Ordinal)))
                Interlocked.Exchange(ref _initialAuthorityRead, 1);
            return ValueTask.FromResult(result);
        }

        private sealed class TransactionBoundary(ProviderBoundary owner) : DbTransactionInterceptor
        {
            public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
                DbConnection connection, TransactionStartingEventData eventData,
                InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
            {
                await owner.ObserveAsync(null, eventData.Context, cancellationToken, connection);
                return result;
            }
        }
    }

    private sealed class InjectedCommitFailure : Exception;

    private sealed class CommitFault(bool afterCommit) : DbTransactionInterceptor
    {
        private Guid _invocation;
        private int _armed;
        internal bool Observed { get; private set; }
        internal void Arm(Guid invocation)
        {
            _invocation = invocation;
            Interlocked.Exchange(ref _armed, 1);
        }

        private void Fail(DbContext? context)
        {
            if (Invocation.Value != _invocation || context is not ExploreDbContext
                || Interlocked.Exchange(ref _armed, 0) != 1) return;
            Observed = true;
            throw new InjectedCommitFailure();
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!afterCommit) Fail(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            if (afterCommit) Fail(eventData.Context);
            return Task.CompletedTask;
        }
    }
}

[Category("Runtime")]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class ExternalApiKeyIssuanceProviderTests
{
    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public Task ConfiguredProviderRecoversOnlyMetadata(ExternalApiKeyOwnerType owner) =>
        ExternalApiKeyIssuanceTests.ReplayAsync(true, owner);

    [Test]
    [Arguments(ExternalApiKeyOwnerType.User)]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    public Task ConfiguredProviderSerializesIdenticalOperations(ExternalApiKeyOwnerType owner) =>
        ExternalApiKeyIssuanceTests.IdenticalRaceAsync(true, owner);

    [Test]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    public Task ConfiguredProviderDeniesEarlierCommittedRevocation(ExternalApiKeyOwnerType owner) =>
        ExternalApiKeyIssuanceTests.RevocationBeforeDecisionAsync(true, owner);

    [Test]
    [Arguments(ExternalApiKeyOwnerType.InstanceAdmin)]
    [Arguments(ExternalApiKeyOwnerType.Tenant)]
    public Task ConfiguredProviderAllowsEarlierIssuanceOnlyOnce(ExternalApiKeyOwnerType owner) =>
        ExternalApiKeyIssuanceTests.IssuanceBeforeRevocationAsync(true, owner);

    [Test]
    public Task ConfiguredProviderRollsBackBothRows() =>
        ExternalApiKeyIssuanceTests.FailureBeforeCommitAsync(true);

    [Test]
    public Task ConfiguredProviderRecoversAfterRealCommitLosesAcknowledgement() =>
        ExternalApiKeyIssuanceTests.LostAcknowledgementAsync(true);

    [Test]
    public Task ConfiguredProviderSeparatesNullableTenantAndOwnershipIdentities() =>
        ExternalApiKeyIssuanceTests.IsolationAsync(true);

    [Test]
    [Arguments(ExternalApiKeyOwnerType.Organization)]
    [Arguments(ExternalApiKeyOwnerType.Group)]
    public Task ConfiguredProviderSeparatesOwnersAndRejectsForeignTenantRecovery(ExternalApiKeyOwnerType owner) =>
        ExternalApiKeyIssuanceTests.OwnerIsolationAsync(true, owner);
}
