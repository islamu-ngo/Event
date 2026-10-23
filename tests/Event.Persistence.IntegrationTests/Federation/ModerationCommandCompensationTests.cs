using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
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
using Explore.Persistence.Identity;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

    [Test]
    public async Task TwoLineageDiscoveryReadsDedicatedPolicyOncePerPage()
    {
        using var probe = new CompensationQueryProbe();
        await ExecuteCaseAsync(identityOnly: false, failAfterWrite: false, multipleLineages: true,
            queryProbe: probe);

        await Assert.That(probe.Observations.Count).IsEqualTo(2);
        foreach (var observation in probe.Observations)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Compensation discovery: lineages={observation.Lineages}; instance_selects={observation.InstanceSelects}; tenant_selects={observation.TenantSelects}; dedicated_command_ms={observation.Duration.TotalMilliseconds:F3}"));
        }
        foreach (var observation in probe.Observations)
        {
            await Assert.That(observation.Lineages).IsEqualTo(2);
            await Assert.That(observation.InstanceSelects).IsEqualTo(1);
            await Assert.That(observation.TenantSelects).IsEqualTo(1);
        }
    }

    [Test]
    public async Task CancelledBufferedDiscoveryResumeRollsBackTheOriginalCommand()
    {
        using var probe = new CompensationQueryProbe();
        await ExecuteCaseAsync(identityOnly: false, failAfterWrite: false, multipleLineages: true,
            queryProbe: probe, cancelOnResume: true);

        await Assert.That(probe.CompletedWrites).IsEqualTo(1);
        await Assert.That(probe.Observations.Count).IsEqualTo(1);
        var observation = probe.Observations.Single();
        await Assert.That(observation.Lineages).IsEqualTo(1);
        await Assert.That(observation.InstanceSelects).IsEqualTo(1);
        await Assert.That(observation.TenantSelects).IsEqualTo(1);
    }

    [Test]
    [Arguments(256, 1)]
    [Arguments(257, 2)]
    public async Task DiscoveryReadsDedicatedPolicyOnlyForNonemptySourcePages(int lineageCount, int expectedPages)
    {
        using var probe = new CompensationQueryProbe();
        await ExecuteCaseAsync(identityOnly: false, failAfterWrite: false, multipleLineages: true,
            queryProbe: probe, additionalLineages: lineageCount - 2);

        await Assert.That(probe.Observations.Count).IsEqualTo(2);
        foreach (var observation in probe.Observations)
        {
            Console.WriteLine(FormattableString.Invariant(
                $"Compensation discovery boundary: lineages={observation.Lineages}; instance_selects={observation.InstanceSelects}; tenant_selects={observation.TenantSelects}; dedicated_command_ms={observation.Duration.TotalMilliseconds:F3}"));
        }
        foreach (var observation in probe.Observations)
        {
            await Assert.That(observation.Lineages).IsEqualTo(lineageCount);
            await Assert.That(observation.InstanceSelects).IsEqualTo(expectedPages);
            await Assert.That(observation.TenantSelects).IsEqualTo(expectedPages);
        }
    }

    [Test]
    [Arguments(false, false, null, false)]
    [Arguments(false, false, true, true)]
    [Arguments(false, true, true, false)]
    [Arguments(true, true, false, true)]
    [Arguments(true, false, false, false)]
    public Task CompensationUsesDedicatedCapabilityAndInstanceLockWithoutLegacyRows(
        bool instanceEnabled, bool instanceLocked, bool? tenantEnabled, bool expectedEnabled) =>
        ExecuteCaseAsync(identityOnly: false, failAfterWrite: false, multipleLineages: false,
            instanceEnabled, instanceLocked, tenantEnabled, expectedEnabled);

    private static async Task ExecuteCaseAsync(
        bool identityOnly, bool failAfterWrite, bool multipleLineages,
        bool instanceEnabled = true, bool instanceLocked = false, bool? tenantEnabled = null,
        bool expectedEnabled = true, CompensationQueryProbe? queryProbe = null, int additionalLineages = 0,
        bool cancelOnResume = false)
    {
        int expectedLineages = (multipleLineages ? 2 : 1) + additionalLineages;
        bool expectRollback = failAfterWrite || cancelOnResume;
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
        {
            ConfigureCompensationAuthentication(services);
            services.AddScoped<IAtprotoModerationCompensationStore>(provider =>
                new FailAfterWriteStore(
                    new AtprotoModerationCompensationStore(provider.GetRequiredService<ExploreDbContext>(), null),
                    provider.GetRequiredService<ExploreDbContext>(), failAfterWrite, queryProbe, cancelOnResume));
        });
        await AuthenticateAdministratorAsync(fixture);
        var context = fixture.Context;
        var source = await fixture.SeedEventAsync(published: true);
        DateTime now = source.CreatedAt;
        const string did = "did:plc:command-compensation";
        Guid identityId = Guid.CreateVersion7();
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
        var capability = await context.Set<AtprotoFederationInstanceSetting>().SingleAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled);
        capability.SetValue(instanceEnabled ? "true" : "false", now, null);
        capability.SetLock(instanceLocked, now, null);
        if (tenantEnabled is { } tenantCapability)
            context.Set<AtprotoFederationTenantSetting>().Add(AtprotoFederationTenantSetting.Create(
                fixture.TenantId, GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                tenantCapability ? "true" : "false", false, now, null));
        foreach ((string key, string value, SettingValueType type) in new[]
        {
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
        for (int lineage = 1; lineage < expectedLineages; lineage++)
            context.PdsSyncOutbox.Add(new PdsSyncOutbox
            {
                Id = Guid.CreateVersion7(),
                TenantId = fixture.TenantId,
                UserId = fixture.UserId,
                Did = did,
                Collection = predecessor.Collection,
                RecordKey = lineage == 1 ? "another-remote-record" : $"another-remote-record-{lineage:D3}",
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
        await Assert.That(await context.SystemSettings.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled)).IsFalse();
        await Assert.That(await context.TenantSettingOverrides.AnyAsync(row =>
            row.SettingKey == GovernanceSettingKeys.Federation.AtprotoEventsEnabled)).IsFalse();
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

        if (cancelOnResume)
            await Assert.ThrowsAsync<OperationCanceledException>(ExecuteAsync);
        else if (failAfterWrite)
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
        await Assert.That(suspended).IsEqualTo(!expectRollback);
        await Assert.That(audits).IsEqualTo(expectRollback ? 0 : 1);
        await Assert.That(deletes.Count).IsEqualTo(expectRollback || !expectedEnabled ? 0 : expectedLineages);
        await Assert.That(persisted.Payload).IsEqualTo(predecessor.Payload);
        await Assert.That(persisted.Status).IsEqualTo(
            expectRollback || !expectedEnabled ? PdsSyncStatus.Processing : PdsSyncStatus.Superseded);
        await Assert.That(persisted.LeaseToken).IsEqualTo(expectRollback || !expectedEnabled ? originalLease : null);
        long disclosureAfter = await context.Set<EventDiscoveryRevision>()
            .Where(row => row.TenantId == fixture.TenantId)
            .Select(row => row.DisclosureEpoch).SingleAsync();
        if (expectRollback)
            await Assert.That(disclosureAfter).IsEqualTo(disclosureBefore);
        else
            await Assert.That(disclosureAfter).IsGreaterThan(disclosureBefore);
        if (!expectRollback && expectedEnabled)
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
                .IsEqualTo(expectedLineages);
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

    private static void ConfigureCompensationAuthentication(IServiceCollection services)
    {
        // Keep this fixture's isolated SQLite selection, but use the graph lane's explicit
        // secret and pinned password-policy authority rather than synthetic credentials.
        var authority = NativeDraftGraphAuthentication.CreateConfiguration();
        if (!string.Equals(authority["Authentication:Local:JwtKey"],
                authority["AUTHENTICATION_LOCAL_JWT_KEY"], StringComparison.Ordinal))
            throw new InvalidOperationException("Compensation requires one matching Local JWT environment authority.");
        var configuration = services.Last(descriptor => descriptor.ServiceType == typeof(IConfiguration))
            .ImplementationInstance as IConfiguration
            ?? throw new InvalidOperationException("The fixture's original configuration is missing.");
        foreach (string key in new[]
        {
            "Authentication:Local:JwtKey", "AUTHENTICATION_LOCAL_JWT_KEY",
            "Authentication:Local:PasswordBlocklistPath", "Authentication:Local:PasswordBlocklistSha256"
        })
        {
            if (string.IsNullOrWhiteSpace(authority[key]))
                throw new InvalidOperationException("Compensation requires the explicit Local JWT and pinned password policy authority.");
            configuration[key] = authority[key];
        }
        var policy = configuration.GetSection(LocalIdentityOptions.SectionName).Get<LocalIdentityOptions>()
            ?? throw new InvalidOperationException("The configured Local password policy is missing.");
        var snapshot = LocalPasswordBlocklistSnapshot.Load(policy);
        if (!snapshot.IsUsable)
            throw new InvalidOperationException("The configured Local password blocklist was not admitted.");
        services.RemoveAll<LocalPasswordBlocklistSnapshot>();
        services.AddSingleton(snapshot);
    }

    private static async Task AuthenticateAdministratorAsync(EventVisitorCapabilitySqliteFixture fixture)
    {
        Guid operatorId = Guid.CreateVersion7();
        var services = fixture.Services;
        var receipt = await NativeDraftGraphAuthentication.CreatePendingAsync(services, operatorId);
        if (receipt.LocalSubjectId != operatorId || receipt.PersonalActorId == fixture.ActorId)
            throw new InvalidOperationException("Compensation requires an independent receipt-bound Local administrator.");
        var context = fixture.Context;
        context.Users.Add(new User
        {
            Id = operatorId,
            Pii = new UserPii
            {
                Email = NativeDraftGraphAuthentication.Email(operatorId),
                FirstName = "Compensation",
                LastName = "Administrator"
            },
            EmailVerified = true
        });
        context.Actors.Add(new Actor
        {
            Id = receipt.PersonalActorId,
            UserId = operatorId,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Compensation administrator" }
        });
        context.UserExternalLogins.Add(new UserExternalLogin
        {
            Id = receipt.ExternalLoginId,
            UserId = operatorId,
            User = null!,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
            AuthenticationProvider = null!,
            ProviderKey = operatorId.ToString("D")
        });
        int adminRole = await context.Roles.Where(row =>
            row.RoleScopeId == (int)RoleScopeEnum.Platform && row.MasterCode == "platform.admin")
            .Select(row => row.Id).SingleAsync();
        context.PlatformUserRoles.Add(new PlatformUserRole
        {
            Id = Guid.CreateVersion7(),
            UserId = operatorId,
            User = null!,
            RoleId = adminRole,
            Role = null!,
            GrantedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        string token = await NativeDraftGraphAuthentication.ActivateAndLoginAsync(services, receipt);
        var request = new DefaultHttpContext { RequestServices = services };
        services.GetRequiredService<IHttpContextAccessor>().HttpContext = request;
        request.Request.Headers.Authorization = "Bearer " + token;
        var authenticated = await request.AuthenticateAsync();
        if (!authenticated.Succeeded || authenticated.Principal is null)
            throw new InvalidOperationException("The production Local validator rejected the compensation administrator.");
        request.User = authenticated.Principal;
        var lifetime = await services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
            .ObserveAsync(request.User, CancellationToken.None);
        if (lifetime?.LocalSession?.LocalSubjectId != operatorId)
            throw new InvalidOperationException("The compensation request lost its validated Local administrator lifetime.");
        // Do not prepare a tenant owner here. The real moderation command must select
        // its sealed-null Global context before binding this authenticated lifetime.
    }

    // The real store executes first, so the failure tests actual persisted writes and supersession rollback.
    private sealed class FailAfterWriteStore(AtprotoModerationCompensationStore inner, ExploreDbContext context,
        bool fail, CompensationQueryProbe? queryProbe, bool cancelOnResume)
        : IAtprotoModerationCompensationStore
    {
        private DbConnection? originalConnection;
        private DbTransaction? originalTransaction;

        public async IAsyncEnumerable<AtprotoModerationDeleteCandidate> ReadDeleteCandidatesAsync(
            AtprotoModerationTarget target, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            originalConnection = context.Database.GetDbConnection();
            originalTransaction = context.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("Compensation must observe its original transaction.");
            await Assert.That(originalTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            queryProbe?.BeginEnumeration(context);
            using var discoveryCancellation = cancelOnResume
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
            try
            {
                CancellationToken discoveryToken = discoveryCancellation?.Token ?? cancellationToken;
                await using var candidates = inner.ReadDeleteCandidatesAsync(target, discoveryToken)
                    .GetAsyncEnumerator(discoveryToken);
                while (true)
                {
                    bool moved;
                    queryProbe?.BeginMoveNext();
                    try
                    {
                        moved = await candidates.MoveNextAsync();
                    }
                    finally
                    {
                        queryProbe?.EndMoveNext();
                    }
                    if (!moved)
                        break;
                    await AssertOriginalOwnerAsync();
                    queryProbe?.RecordLineage();
                    yield return candidates.Current;
                    discoveryCancellation?.Cancel();
                }
                await AssertOriginalOwnerAsync();
            }
            finally
            {
                queryProbe?.EndEnumeration();
                await AssertOriginalOwnerAsync();
            }
        }

        public async Task WriteDeleteAsync(
            AtprotoModerationTarget target, AtprotoModerationDeleteWrite write, CancellationToken cancellationToken)
        {
            await AssertOriginalOwnerAsync();
            await inner.WriteDeleteAsync(target, write, cancellationToken);
            await AssertOriginalOwnerAsync();
            queryProbe?.RecordWrite();
            if (fail)
                throw new CompensationRollbackException();
        }

        private async Task AssertOriginalOwnerAsync()
        {
            await Assert.That(ReferenceEquals(originalConnection, context.Database.GetDbConnection())).IsTrue();
            await Assert.That(ReferenceEquals(originalTransaction,
                context.Database.CurrentTransaction?.GetDbTransaction())).IsTrue();
            await Assert.That(originalConnection?.State).IsEqualTo(ConnectionState.Open);
        }
    }

    private sealed class CompensationQueryProbe : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly IDisposable allListeners;
        private readonly System.Collections.Concurrent.ConcurrentBag<IDisposable> subscriptions = [];
        private ExploreDbContext? selectedContext;
        private DbContextId selectedContextId;
        private string instanceTable = string.Empty;
        private string tenantTable = string.Empty;
        private QueryObservation? current;
        private bool readingCandidates;

        public CompensationQueryProbe() => allListeners = DiagnosticListener.AllListeners.Subscribe(this);

        public List<QueryObservation> Observations { get; } = [];
        public int CompletedWrites { get; private set; }

        public void BeginEnumeration(ExploreDbContext context)
        {
            if (current is not null)
                throw new InvalidOperationException("Compensation query observation cannot overlap enumerations.");
            selectedContext = context;
            selectedContextId = context.ContextId;
            var sql = context.GetService<ISqlGenerationHelper>();
            instanceTable = sql.DelimitIdentifier(context.Model.FindEntityType(typeof(AtprotoFederationInstanceSetting))!
                .GetTableName()!);
            tenantTable = sql.DelimitIdentifier(context.Model.FindEntityType(typeof(AtprotoFederationTenantSetting))!
                .GetTableName()!);
            current = new QueryObservation();
        }

        // Count discovery SQL, not writes that the planner executes between yielded candidates.
        public void BeginMoveNext() => readingCandidates = true;
        public void EndMoveNext() => readingCandidates = false;
        public void RecordLineage() => current!.Lineages++;
        public void RecordWrite() => CompletedWrites++;

        public void EndEnumeration()
        {
            readingCandidates = false;
            Observations.Add(current ?? throw new InvalidOperationException("No compensation observation is active."));
            current = null;
            selectedContext = null;
        }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.EntityFrameworkCore")
                subscriptions.Add(listener.Subscribe(this,
                    name => name == RelationalEventId.CommandExecuted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> diagnostic)
        {
            if (!readingCandidates || current is not { } observation
                || diagnostic.Value is not CommandExecutedEventData command
                || !ReferenceEquals(command.Context, selectedContext)
                || command.Context?.ContextId != selectedContextId
                || command.ExecuteMethod != DbCommandMethod.ExecuteReader)
                return;

            string sql = command.Command.CommandText;
            if (sql.Contains(instanceTable, StringComparison.Ordinal))
                observation.InstanceSelects++;
            else if (sql.Contains(tenantTable, StringComparison.Ordinal))
                observation.TenantSelects++;
            else
                return;
            observation.Duration += command.Duration;
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;

        public void Dispose()
        {
            allListeners.Dispose();
            foreach (var subscription in subscriptions)
                subscription.Dispose();
        }

        public sealed class QueryObservation
        {
            public int Lineages { get; set; }
            public int InstanceSelects { get; set; }
            public int TenantSelects { get; set; }
            public TimeSpan Duration { get; set; }
        }
    }

    private sealed class CompensationRollbackException : Exception;
}
