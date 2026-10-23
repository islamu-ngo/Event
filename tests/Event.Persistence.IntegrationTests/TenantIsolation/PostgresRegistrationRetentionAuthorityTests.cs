using Explore.Persistence.Database.ProviderPrimitives;
using System.Data.Common;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.TenantIsolation;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel]
public sealed class PostgresRegistrationRetentionAuthorityTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ActiveDirectoryPagesUseFixedEfReadsWithoutRegistrationDiscoveryRoutine()
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var strategyContext = new ExploreDbContext(options.Options);
        strategyContext.BindTenantLease();
        Guid[] ids = await strategyContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var seed = new ExploreDbContext(options.Options);
            seed.BindTenantLease();
            await using var gate = new EventDiscoveryEnrollmentGate(seed);
            await using var custody = await gate.AcquireAsync();
            Guid[] selected = [Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7()];
            seed.Tenants.AddRange(selected.Select((id, index) => new Tenant
            {
                Id = id,
                FullName = "Retention directory",
                Slug = $"retention-directory-{id:N}",
                TenantStatusId = (int)(index == 2 ? TenantStatusEnum.Suspended : TenantStatusEnum.Active),
                TenantStatus = null!
            }));
            await seed.SaveChangesAsync();
            return selected;
        });

        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        await using (var global = await factory.CreateDbContextAsync())
        {
            global.RequireBoundTenantLease();
            await Assert.That(global.TenantFilterTenantId).IsNull();
            await global.Database.OpenConnectionAsync();
            await using var command = global.Database.GetDbConnection().CreateCommand();
            var tenant = global.Model.FindEntityType(typeof(Tenant))!;
            command.CommandText = """
                SELECT pg_catalog.to_regprocedure(@routine) IS NULL
                    AND NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname IN
                        ('registration_retention_discovery_owner',
                         'registration_retention_discovery_migrator',
                         'registration_retention_discovery_executor'))
                """;
            command.Parameters.Add(new NpgsqlParameter("routine",
                $"{Quote(tenant.GetSchema() ?? "public")}.{Quote(tenant.GetTableName() + "_registration_retention_ids")}(uuid,integer)"));
            await Assert.That(await command.ExecuteScalarAsync()).IsEqualTo(true);
            var eligible = await global.Tenants.IgnoreAutoIncludes().AsNoTracking()
                .Where(row => ids.Contains(row.Id) && row.TenantStatus.IsActiveState)
                .Select(row => row.Id).ToArrayAsync();
            await Assert.That(eligible).IsEquivalentTo(ids.Take(2));
            var denied = await Assert.ThrowsAsync<PostgresException>(() => global.RegistrationSensitiveAnswerValues
                .IgnoreQueryFilters().Select(row => row.Ciphertext).ToArrayAsync());
            await Assert.That(denied.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }

        var discovery = new RegistrationRetentionTenantDiscoveryRepository(factory, fixture.NativeQueueEnrollment);
        var observed = new HashSet<Guid>();
        Guid? cursor = null;
        while (true)
        {
            var page = await discovery.GetTenantIdsAsync(cursor, 1, CancellationToken.None);
            await Assert.That(page.Count).IsLessThanOrEqualTo(1);
            if (page.Count == 0)
                break;
            await Assert.That(observed.Add(page[0])).IsTrue();
            if (cursor is { } previous)
                await Assert.That(page[0].CompareTo(previous)).IsGreaterThan(0);
            cursor = page[0];
        }
        await Assert.That(observed.Where(ids.Contains)).IsEquivalentTo(ids.Take(2));
    }

    [Test]
    public async Task FleetIdentityDiscoveryRequiresIndependentTenantLeasesForDeletion()
    {
        await using var setup = fixture.CreateDbContext();
        // The parent-owned fixture must install the production contract. Do not
        // repair a missing installer here: an absent capability must fail this test.
        var first = await SeedAsync(setup, TenantStatusEnum.Active);
        var second = await SeedAsync(setup, TenantStatusEnum.Active);
        var suspended = await SeedAsync(setup, TenantStatusEnum.Suspended);
        await GrantRuntimeCleanupAsync(setup);

        var globalFactory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var runtimeFactory = new TenantExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime), poolSize: 1);
        var discovery = new RegistrationRetentionTenantDiscoveryRepository(globalFactory, fixture.NativeQueueEnrollment);
        var execution = new RegistrationRetentionExecutionScopeFactory(runtimeFactory);
        var discovered = new HashSet<Guid>();
        Guid? cursor = null;
        do
        {
            var page = await discovery.GetTenantIdsAsync(cursor, 1, CancellationToken.None);
            await Assert.That(page.Count).IsLessThanOrEqualTo(1);
            if (page.Count == 0)
                break;
            await Assert.That(discovered.Add(page[0])).IsTrue();
            cursor = page[0];
        } while (true);
        await Assert.That(discovered.Contains(first.TenantId)).IsTrue();
        await Assert.That(discovered.Contains(second.TenantId)).IsTrue();
        await Assert.That(discovered.Contains(suspended.TenantId)).IsFalse();

        await using (var global = await globalFactory.CreateDbContextAsync())
        {
            await Assert.That(global.BindTenantLease()).IsNull();
            var deniedRead = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.RegistrationSensitiveAnswerValues.IgnoreQueryFilters()
                    .Select(row => row.Ciphertext).ToArrayAsync());
            await Assert.That(deniedRead?.SqlState).IsEqualTo("42501");
            var deniedDelete = await Assert.ThrowsAsync<PostgresException>(async () =>
                await global.RegistrationAnswers.IgnoreQueryFilters()
                    .Where(row => row.Id == first.ExpiredAnswerId).ExecuteDeleteAsync());
            await Assert.That(deniedDelete?.SqlState).IsEqualTo("42501");
            await Assert.That(await global.Database.SqlQueryRaw<bool>(
                "SELECT NOT EXISTS (SELECT 1 FROM pg_catalog.pg_roles WHERE rolname='registration_retention_discovery_owner') AS \"Value\"")
                .SingleAsync()).IsTrue();
        }

        // The original defect: explicit predicates do not select native authority.
        var unboundOptions = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(
            unboundOptions, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        unboundOptions.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        await using (var unbound = new ExploreDbContext(unboundOptions.Options))
        {
            var result = await new RegistrationRetentionCleanupRepository(unbound, new EfCoreUnitOfWork(unbound))
                .CleanupTenantAsync(first.TenantId, Now, 500, CancellationToken.None);
            await Assert.That(result.TotalDeleted).IsEqualTo(0);
        }

        await using (var firstScope = await execution.CreateAsync(first.TenantId, CancellationToken.None))
        {
            var result = await firstScope.CleanupAsync(Now, 500, CancellationToken.None);
            await Assert.That(result.AnswersDeleted).IsEqualTo(1);
            await Assert.That(result.SensitiveValuesDeleted).IsEqualTo(1);
            await Assert.That(result.OrderPiiDeleted).IsEqualTo(1);
            await Assert.That(result.ParticipantPiiDeleted).IsEqualTo(1);
        }
        await using (var secondView = await runtimeFactory.CreateDbContextAsync(second.TenantId))
        {
            await Assert.That(await secondView.RegistrationAnswers
                .AnyAsync(row => row.Id == second.ExpiredAnswerId)).IsTrue();
            await Assert.That(await secondView.RegistrationAnswers.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == first.TenantId)).IsFalse();
        }
        // Pool size one deliberately exercises release and rebinding between owners.
        await using (var secondScope = await execution.CreateAsync(second.TenantId, CancellationToken.None))
            await Assert.That((await secondScope.CleanupAsync(Now, 500, CancellationToken.None)).TotalDeleted).IsEqualTo(4);

        foreach (var graph in new[] { first, second })
        {
            await using var verify = await runtimeFactory.CreateDbContextAsync(graph.TenantId);
            await Assert.That(await verify.RegistrationAnswers.IgnoreQueryFilters()
                .AnyAsync(row => row.Id == graph.ExpiredAnswerId)).IsFalse();
            await Assert.That(await verify.RegistrationSensitiveAnswerValues.IgnoreQueryFilters()
                .AnyAsync(row => row.Id == graph.ExpiredSensitiveId)).IsFalse();
            await Assert.That(await verify.RegistrationAnswers
                .AnyAsync(row => row.Id == graph.LiveAnswerId)).IsTrue();
            await Assert.That(await verify.RegistrationAnswers
                .AnyAsync(row => row.Id == graph.HeldAnswerId)).IsTrue();
        }
        await using var suspendedView = await runtimeFactory.CreateDbContextAsync(suspended.TenantId);
        await Assert.That(await suspendedView.RegistrationAnswers
            .AnyAsync(row => row.Id == suspended.ExpiredAnswerId)).IsTrue();
    }

    [Test]
    [Arguments("global_table")]
    [Arguments("grant_option")]
    [Arguments("public")]
    [Arguments("global_private")]
    [Arguments("directory_rls")]
    [Arguments("membership")]
    public async Task DirectoryReadDriftFailsWithoutRepair(string drift)
    {
        await using var migration = fixture.CreateDbContext();
        string tenants = Table<Tenant>(migration);
        string answers = Table<RegistrationAnswer>(migration);
        string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string global = Quote(fixture.NativeQueueEnrollment.GlobalLogin);
        if (!PostgresUserTenantMembershipReadPermissions.TryGetGlobalReadColumns(
                migration, typeof(Tenant), out var tenantReadColumns))
            throw new InvalidOperationException("The fixed Tenant read manifest is required for fault restoration.");
        string restoreTenantReads = string.Join(", ", tenantReadColumns.Select(Quote));
        (string Inject, string Restore) fault = drift switch
        {
            // Table-level REVOKE also removes the original column SELECT grants.
            "global_table" => ($"GRANT SELECT ON {tenants} TO {global}",
                $"REVOKE SELECT ON {tenants} FROM {global}; GRANT SELECT ({restoreTenantReads}) ON {tenants} TO {global}"),
            "grant_option" => ($"GRANT SELECT (id) ON {tenants} TO {global} WITH GRANT OPTION",
                $"REVOKE GRANT OPTION FOR SELECT (id) ON {tenants} FROM {global}"),
            "public" => ($"GRANT SELECT (id) ON {tenants} TO PUBLIC",
                $"REVOKE SELECT (id) ON {tenants} FROM PUBLIC"),
            "global_private" => ($"GRANT SELECT ON {answers} TO {global}",
                $"REVOKE SELECT ON {answers} FROM {global}"),
            "directory_rls" => ($"ALTER TABLE {tenants} ENABLE ROW LEVEL SECURITY",
                $"ALTER TABLE {tenants} DISABLE ROW LEVEL SECURITY"),
            "membership" => ($"GRANT {global} TO {runtime} WITH INHERIT TRUE, SET FALSE, ADMIN FALSE",
                $"REVOKE {global} FROM {runtime}"),
            _ => throw new ArgumentOutOfRangeException(nameof(drift))
        };
        var globalFactory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var discovery = new RegistrationRetentionTenantDiscoveryRepository(globalFactory, fixture.NativeQueueEnrollment);
        await discovery.GetTenantIdsAsync(null, 1, CancellationToken.None);
        await migration.Database.ExecuteSqlRawAsync(fault.Inject);
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                discovery.GetTenantIdsAsync(null, 1, CancellationToken.None));
            // Read admission must leave the fault intact, not normalize it for another attempt.
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                discovery.GetTenantIdsAsync(null, 1, CancellationToken.None));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync(fault.Restore);
        }
        await discovery.GetTenantIdsAsync(null, 1, CancellationToken.None);
    }

    [Test]
    public async Task DirectoryDiscoveryRejectsInvalidPagesAndTenantBoundGlobalOwners()
    {
        var factory = new GlobalExploreDbContextFactory(
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), poolSize: 1);
        var discovery = new RegistrationRetentionTenantDiscoveryRepository(factory, fixture.NativeQueueEnrollment);
        foreach (int limit in new[] { 0, IRegistrationRetentionTenantDiscoveryRepository.PageSize + 1 })
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                discovery.GetTenantIdsAsync(null, limit, CancellationToken.None));
        var selected = new RegistrationRetentionTenantDiscoveryRepository(
            new TenantSelectedGlobalFactory(factory, Guid.CreateVersion7()), fixture.NativeQueueEnrollment);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => selected.GetTenantIdsAsync(null, 1, CancellationToken.None));

        // The native session must independently agree with the original null-bound EF owner.
        await using var global = await factory.CreateDbContextAsync();
        await global.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await global.Database.OpenConnectionAsync();
            try
            {
                await using var transaction = await global.Database.BeginTransactionAsync();
                await global.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_catalog.set_config('app.current_tenant_id', {Guid.CreateVersion7().ToString()}, true)");
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
                    PostgresRegistrationRetentionReadPermissions.RequireReadyAsync(global, fixture.NativeQueueEnrollment, default));
                await transaction.RollbackAsync();
            }
            finally { await global.Database.CloseConnectionAsync(); }
        });
    }

    [Test]
    public async Task RuntimeTransactionBoundaryPreservesAnAlreadyClaimedProviderHandoff()
    {
        await using var setup = fixture.CreateDbContext();
        var graph = await SeedAsync(setup, TenantStatusEnum.Active, withProviderClaim: true);
        await GrantRuntimeCleanupAsync(setup);
        var barrier = new RetentionTransactionBoundary();
        var options = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        var scopes = new RegistrationRetentionExecutionScopeFactory(new ObservedRuntimeFactory(options, barrier));
        await using var scope = await scopes.CreateAsync(graph.TenantId, CancellationToken.None);
        Task<RegistrationRetentionCleanupResult> cleanup = scope.CleanupAsync(Now, 500, CancellationToken.None);
        try
        {
            Task reached = barrier.Reached.Task;
            if (await Task.WhenAny(reached, cleanup).WaitAsync(TimeSpan.FromSeconds(10)) == cleanup)
                await cleanup;
            var authority = await barrier.Reached.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(authority.TenantId).IsEqualTo(graph.TenantId);
            await Assert.That(authority.Login).IsEqualTo(fixture.NativeQueueEnrollment.RuntimeLogin);
            await using var view = await new TenantExploreDbContextFactory(options, poolSize: 1)
                .CreateDbContextAsync(graph.TenantId);
            await Assert.That(await view.RegistrationAnswers.AnyAsync(row => row.Id == graph.ExpiredAnswerId)).IsTrue();
            var claim = await view.RegistrationProviderSubmissionWriteEffects.SingleAsync(row => row.Id == graph.EffectId);
            await Assert.That(claim.Status).IsEqualTo(OutboxMessageStatus.Processing);
            await Assert.That(claim.ProcessingLeaseToken).IsEqualTo(graph.LeaseToken);
        }
        finally
        {
            barrier.Release.TrySetResult();
            await cleanup.WaitAsync(TimeSpan.FromSeconds(10));
        }
        await Assert.That((await cleanup).AnswersDeleted).IsEqualTo(1);
        await using var verification = await new TenantExploreDbContextFactory(options, poolSize: 1)
            .CreateDbContextAsync(graph.TenantId);
        var retained = await verification.RegistrationProviderSubmissionWriteEffects.SingleAsync(row => row.Id == graph.EffectId);
        await Assert.That(retained.Status).IsEqualTo(OutboxMessageStatus.Processing);
        await Assert.That(retained.ProcessingLeaseToken).IsEqualTo(graph.LeaseToken);
        await Assert.That(retained.ProcessingFence).IsEqualTo(1L);
        await Assert.That(retained.AttemptCount).IsEqualTo(1);
    }

    private async Task GrantRuntimeCleanupAsync(ExploreDbContext context)
    {
        string runtime = Quote(fixture.NativeQueueEnrollment.RuntimeLogin);
        string schema = context.Model.FindEntityType(typeof(Tenant))!.GetSchema()!;
        Type[] reads =
        [
            typeof(RegistrationAnswer), typeof(RegistrationSensitiveAnswerValue),
            typeof(RegistrationOrderPii), typeof(RegistrationParticipantPii),
            typeof(RegistrationProviderSubmissionWriteEffect), typeof(RegistrationOrder),
            typeof(RegistrationFormField), typeof(RegistrationProviderFieldMapping), typeof(RegistrationProviderBinding),
            typeof(StorageObject), typeof(RegistrationSubmission), typeof(RegistrationFormVersion),
            typeof(RegistrationRetentionPolicy)
        ];
        await context.Database.ExecuteSqlRawAsync($"""
            GRANT USAGE ON SCHEMA {Quote(schema)} TO {runtime};
            GRANT SELECT ON {string.Join(", ", reads.Select(type => Table(context, type)))} TO {runtime};
            GRANT DELETE ON {Table<RegistrationAnswer>(context)}, {Table<RegistrationSensitiveAnswerValue>(context)},
                {Table<RegistrationOrderPii>(context)}, {Table<RegistrationParticipantPii>(context)} TO {runtime};
            """);
    }

    private static async Task<Graph> SeedAsync(
        ExploreDbContext context, TenantStatusEnum status, bool withProviderClaim = false)
    {
        context.BindTenantLease();
        DateTime created = Now.AddDays(-10);
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Retention authority",
            Slug = "retention-" + Guid.CreateVersion7().ToString("N"),
            TenantStatusId = (int)status,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii { Email = $"retention-{Guid.CreateVersion7():N}@example.test", FirstName = "Retention", LastName = "Owner" }
        };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = 1,
            ActorType = null!,
            UserId = user.Id,
            Pii = new ActorPii { DisplayName = "Retention owner" }
        };
        context.Add(actor);
        await context.SaveChangesAsync();
        var target = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = null!,
            ActorId = actor.Id,
            Actor = null!,
            Title = "Retention",
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Add(target);
        await context.SaveChangesAsync();
        var catalog = EventTicketCatalogVersion.Create(tenant.Id, target.Id, "EUR", 1);
        var workflow = RegistrationWorkflow.Create(tenant.Id, target.Id, "RETENTION", created);
        var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_SYNC,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, created);
        workflow.AddRequirement(requirement);
        var form = RegistrationForm.Create(tenant.Id, target.Id, "registration", "retention", "Retention", created);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, created);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Retention", created);
        version.AddSection(section);
        var field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "registration", "private", "Private",
            RegistrationFieldTypeEnum.ShortText, (int)RegistrationRetentionPolicyEnum.StandardOperational,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, created);
        var heldField = RegistrationFormField.Create(Guid.CreateVersion7(), section, 2, "registration", "held", "Held",
            RegistrationFieldTypeEnum.ShortText, (int)RegistrationRetentionPolicyEnum.LegalHold,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, created);
        var liveField = RegistrationFormField.Create(Guid.CreateVersion7(), section, 3, "registration", "live", "Live",
            RegistrationFieldTypeEnum.ShortText, (int)RegistrationRetentionPolicyEnum.StandardOperational,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, created);
        version.AddField(section, field);
        version.AddField(section, heldField);
        version.AddField(section, liveField);
        form.AddVersion(version);
        RegistrationProviderBinding? binding = null;
        var revision = RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32]));
        if (withProviderClaim)
        {
            var connection = RegistrationProviderConnection.Create(tenant.Id, "Retention provider",
                RegistrationProviderKindEnum.ExternalApi, RegistrationProviderDeploymentKindEnum.HostedSaas,
                "retention-native-test", "hosted", "1", "1", "1",
                "https://8.8.8.8", "https://8.8.8.8", "retention", null, null, created);
            binding = RegistrationProviderBinding.Create(tenant.Id, connection.Id, form.Id, version.Id,
                RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
                RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, created);
            binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding, "registration.private", "private", true));
            binding.Publish(revision, created);
            context.AddRange(connection, binding);
        }
        var channel = RegistrationChannel.Create(requirement, 1, !withProviderClaim, binding?.Id, created);
        requirement.AddChannel(channel);
        context.AddRange(catalog, workflow, form);
        await context.SaveChangesAsync();
        var order = RegistrationOrder.Create(tenant.Id, target.Id, withProviderClaim ? null : user.Id, null,
            BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.GuestAllowed,
                GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, withProviderClaim ? CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])) : null,
            "EUR", created, Now.AddDays(1), withProviderClaim ? Now : null);
        order.SetPii(RegistrationOrderPii.Create(order.Id, tenant.Id, "Private", "private@example.test", null, null,
            (int)RegistrationRetentionPolicyEnum.StandardOperational, created, Now));
        var participant = RegistrationParticipant.Create(tenant.Id, order.Id, user.Id, ParticipantTypeEnum.Adult, null);
        participant.SetPii(RegistrationParticipantPii.Create(participant.Id, tenant.Id, "Private", "private@example.test", null,
            (int)RegistrationRetentionPolicyEnum.StandardOperational, created, Now));
        context.AddRange(order, participant);
        await context.SaveChangesAsync();
        var attempt = RegistrationAttempt.Create(tenant.Id, target.Id, order.Id, workflow.Id, requirement.Id, channel.Id,
            form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])),
            binding?.Id, binding is null ? null : revision, created, Now.AddDays(1));
        var submission = withProviderClaim
            ? attempt.SubmitHeadlessProvider(revision, created.AddMinutes(1), null)
            : RegistrationSubmission.Create(attempt, revision, created.AddMinutes(1), null, null, null, null);
        context.AddRange(attempt, submission);
        await context.SaveChangesAsync();
        RegistrationProviderSubmissionWriteEffect? effect = null;
        Guid? leaseToken = null;
        if (withProviderClaim)
        {
            effect = RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, created.AddMinutes(1));
            leaseToken = Guid.CreateVersion7();
            effect.Claim("retention-native-test", leaseToken.Value, Now.AddMinutes(1), Now.AddMinutes(-1));
            context.Add(effect);
        }
        var sensitive = RegistrationSensitiveAnswerValue.Create(tenant.Id, Convert.ToBase64String(new byte[64]), 1,
            (int)RegistrationRetentionPolicyEnum.StandardOperational, created, Now);
        var expired = RegistrationAnswer.CreateSensitive(submission, field, requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, 1, sensitive, created, anonymousUpperBoundUtc: Now);
        var live = RegistrationAnswer.CreateText(submission, liveField, requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, 1, "still live", created,
            anonymousUpperBoundUtc: Now.AddTicks(10));
        var held = RegistrationAnswer.CreateText(submission, heldField, requirement,
            RegistrationAnswerSubjectTypeEnum.RegistrationOrder, order.Id, 1, "held", created);
        context.AddRange(expired, live, held);
        await context.SaveChangesAsync();
        return new(tenant.Id, expired.Id, sensitive.Id, live.Id, held.Id, effect?.Id, leaseToken);
    }

    private static string Table<T>(ExploreDbContext context) => Table(context, typeof(T));

    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private sealed record Graph(
        Guid TenantId, Guid ExpiredAnswerId, Guid ExpiredSensitiveId, Guid LiveAnswerId, Guid HeldAnswerId,
        Guid? EffectId, Guid? LeaseToken);

    private sealed class ObservedRuntimeFactory(
        PrimaryDatabaseConnectionOptions options, RetentionTransactionBoundary barrier) : ITenantExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var builder = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(builder, options);
            builder.AddInterceptors(PostgresTenantSessionInterceptor.Instance, barrier);
            var context = new ExploreDbContext(builder.Options) { TenantContext = new TenantIdentity(tenantId) };
            context.BindTenantLease();
            return Task.FromResult(context);
        }
    }

    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
    private sealed class TenantSelectedGlobalFactory(GlobalExploreDbContextFactory factory, Guid tenantId)
        : IGlobalExploreDbContextFactory
    {
        public Task<ExploreDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            ((IMixedSettingsDbContextFactory)factory).CreateMixedSettingsAsync(tenantId, cancellationToken);
    }
    private sealed record SessionAuthority(Guid TenantId, string Login);

    private sealed class RetentionTransactionBoundary : DbTransactionInterceptor
    {
        public TaskCompletionSource<SessionAuthority> Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = result;
            command.CommandText = "SELECT current_setting('app.current_tenant_id')::uuid, session_user::text";
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                    throw new InvalidOperationException("Retention transaction authority was not observable.");
                Reached.TrySetResult(new(reader.GetGuid(0), reader.GetString(1)));
            }
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return result;
        }
    }
}
