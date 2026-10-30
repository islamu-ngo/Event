using Explore.Persistence.Database.ProviderPrimitives;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

internal sealed class LocationSettingsNativeFixture : IAsyncDisposable
{
    internal const string Key = GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress;
    private readonly PostgreSqlContainerFixture postgres;
    private readonly IAsyncDisposable authentication;
    private readonly Func<Func<Task>, Task> inRequest;
    internal IServiceProvider Services { get; }
    internal ExploreDbContext Owner => Services.GetRequiredService<ExploreDbContext>();
    internal Guid Actor => Services.GetRequiredService<IHumanCommitScopePreparation>().Subject!.PrincipalId;
    internal EventLocation A { get; private set; } = null!;
    internal EventLocation B { get; private set; } = null!;
    internal Guid TenantA { get; }
    internal Guid TenantB { get; }
    private Guid RestrictedTenant { get; }
    internal bool Instance { get; }
    private Policy[] initial = [];
    private Guid[] audits = [];
    private TenantValue[] tenantValues = [];
    private readonly List<string> revokeOwnedGrants = [];
    internal DateTime Now { get; } = DateTime.UtcNow.AddHours(-1);

    private LocationSettingsNativeFixture(PostgreSqlContainerFixture postgres, IAsyncDisposable authentication,
        IServiceProvider services, Func<Func<Task>, Task> inRequest, Guid tenantA, Guid tenantB, Guid restricted, bool instance)
    {
        this.postgres = postgres;
        this.authentication = authentication;
        this.inRequest = inRequest;
        Services = services;
        TenantA = tenantA;
        TenantB = tenantB;
        RestrictedTenant = restricted;
        Instance = instance;
    }

    internal static async Task<LocationSettingsNativeFixture> CreateAsync(PostgreSqlContainerFixture postgres, bool instance)
    {
        await postgres.ResetAsync();
        Guid a = Guid.CreateVersion7(), b = Guid.CreateVersion7(), restricted = Guid.CreateVersion7();
        await using (var seed = postgres.CreateDbContext())
        {
            foreach (var id in new[] { a, b, restricted })
                seed.Tenants.Add(new Tenant
                {
                    Id = id,
                    Slug = $"governance-{id:N}",
                    FullName = "Governance tenant",
                    TenantStatusId = (int)TenantStatusEnum.Active,
                    TenantStatus = null!
                });
            await seed.SaveChangesAsync();
        }
        LocationSettingsNativeFixture result;
        if (instance)
        {
            var authenticated = await OperatorIdentityEditorNativeFixture.CreateAsync(postgres);
            result = new(postgres, authenticated, authenticated.Services, authenticated.InRequestAsync, a, b, restricted, true);
        }
        else
        {
            var authenticated = await TenantBrandingNativeAuthenticationFixture.CreateAsync(postgres, a);
            result = new(postgres, authenticated, authenticated.Services,
                async action => await authenticated.InOriginalRequestAsync(async () => { await action(); return true; }),
                a, b, restricted, false);
        }
        try { await result.SeedAsync(); return result; }
        catch { await result.DisposeAsync(); throw; }
    }

