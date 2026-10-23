using System.Data.Common;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Exceptions;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Persistence.IntegrationTests.Federation;

public sealed partial class AtprotoFederationSettingsMutationTests
{
    private const string ProfileKey = GovernanceSettingKeys.Federation.AtprotoEventValidationProfile;
    private const string InitialProfile = "\"platform\"";
    private const string ChangedProfile = "\"community_lexicon\"";

    [Test]
    [Arguments(SettingScope.Instance)]
    [Arguments(SettingScope.Tenant)]
    public async Task ActualBatchCommitsDedicatedAndUnmovedEffectsWithOneDisclosureAdvancement(SettingScope scope)
    {
        await using var scenario = await CreateScenarioAsync();
        await SeedUnmovedProfileAsync(scenario);
        DatabaseState before = null!;
        ProfileState profileBefore = null!;
        await scenario.Authentication.RunAsync(scope == SettingScope.Instance ? null : scenario.Tenant, async session =>
        {
            // RunAsync first establishes the real tenant membership. Its disclosure
            // effect belongs to fixture setup, not to this original batch.
            before = await ReadAsync(scenario);
            profileBefore = await ReadProfileAsync(scenario);
            var command = ProfileBatch(scope);
            await Assert.That(MixedSettingsBatchMutation.RequiresCombinedSource(command)).IsFalse();
            var response = await BatchHandler(session).ExecuteAsync(command, CancellationToken.None);
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Count).IsEqualTo(2);
            await Assert.That(response.Results.All(result => result.Applied)).IsTrue();
            return true;
        });

