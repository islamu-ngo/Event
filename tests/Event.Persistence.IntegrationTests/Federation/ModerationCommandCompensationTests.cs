using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Actors.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed class ModerationCommandCompensationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task NativeCommandCommitsOrRollsBackSubjectAuditAndRealCompensation(
        bool identityOnly, bool failAfterWrite) =>
        ExecuteCaseAsync(identityOnly, failAfterWrite, multipleLineages: false);

    [Test]
    public Task NativeCommandCompensatesEveryExactRemoteLineageForOneSourceVersion() =>
        ExecuteCaseAsync(identityOnly: false, failAfterWrite: false, multipleLineages: true);

    private static async Task ExecuteCaseAsync(
        bool identityOnly, bool failAfterWrite, bool multipleLineages)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            services.AddScoped<IAtprotoModerationCompensationStore>(provider =>
                new FailAfterWriteStore(
                    new AtprotoModerationCompensationStore(provider.GetRequiredService<ExploreDbContext>()),
                    failAfterWrite)));
        var context = fixture.Context;
        var source = await fixture.SeedEventAsync(published: true);
        DateTime now = source.CreatedAt;
        const string did = "did:plc:command-compensation";
        Guid identityId = Guid.CreateVersion7();
        int adminRole = await context.Roles.Where(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin")
            .Select(row => row.Id).SingleAsync();
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            RoleId = adminRole,
            Role = null!,
            GrantedAt = now
        });
        context.AtprotoIdentities.Add(new AtprotoIdentity(AtprotoDid.Parse(did))
        {
            Id = identityId,
            ActorId = fixture.ActorId,
            Actor = null!,
            PdsHost = "https://pds.example.test/",
            IsActive = true,
            CreatedAt = now,
            ConcurrencyStamp = Guid.CreateVersion7()
        });
        foreach ((string key, string value, SettingValueType type) in new[]
        {
            (GovernanceSettingKeys.Federation.AtprotoEventsEnabled, "true", SettingValueType.Boolean),
            (GovernanceSettingKeys.Federation.AtprotoEventValidationProfile, "\"platform\"", SettingValueType.String),
            (GovernanceSettingKeys.Federation.AtprotoPublishMyEvents, "false", SettingValueType.Boolean)
        })
        {
            var setting = await context.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == key);
            if (setting is null)
                context.SystemSettings.Add(new SystemSetting
                {
                    Id = Guid.CreateVersion7(),
                    SettingKey = key,
                    Value = value,
                    ValueType = type,
                    CreatedAt = now
                });
            else
            {
                setting.Value = value;
                setting.IsLocked = false;
            }
        }
        context.UserPreferences.Add(new UserPreference
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            Tenant = null!,
            UserId = fixture.UserId,
            SettingKey = GovernanceSettingKeys.Federation.AtprotoPublishMyEvents,
            Value = "true",
            CreatedAt = now
        });
        context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Atproto,
            AuthenticationProvider = null!,
            ProviderKey = did,
            CreatedAt = now
        });
        context.UserAuthenticationTokens.Add(new UserAuthenticationToken
        {
            Id = Guid.CreateVersion7(),
            UserId = fixture.UserId,
            User = null!,
            TenantId = fixture.TenantId,
            Tenant = null!,
            Provider = AuthenticationProviderDeploymentOptions.AtprotoProvider,
            SubjectDid = did,
            PdsHost = "https://pds.example.test/",
            SessionCiphertext = RandomNumberGenerator.GetBytes(64),
            EncryptionKeyId = Guid.CreateVersion7().ToString("N"),
            OAuthClientKeyId = Guid.CreateVersion7().ToString("N"),
            EnvelopeVersion = 1,
            ConcurrencyStamp = Guid.CreateVersion7(),
            CreatedAt = now
        });
        var predecessor = new PdsSyncOutbox
        {
            Id = Guid.CreateVersion7(),
            TenantId = fixture.TenantId,
            UserId = fixture.UserId,
            Did = did,
            Collection = "community.lexicon.calendar.event",
            RecordKey = "compensation",
            SourceEntityType = "Event",
            SourceEntityId = source.Id,
            SourceVersion = source.ConcurrencyStamp,
            Operation = PdsSyncOperation.Create,
            Payload = "{\"name\":\"Private predecessor evidence\"}",
            PayloadHash = Guid.CreateVersion7().ToString("N"),
            IdempotencyKey = Guid.CreateVersion7().ToString("N"),
            PdsHost = "https://pds.example.test/",
            Status = PdsSyncStatus.Processing,
            CreatedAt = now,
            MaxRetries = 10,
            LeaseOwner = "held-worker",
            LeaseToken = Guid.CreateVersion7(),
            LeaseFence = 3,
            LeaseExpiresAt = now.AddMinutes(10)
        };
        Guid? originalLease = predecessor.LeaseToken;
        context.PdsSyncOutbox.Add(predecessor);
        if (multipleLineages)
            context.PdsSyncOutbox.Add(new PdsSyncOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                UserId = fixture.UserId,
                Did = did,
                Collection = predecessor.Collection,
                RecordKey = "another-remote-record",
                SourceEntityType = "Event",
                SourceEntityId = source.Id,
                SourceVersion = source.ConcurrencyStamp,
                Operation = PdsSyncOperation.Update,
                Payload = "{}",
                PayloadHash = Convert.ToHexStringLower(SHA256.HashData("{}"u8)),
                IdempotencyKey = Guid.CreateVersion7().ToString("N"),
                PdsHost = predecessor.PdsHost,
                Status = PdsSyncStatus.Pending,
                CreatedAt = now,
                MaxRetries = 10
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        long disclosureBefore = await context.Set<EventDiscoveryRevision>()
            .Where(row => row.TenantId == fixture.TenantId)
            .Select(row => row.DisclosureEpoch).SingleAsync();

        async Task ExecuteAsync()
        {
            var moderation = new GlobalModerationRequest
            {
                Action = GlobalModerationAction.Suspend,
                ReasonCode = "policy_violation"
            };
            BaseCommandResponse<Guid> response = identityOnly
                ? await fixture.ExecuteCommandAsync<ModerateAtprotoIdentityCommand, BaseCommandResponse<Guid>>(
                    new() { AtprotoIdentityId = identityId, Moderation = moderation })
                : await fixture.ExecuteCommandAsync<ModerateActorCommand, BaseCommandResponse<Guid>>(
                    new() { ActorId = fixture.ActorId, Moderation = moderation });
            await Assert.That(response.Id).IsEqualTo(identityOnly ? identityId : fixture.ActorId);
        }

        if (failAfterWrite)
            await Assert.ThrowsAsync<CompensationRollbackException>(ExecuteAsync);
        else
            await ExecuteAsync();

        context.ChangeTracker.Clear();
        bool suspended = identityOnly
            ? await context.AtprotoIdentities.Where(row => row.Id == identityId)
                .Select(row => row.IsSuspended).SingleAsync()
            : await context.Actors.IgnoreAutoIncludes().Where(row => row.Id == fixture.ActorId)
                .Select(row => row.IsSuspended).SingleAsync();
        int audits = identityOnly
            ? await context.Set<AtprotoIdentityModerationRecord>().CountAsync(row => row.AtprotoIdentityId == identityId)
            : await context.Set<ActorModerationRecord>().CountAsync(row => row.ActorId == fixture.ActorId);
        var persisted = await context.PdsSyncOutbox.AsNoTracking().SingleAsync(row => row.Id == predecessor.Id);
        var deletes = await context.PdsSyncOutbox.AsNoTracking()
            .Where(row => row.Operation == PdsSyncOperation.Delete).ToListAsync();
        await Assert.That(suspended).IsEqualTo(!failAfterWrite);
        await Assert.That(audits).IsEqualTo(failAfterWrite ? 0 : 1);
        await Assert.That(deletes.Count).IsEqualTo(failAfterWrite ? 0 : multipleLineages ? 2 : 1);
        await Assert.That(persisted.Payload).IsEqualTo(predecessor.Payload);
        await Assert.That(persisted.Status).IsEqualTo(
            failAfterWrite ? PdsSyncStatus.Processing : PdsSyncStatus.Superseded);
        await Assert.That(persisted.LeaseToken).IsEqualTo(failAfterWrite ? originalLease : null);
        long disclosureAfter = await context.Set<EventDiscoveryRevision>()
            .Where(row => row.TenantId == fixture.TenantId)
            .Select(row => row.DisclosureEpoch).SingleAsync();
        if (failAfterWrite)
            await Assert.That(disclosureAfter).IsEqualTo(disclosureBefore);
        else
            await Assert.That(disclosureAfter).IsGreaterThan(disclosureBefore);
        if (!failAfterWrite)
        {
            var successor = deletes.Single(row => row.RecordKey == predecessor.RecordKey);
            await Assert.That(persisted.SupersededById).IsEqualTo(successor.Id);
            await Assert.That(successor.Payload).IsNull();
            await Assert.That(successor.NextRetryAt >= predecessor.LeaseExpiresAt).IsTrue();
            var lateMutation = new PdsSyncOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                UserId = fixture.UserId,
                Did = did,
                Collection = predecessor.Collection,
                RecordKey = predecessor.RecordKey,
                SourceEntityType = "Event",
                SourceEntityId = source.Id,
                SourceVersion = source.ConcurrencyStamp,
                Operation = PdsSyncOperation.Update,
                Payload = predecessor.Payload,
                PayloadHash = predecessor.PayloadHash,
                IdempotencyKey = Guid.CreateVersion7().ToString("N"),
                PdsHost = predecessor.PdsHost,
                Status = PdsSyncStatus.Pending,
                CreatedAt = now,
                MaxRetries = 10
            };
            context.PdsSyncOutbox.Add(lateMutation);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            // Already suspended subjects must still reconcile newly queued remote work.
            await ExecuteAsync();
            await Assert.That(await context.PdsSyncOutbox.AsNoTracking().CountAsync(row =>
                row.Operation == PdsSyncOperation.Delete && row.Status == PdsSyncStatus.Pending))
                .IsEqualTo(multipleLineages ? 2 : 1);
            await Assert.That(await context.PdsSyncOutbox.AsNoTracking().Where(row => row.Id == lateMutation.Id)
                .Select(row => row.Status).SingleAsync()).IsEqualTo(PdsSyncStatus.Superseded);
            await Assert.That(identityOnly
                ? await context.Set<AtprotoIdentityModerationRecord>().CountAsync(row => row.AtprotoIdentityId == identityId)
                : await context.Set<ActorModerationRecord>().CountAsync(row => row.ActorId == fixture.ActorId)).IsEqualTo(1);
        }
        await Assert.That(context.TenantFilterTenantId).IsEqualTo(fixture.TenantId);
        await Assert.That(context.ChangeTracker.Entries<ActorPii>()).IsEmpty();
        await Assert.That(context.ChangeTracker.Entries<UserAuthenticationToken>()).IsEmpty();
    }

    // The real store executes first, so the failure tests actual persisted writes and supersession rollback.
    private sealed class FailAfterWriteStore(AtprotoModerationCompensationStore inner, bool fail)
        : IAtprotoModerationCompensationStore
    {
        public async IAsyncEnumerable<AtprotoModerationDeleteCandidate> ReadDeleteCandidatesAsync(
            AtprotoModerationTarget target, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var candidate in inner.ReadDeleteCandidatesAsync(target, cancellationToken))
                yield return candidate;
        }

        public async Task WriteDeleteAsync(
            AtprotoModerationTarget target, AtprotoModerationDeleteWrite write, CancellationToken cancellationToken)
        {
            await inner.WriteDeleteAsync(target, write, cancellationToken);
            if (fail)
                throw new CompensationRollbackException();
        }
    }

    private sealed class CompensationRollbackException : Exception;
}