    private async Task SeedAsync()
    {
        await using var seed = postgres.CreateDbContext();
        var actor = await seed.Actors.SingleAsync(row => row.UserId == Actor);
        A = await AddPlacementAsync(TenantA);
        B = await AddPlacementAsync(TenantB);
        _ = await AddPlacementAsync(RestrictedTenant);
        var detached = EventLocation.CreatePhysical(TenantB, B.EventId, B.LocationId!.Value, Actor, Now);
        detached.DetachFinalReference(Actor, Now);
        await new EventLocationRepository(seed).AddAsync(detached, default);
        seed.TenantSettingOverrides.Add(new TenantSetting
        { TenantId = RestrictedTenant, Tenant = null!, SettingKey = Key, Value = "false", CreatedAt = Now });
        var setting = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == Key);
        if (setting is null)
            seed.SystemSettings.Add(setting = new SystemSetting
            { SettingKey = Key, Value = "true", ValueType = SettingValueType.Boolean, CreatedAt = Now });
        setting.Value = "true";
        setting.IsLocked = false;
        await seed.SaveChangesAsync();
        initial = await PoliciesAsync(seed);
        audits = await seed.EventLocationDisclosureAudits.Select(row => row.Id).ToArrayAsync();
        tenantValues = await seed.TenantSettingOverrides.Where(row => row.SettingKey == Key)
            .Select(row => new TenantValue(row.TenantId, row.Value)).ToArrayAsync();
        var sql = seed.GetService<ISqlGenerationHelper>();
        string Table(Type type)
        {
            var entity = seed.Model.FindEntityType(type)!;
            return sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        }
        string global = postgres.NativeQueueEnrollment.GlobalLogin;
        string runtime = postgres.NativeQueueEnrollment.RuntimeLogin;
        foreach (string privilege in new[] { "SELECT", "INSERT", "UPDATE" })
        {
            await GrantTableAsync(global, typeof(SystemSetting), privilege);
            await GrantTableAsync(runtime, typeof(TenantSetting), privilege);
        }
        foreach (Type type in new[] { typeof(SystemSetting), typeof(EventLocation), typeof(OutboxMessage), typeof(LocationPrivacyCorrectionReceipt) })
            await GrantTableAsync(runtime, type, "SELECT");
        await GrantRoleAsync(global, PostgresLocationPrivacyGovernanceContract.GlobalRole);
        await GrantRoleAsync(runtime, PostgresLocationPrivacyGovernanceContract.RuntimeRole);
        await GrantRoleAsync(global, PostgresDiscoveryDisclosureContract.RuntimeRole);

        async Task GrantTableAsync(string login, Type type, string privilege)
        {
            string table = Table(type);
            if (await seed.Database.SqlQuery<bool>(
                    $"SELECT pg_catalog.has_table_privilege({login}, {table}, {privilege}) AS \"Value\"").SingleAsync())
                return;
            await seed.Database.ExecuteSqlRawAsync($"GRANT {privilege} ON {table} TO {sql.DelimitIdentifier(login)}");
            revokeOwnedGrants.Add($"REVOKE {privilege} ON {table} FROM {sql.DelimitIdentifier(login)}");
        }

        async Task GrantRoleAsync(string login, string role)
        {
            if (await seed.Database.SqlQuery<bool>(
                    $"SELECT pg_catalog.pg_has_role({login}, {role}, 'MEMBER') AS \"Value\"").SingleAsync())
            {
                await Assert.That(await seed.Database.SqlQuery<bool>(
                    $"SELECT pg_catalog.pg_has_role({login}, {role}, 'USAGE') AS \"Value\"").SingleAsync()).IsTrue();
                return;
            }
            await seed.Database.ExecuteSqlRawAsync(
                $"GRANT {sql.DelimitIdentifier(role)} TO {sql.DelimitIdentifier(login)} WITH INHERIT TRUE, SET FALSE");
            revokeOwnedGrants.Add($"REVOKE {sql.DelimitIdentifier(role)} FROM {sql.DelimitIdentifier(login)}");
        }