        var after = await ReadAsync(scenario);
        var profileAfter = await ReadProfileAsync(scenario);
        await AssertBatchDedicatedAsync(scenario, before, after, scope);
        await AssertEpochsAsync(before, after, root: scope == SettingScope.Instance);
        await AssertLegacyUnchangedAsync(before, after);
        if (scope == SettingScope.Instance)
        {
            await Assert.That(profileAfter.System.UpdatedAt).IsNotNull();
            await Assert.That(profileAfter.System).IsEqualTo(profileBefore.System with
            {
                Value = ChangedProfile,
                UpdatedAt = profileAfter.System.UpdatedAt,
                UpdatedBy = scenario.Authentication.ActorId
            });
            await Assert.That(profileAfter.Target).IsEqualTo(profileBefore.Target);
        }
        else
        {
            await Assert.That(profileAfter.Target.UpdatedAt).IsNotNull();
            await Assert.That(profileAfter.Target).IsEqualTo(profileBefore.Target with
            {
                Value = ChangedProfile,
                UpdatedAt = profileAfter.Target.UpdatedAt,
                UpdatedBy = scenario.Authentication.ActorId
            });
            await Assert.That(profileAfter.System).IsEqualTo(profileBefore.System);
        }
        await Assert.That(profileAfter.Foreign).IsEqualTo(profileBefore.Foreign);
    }

    [Test]
    public async Task ActualMixedBatchCanAcceptOnlyDedicatedEffectsAndCreateItsMissingTenantOverride()
    {
        await using var scenario = await CreateScenarioAsync(missingTarget: true);
        await SeedUnmovedProfileAsync(scenario);
        DatabaseState before = null!;
        ProfileState profileBefore = null!;
        RejectedSmtpState smtpBefore = null!;
        await scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            profileBefore = await ReadProfileAsync(scenario);
            smtpBefore = await ReadRejectedSmtpAsync(scenario);
            await Assert.That(before.Target).IsNull();
            var command = new UpdateSettingBatchCommand
            {
                Category = AtprotoFederationSettingDefinitions.Category,
                Scope = SettingScope.Tenant,
                Mode = BatchUpdateMode.BestEffort,
                Values = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [Key] = "true",
                    [GovernanceSettingKeys.Email.SmtpHost] = "mail.example.test"
                }
            };
            await Assert.That(MixedSettingsBatchMutation.RequiresCombinedSource(command)).IsTrue();
            var response = await BatchHandler(session).ExecuteAsync(command, CancellationToken.None);
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Count).IsEqualTo(2);
            await Assert.That(response.Results.Single(result => result.Key == Key).Applied).IsTrue();
            var rejected = response.Results.Single(result => result.Key == GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(rejected.Applied).IsFalse();
            await Assert.That(rejected.SkipReason).Contains("does not belong to category");
            return true;
        }, mixed: true);

        var after = await ReadAsync(scenario);
        await Assert.That(after.Target).IsNotNull();
        await Assert.That(after.Target!.Id).IsNotEqualTo(Guid.Empty);
        await Assert.That(after.Target.Id.Version).IsEqualTo(7);
        await Assert.That(after.Target.Value).IsEqualTo("true");
        await Assert.That(after.Target.IsLocked).IsFalse();
        await Assert.That(after.Target.CreatedBy).IsEqualTo(scenario.Authentication.ActorId);
        await Assert.That(after.Target.UpdatedAt).IsNull();
        await Assert.That(after.Target.UpdatedBy).IsNull();
        await Assert.That(after.Instance).IsEqualTo(before.Instance);
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        await Assert.That(await ReadProfileAsync(scenario)).IsEqualTo(profileBefore);
        await Assert.That(await ReadRejectedSmtpAsync(scenario)).IsEqualTo(smtpBefore);
        await AssertEpochsAsync(before, after);
        await AssertLegacyUnchangedAsync(before, after);
    }

    [Test]
    public async Task ActualBatchLateFailureRollsBackBothWriterEffectsAndTheirCompletedDisclosure()
    {
        var failure = new BatchCommitFailure();
        await using var scenario = await CreateScenarioAsync(interceptor: failure);
        await SeedUnmovedProfileAsync(scenario);
        DatabaseState before = null!;
        ProfileState profileBefore = null!;
        var commitFailure = await Assert.ThrowsAsync<HumanMutationOutcomeUnknownException>(() => scenario.Authentication.RunAsync(scenario.Tenant, async session =>
        {
            before = await ReadAsync(scenario);
            profileBefore = await ReadProfileAsync(scenario);
            failure.Arm(session, scenario.Tenant, before.TargetRevision.DisclosureEpoch);
            return await BatchHandler(session).ExecuteAsync(ProfileBatch(SettingScope.Tenant), CancellationToken.None);
        }));

        if (commitFailure!.InnerException is not DeliberateRollbackException)
            throw new InvalidOperationException("The batch did not reach its deliberate physical-commit refusal.",
                commitFailure.InnerException);

        // A DI/authentication/source failure cannot pass this test. The probe must
        // independently see both real writes and the completed epoch on the same
        // original transaction before deliberately refusing physical commit.
        await Assert.That(failure.ReachedCanonicalCommitBoundary).IsTrue();
        await Assert.That(before).IsNotNull();
        await Assert.That(profileBefore).IsNotNull();
        await Assert.That(await ReadAsync(scenario)).IsEqualTo(before);
        await Assert.That(await ReadProfileAsync(scenario)).IsEqualTo(profileBefore);
    }

    private static ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto> BatchHandler(
        AuthenticatedSettingsFixture.Session session) => session.Services
        .GetRequiredService<ICommandHandler<UpdateSettingBatchCommand, BatchUpdateResponseDto>>();

    private static UpdateSettingBatchCommand ProfileBatch(SettingScope scope) => new()
    {
        Category = AtprotoFederationSettingDefinitions.Category,
        Scope = scope,
        Mode = BatchUpdateMode.Strict,
        Values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Key] = "true",
            [ProfileKey] = "community_lexicon"
        }
    };

    private async Task SeedUnmovedProfileAsync(Scenario scenario)
    {
        await using var seed = OpenMigrator();
        var definition = SettingRegistry.Get(ProfileKey)!;
        var system = await seed.SystemSettings.SingleOrDefaultAsync(row => row.SettingKey == ProfileKey);
        if (system is null)
        {
            system = new SystemSetting
            {
                Id = Guid.CreateVersion7(),
                SettingKey = ProfileKey,
                Value = InitialProfile,
                ValueType = definition.ValueType,
                IsLocked = false,
                Description = definition.Description,
                Category = definition.Category,
                AllowedValues = definition.AllowedValues is null ? null : JsonSerializer.Serialize(definition.AllowedValues),
                CreatedAt = CreatedAt
            };
            seed.Add(system);
        }
        else
        {
            system.Value = InitialProfile;
            system.IsLocked = false;
        }
        foreach (Guid tenant in new[] { scenario.Tenant, scenario.ForeignTenant })
            seed.Add(new TenantSetting
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant,
                Tenant = null!,
                SettingKey = ProfileKey,
                Value = InitialProfile,
                IsLocked = false,
                CreatedAt = CreatedAt
            });
        await seed.SaveChangesAsync();
    }

    private async Task<ProfileState> ReadProfileAsync(Scenario scenario)
    {
        await using var observer = OpenMigrator();
        var system = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == ProfileKey);
        var tenants = await observer.Set<TenantSetting>().IgnoreTenantFilter(IsolationProof).AsNoTracking()
            .Where(row => row.SettingKey == ProfileKey && (row.TenantId == scenario.Tenant || row.TenantId == scenario.ForeignTenant))
            .ToListAsync();
        return new(new(system.Id, system.Value, system.SettingValueTypeId, system.IsLocked,
                system.Description, system.Category, system.AllowedValues,
                system.CreatedAt, system.CreatedBy, system.UpdatedAt, system.UpdatedBy),
            Profile(tenants.Single(row => row.TenantId == scenario.Tenant)),
            Profile(tenants.Single(row => row.TenantId == scenario.ForeignTenant)));
    }

    private static GenericTenantState Profile(TenantSetting row) => new(row.Id, row.TenantId, row.SettingKey,
        row.Value, row.IsLocked, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    private async Task<RejectedSmtpState> ReadRejectedSmtpAsync(Scenario scenario)
    {
        await using var observer = OpenMigrator();
        var system = await observer.SystemSettings.AsNoTracking()
            .SingleOrDefaultAsync(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost);
        var tenants = await observer.Set<TenantSetting>().IgnoreTenantFilter(IsolationProof).AsNoTracking()
            .Where(row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost
                && (row.TenantId == scenario.Tenant || row.TenantId == scenario.ForeignTenant)).ToListAsync();
        var target = tenants.SingleOrDefault(row => row.TenantId == scenario.Tenant);
        var foreign = tenants.SingleOrDefault(row => row.TenantId == scenario.ForeignTenant);
        return new(system is null ? null : new(system.Id, system.SettingKey, system.Value,
                system.SettingValueTypeId, system.IsLocked, system.Description, system.Category,
                system.AllowedValues, system.DisplayOrder, system.CreatedAt, system.CreatedBy,
                system.UpdatedAt, system.UpdatedBy),
            target is null ? null : Profile(target), foreign is null ? null : Profile(foreign));
    }

    private static async Task AssertBatchDedicatedAsync(Scenario scenario, DatabaseState before, DatabaseState after, SettingScope scope)
    {
        var original = scope == SettingScope.Instance ? before.Instance : before.Target!;
        var changed = scope == SettingScope.Instance ? after.Instance : after.Target!;
        await Assert.That(changed.UpdatedAt).IsNotNull();
        await Assert.That(changed).IsEqualTo(original with
        {
            Value = "true",
            UpdatedAt = changed.UpdatedAt,
            UpdatedBy = scenario.Authentication.ActorId
        });
        await Assert.That(after.InstanceBackfill).IsEqualTo(before.InstanceBackfill);
        await Assert.That(after.TargetBackfill).IsEqualTo(before.TargetBackfill);
        await Assert.That(after.Foreign).IsEqualTo(before.Foreign);
        if (scope == SettingScope.Instance)
            await Assert.That(after.Target).IsEqualTo(before.Target);
        else
            await Assert.That(after.Instance).IsEqualTo(before.Instance);
    }

    private sealed class BatchCommitFailure : DbTransactionInterceptor
    {
        private ExploreDbContext? originalContext;
        private DbConnection? originalConnection;
        private IInstanceTenantSettingsReader? ordinaryReader;
        private Guid tenant;
        private Guid actor;
        private long beforeEpoch;
        internal bool ReachedCanonicalCommitBoundary { get; private set; }

        internal void Arm(AuthenticatedSettingsFixture.Session session, Guid tenantId, long disclosureEpoch)
        {
            originalContext = session.Context;
            originalConnection = session.Context.Database.GetDbConnection();
            ordinaryReader = session.Services.GetRequiredService<IInstanceTenantSettingsReader>();
            tenant = tenantId;
            actor = session.ActorId;
            beforeEpoch = disclosureEpoch;
        }

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction, TransactionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (eventData.Context is not ExploreDbContext context || !ReferenceEquals(context, originalContext))
                return result;
            await Assert.That(context.HumanCommitAttemptOwnsTransaction).IsTrue();
            await Assert.That(context.Database.CurrentTransaction!.GetDbTransaction()).IsSameReferenceAs(transaction);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(originalConnection);
            var dedicated = await context.Set<AtprotoFederationTenantSetting>().AsNoTracking()
                .SingleAsync(row => row.TenantId == tenant && row.SettingKey == Key, cancellationToken);
            var (_, ordinaryRows) = await ordinaryReader!.ReadAsync(tenant, [ProfileKey], cancellationToken);
            var unmoved = ordinaryRows.Single(row => row.TenantId == tenant && row.SettingKey == ProfileKey);
            await Assert.That(context.Database.CurrentTransaction!.GetDbTransaction()).IsSameReferenceAs(transaction);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(originalConnection);
            var revision = await context.Set<EventDiscoveryRevision>().AsNoTracking()
                .SingleAsync(row => row.TenantId == tenant, cancellationToken);
            await Assert.That(dedicated.Value).IsEqualTo("true");
            await Assert.That(dedicated.UpdatedBy).IsEqualTo(actor);
            await Assert.That(unmoved.Value).IsEqualTo(ChangedProfile);
            await Assert.That(unmoved.UpdatedBy).IsEqualTo(actor);
            await Assert.That(revision.DisclosureEpoch).IsEqualTo(checked(beforeEpoch + 1));
            ReachedCanonicalCommitBoundary = true;
            throw new DeliberateRollbackException();
        }
    }

    private sealed record GenericSystemState(Guid Id, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, string? AllowedValues, DateTime CreatedAt, Guid? CreatedBy,
        DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record GenericTenantState(Guid Id, Guid TenantId, string Key, string Value, bool IsLocked,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record ProfileState(GenericSystemState System, GenericTenantState Target, GenericTenantState Foreign);
    private sealed record RejectedSmtpSystemState(Guid Id, string Key, string Value, int ValueType, bool IsLocked,
        string? Description, string? Category, string? AllowedValues, int DisplayOrder,
        DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy);
    private sealed record RejectedSmtpState(RejectedSmtpSystemState? System,
        GenericTenantState? Target, GenericTenantState? Foreign);
}