        async Task<EventLocation> AddPlacementAsync(Guid tenant)
        {
            var source = new Explore.Domain.Event(EventStatusEnum.Draft)
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                ActorId = actor.Id,
                Actor = null!,
                Title = "Governance placement",
                EventStatus = null!,
                EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                VisibilityType = null!,
                EventFormatId = (int)EventFormatEnum.Local,
                EventFormat = null!,
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            var location = new Location
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                FullName = "Governance venue",
                Country = "BE",
                City = "Brussels",
                CreatedAt = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            location.SetManualAddress("Invariant venue street", "1000");
            seed.AddRange(source, location);
            await seed.SaveChangesAsync();
            var placement = EventLocation.CreatePhysical(tenant, source.Id, location.Id, Actor, Now);
            await new EventLocationRepository(seed).AddAsync(placement, default);
            seed.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                EventLocationDisclosureFields.StreetAddress | EventLocationDisclosureFields.Postcode,
                LocationDisclosureAudienceEnum.AnyCurrentRegistrant, null, placement.PolicyVersion, Actor,
                EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange, Now, needsPrivacyReview: false));
            await seed.SaveChangesAsync();
            return placement;
        }
    }

    internal ExploreDbContext Ordinary(Guid? tenant, params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors([PostgresTenantSessionInterceptor.RuntimeInstance, .. interceptors]);
        return new ExploreDbContext(options.Options) { TenantContext = tenant is { } id ? new TenantIdentity(id) : null };
    }

    internal ExploreDbContext Observer() => postgres.CreateDbContext();

    internal async Task<IGenericOutboxAdmission> CreateOutboxAdmissionAsync()
    {
        await using (var setup = postgres.CreateDbContext())
        {
            await PostgresGenericOutboxAdmissionContract.ApplyAsync(setup);
            string login = postgres.NativeQueueEnrollment.GlobalLogin;
            string role = PostgresGenericOutboxAdmissionContract.GlobalRole;
            var sql = setup.GetService<ISqlGenerationHelper>();
            if (!await setup.Database.SqlQuery<bool>(
                    $"SELECT pg_catalog.pg_has_role({login}, {role}, 'MEMBER') AS \"Value\"").SingleAsync())
            {
                await setup.Database.ExecuteSqlRawAsync(
                    $"GRANT {sql.DelimitIdentifier(role)} TO {sql.DelimitIdentifier(login)} WITH INHERIT TRUE, SET FALSE");
                revokeOwnedGrants.Add($"REVOKE {sql.DelimitIdentifier(role)} FROM {sql.DelimitIdentifier(login)}");
            }
            await Assert.That(await setup.Database.SqlQuery<bool>(
                $"SELECT pg_catalog.pg_has_role({login}, {role}, 'USAGE') AS \"Value\"").SingleAsync()).IsTrue();
        }
        // Queue admission gets its actual enrolled, unbound Global pool. It is
        // neither the privileged observation connection nor the settings owner.
        IGenericOutboxAdmissionDbContextFactory contexts = new GlobalExploreDbContextFactory(
            postgres.GetNativeQueueOptions(PrimaryDatabaseRole.Global));
        await using (var global = await contexts.CreateDbContextAsync())
        {
            await Assert.That(global.BindTenantLease()).IsNull();
            await Assert.That(global.IsTenantFilterBypassed).IsFalse();
            await Assert.That(await global.Database.SqlQueryRaw<string>(
                "SELECT current_user::text AS \"Value\"").SingleAsync())
                .IsEqualTo(postgres.NativeQueueEnrollment.GlobalLogin);
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                "SELECT rolsuper OR rolbypassrls AS \"Value\" FROM pg_catalog.pg_roles WHERE rolname = current_user")
                .SingleAsync()).IsFalse();
        }
        return new GenericOutboxAdmission(contexts, TimeProvider.System);
    }

    internal async Task<LocationPrivacyGovernanceMutationResult> ApplyAsync(
        Func<CancellationToken, Task>? afterPersist = null,
        Func<LocationPrivacyGovernanceMutationResult, CancellationToken, Task>? afterMutation = null)
    {
        LocationPrivacyGovernanceMutationResult? result = null;
        await inRequest(async () =>
        {
            var preparation = Services.GetRequiredService<IHumanSettingsMutationPreparation>();
            var proof = await preparation.PrepareAsync(new UpdateSettingCommand
            { Key = Key, Value = "false", Scope = Instance ? SettingScope.Instance : SettingScope.Tenant }, "false");
            var mutationLock = Services.GetRequiredService<ISettingMutationLock>();
            await mutationLock.ExecuteOrderedGroupsAsync([[Key]], async token =>
            {
                async Task<bool> Execute(CancellationToken ownerToken) =>
                    await preparation.ExecutePreparedAsync(proof, async (_, inner) =>
                    {
                        result = await Services.GetRequiredService<ILocationPrivacyGovernanceMutationService>()
                            .ExecuteAsync(Key, "false", Instance ? SettingScope.Instance : SettingScope.Tenant,
                                Instance ? null : TenantA, Actor, async ct =>
                                {
                                    string? previous = null;
                                    if (Instance)
                                        previous = await Services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(
                                            new SystemSetting
                                            {
                                                SettingKey = Key,
                                                Value = "false",
                                                ValueType = SettingValueType.Boolean,
                                                UpdatedBy = Actor,
                                                UpdatedAt = Now
                                            }, ct);
                                    else
                                        await Services.GetRequiredService<ITenantSettingRepository>()
                                            .SetValueAsync(TenantA, Key, "false", ct, Actor);
                                    if (afterPersist is not null) await afterPersist(ct);
                                    return previous;
                                }, inner);
                        if (afterMutation is not null) await afterMutation(result, inner);
                        return true;
                    }, ownerToken);
                return await Execute(token);
            });
        });
        return result ?? throw new InvalidOperationException("The native governance invocation produced no result.");
    }

    internal async Task AssertStateAsync(Guid[] corrected, bool committed)
    {
        await using var observer = Observer();
        var policies = await PoliciesAsync(observer);
        foreach (var previous in initial)
        {
            var current = policies.Single(row => row.Id == previous.Id);
            bool changed = corrected.Contains(previous.Id);
            if (!changed) await Assert.That(current).IsEqualTo(previous);
            else
            {
                await Assert.That(current with
                {
                    Version = previous.Version,
                    Review = previous.Review,
                    Stamp = previous.Stamp,
                    Actor = previous.Actor,
                    ChangedAt = previous.ChangedAt
                }).IsEqualTo(previous);
                await Assert.That(current.Version).IsEqualTo(previous.Version + 1);
                await Assert.That(current.Review).IsTrue();
                await Assert.That(current.Actor).IsEqualTo((Guid?)Actor);
            }
        }
        await Assert.That(await observer.EventLocationDisclosureAudits
            .Where(row => row.Reason != EventLocationDisclosureAuditReasonEnum.GovernanceTightening)
            .Select(row => row.Id).ToArrayAsync()).IsEquivalentTo(audits);
        await Assert.That(await observer.EventLocationDisclosureAudits
            .Where(row => row.Reason == EventLocationDisclosureAuditReasonEnum.GovernanceTightening)
            .Select(row => row.EventLocationId).ToArrayAsync()).IsEquivalentTo(corrected);
        await Assert.That(await observer.OutboxMessages
            .Where(row => row.EventType == LocationPrivacyOutboxMessageFactory.ProjectionCorrectionEventType)
            .Select(row => row.AggregateId).ToArrayAsync()).IsEquivalentTo(corrected);
        await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().Select(row => row.EventLocationId)
            .ToArrayAsync()).IsEquivalentTo(corrected);
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == Key).Select(row => row.Value).SingleAsync())
            .IsEqualTo(committed && Instance ? "false" : "true");
        await Assert.That(await observer.TenantSettingOverrides.Where(row => row.SettingKey == Key)
            .Select(row => new TenantValue(row.TenantId, row.Value)).ToArrayAsync())
            .IsEquivalentTo(committed && !Instance
                ? [new TenantValue(RestrictedTenant, "false"), new TenantValue(TenantA, "false")] : tenantValues);
    }

    private static Task<Policy[]> PoliciesAsync(ExploreDbContext observer) =>
        observer.EventLocations.IgnoreQueryFilters().AsNoTracking().Select(row => new Policy(
            row.Id, row.PolicyVersion, row.NeedsPrivacyReview, row.ConcurrencyStamp, row.LastPolicyActorUserId,
            row.LastPolicyChangedAtUtc, row.ShowStreetAddress, row.ShowPostcode, row.ShowCoordinates,
            row.FullDetailsAudienceId, row.RevealFullDetailsFromUtc, row.IsDeleted)).ToArrayAsync();

    public async ValueTask DisposeAsync()
    {
        await authentication.DisposeAsync();
        await using var setup = postgres.CreateDbContext();
        for (int index = revokeOwnedGrants.Count - 1; index >= 0; index--)
            await setup.Database.ExecuteSqlRawAsync(revokeOwnedGrants[index]);
    }
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed record TenantValue(Guid TenantId, string Value);
    private sealed record Policy(Guid Id, int Version, bool Review, Guid Stamp, Guid? Actor, DateTime? ChangedAt,
        bool Street, bool Postcode, bool Coordinates, int Audience, DateTime? Reveal, bool Deleted);
}
