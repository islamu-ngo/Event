using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests;

/// <summary>Physical capability tests use enrolled database callers; human admission is covered by the authenticated API cohort.</summary>
[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class HumanEventProviderNativeSourceFenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("profile")]
    [Arguments("capability-absence")]
    [Arguments("schema-absence")]
    [Arguments("version-predicate")]
    [Arguments("binding-reparent")]
    [Arguments("form-absence")]
    public async Task CommittedContributorChangeAfterSnapshotCannotBeAdmittedAsCurrent(string change)
    {
        var seed = await SeedAsync();
        await using var context = Open(seed.TenantId);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var initial = JsonDocument.Parse(await ReadAsync(context, seed, false));
        await using (var writer = fixture.CreateDbContext())
        {
            switch (change)
            {
                case "profile":
                    await writer.RegistrationProviderConnections.Where(row => row.Id == seed.Connection.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.AdapterPolicyVersion, "2"));
                    break;
                case "capability-absence":
                    writer.Add(RegistrationProviderCapability.Create(seed.Binding, "source", "hosted", "1", "1", "source", "MANUAL"));
                    await writer.SaveChangesAsync();
                    break;
                case "schema-absence":
                    byte[] hash = SHA256.HashData("{\"fields\":[]}"u8);
                    writer.Add(RegistrationProviderSchemaRevision.Create(seed.TenantId, seed.Connection.Id,
                        RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered, RegistrationEvidenceHash.Create(Convert.ToBase64String(hash)),
                        "source", "2", "{\"fields\":[]}", Convert.ToHexStringLower(hash), RegistrationProviderDriftClassEnum.NoDrift, seed.Now));
                    await writer.SaveChangesAsync();
                    break;
                case "version-predicate":
                    writer.Add(RegistrationFormVersion.Create(seed.Form, 2, "en", null, null, seed.Now));
                    await writer.SaveChangesAsync();
                    break;
                case "binding-reparent":
                    await writer.RegistrationProviderBindings.Where(row => row.Id == seed.Binding.Id)
                        .ExecuteUpdateAsync(update => update.SetProperty(row => row.RegistrationFormId, seed.OtherForm.Id)
                            .SetProperty(row => row.RegistrationFormVersionId, seed.OtherVersion.Id));
                    break;
                case "form-absence":
                    writer.Add(RegistrationForm.Create(seed.TenantId, seed.EventId, "source", "new", "New form", seed.Now));
                    await writer.SaveChangesAsync();
                    break;
            }
        }
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seed, true));
        await Assert.That(failure!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    [Test]
    public async Task EventBindingConnectionAndVersionPredicatesHaveIndependentHeldEpochs()
    {
        var seed = await SeedAsync();
        await using var context = Open(seed.TenantId);
        await using var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var initial = JsonDocument.Parse(await ReadAsync(context, seed, true));
        foreach (var key in new[]
        {
            ("event", seed.EventId), ("form", seed.Form.Id), ("binding", seed.Binding.Id),
            ("connection", seed.Connection.Id), ("version", seed.Version.Id)
        })
        {
            await using var contender = fixture.CreateDbContext();
            await using var attempt = await contender.Database.BeginTransactionAsync();
            var failure = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                $"SELECT source_id FROM {Epoch(contender)} WHERE tenant_id = {{0}} AND kind = {{1}} AND source_id = {{2}} FOR UPDATE NOWAIT",
                seed.TenantId, key.Item1, key.Item2));
            await Assert.That(failure!.SqlState).IsEqualTo("55P03");
            await attempt.RollbackAsync();
        }
        await context.RegistrationProviderBindings.Where(row => row.Id == seed.Binding.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderSurveyRevisionId, "own"));
        using var fresh = JsonDocument.Parse(await ReadAsync(context, seed, true, final: true));
        await Assert.That(fresh.RootElement.GetProperty("1")[0].GetProperty("ProviderSurveyRevisionId").GetString()).IsEqualTo("own");
        await Assert.That(initial.RootElement.GetProperty("1")[0].GetProperty("ProviderSurveyRevisionId").GetString()).IsEqualTo("1");
        await owner.RollbackAsync();
    }

    [Test]
    public async Task RowWritersReserveBothSidesOfBindingReparentAndEmptyCapabilityPredicate()
    {
        var seed = await SeedAsync();
        await using var writer = fixture.CreateDbContext();
        await writer.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var write = await writer.Database.BeginTransactionAsync();
            await writer.RegistrationProviderBindings.Where(row => row.Id == seed.Binding.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RegistrationFormId, seed.OtherForm.Id)
                    .SetProperty(row => row.RegistrationFormVersionId, seed.OtherVersion.Id));
            foreach (Guid form in new[] { seed.Form.Id, seed.OtherForm.Id })
            {
                await using var contender = fixture.CreateDbContext();
                await using var attempt = await contender.Database.BeginTransactionAsync();
                var failure = await Assert.ThrowsAsync<PostgresException>(() => contender.Database.ExecuteSqlRawAsync(
                    $"SELECT source_id FROM {Epoch(contender)} WHERE tenant_id = {{0}} AND kind = 'form' AND source_id = {{1}} FOR UPDATE NOWAIT",
                    seed.TenantId, form));
                await Assert.That(failure!.SqlState).IsEqualTo("55P03");
                await attempt.RollbackAsync();
            }
            writer.Add(RegistrationProviderCapability.Create(seed.Binding, "source", "hosted", "1", "1", "source", "MANUAL"));
            await writer.SaveChangesAsync();
            await using var blocker = fixture.CreateDbContext();
            await using var blocked = await blocker.Database.BeginTransactionAsync();
            var excluded = await Assert.ThrowsAsync<PostgresException>(() => blocker.Database.ExecuteSqlRawAsync(
                $"SELECT source_id FROM {Epoch(blocker)} WHERE tenant_id = {{0}} AND kind = 'binding' AND source_id = {{1}} FOR UPDATE NOWAIT",
                seed.TenantId, seed.Binding.Id));
            await Assert.That(excluded!.SqlState).IsEqualTo("55P03");
            await blocked.RollbackAsync();
            await write.RollbackAsync();
        });
    }

    [Test]
    public async Task FinalNativeReadRejectsAnUnobservedConnectionAndCannotBorrowACompletedOwner()
    {
        var seed = await SeedAsync();
        var other = RegistrationProviderConnection.Create(seed.TenantId, "Unobserved connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "source", "hosted", "1", "1", "unobserved", "https://provider.example", "https://provider.example",
            "other", null, null, seed.Now);
        await using (var setup = fixture.CreateDbContext())
        {
            setup.Add(other);
            await setup.SaveChangesAsync();
        }
        await using var context = Open(seed.TenantId);
        await using (var owner = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            using var initial = JsonDocument.Parse(await ReadAsync(context, seed, true));
            await Assert.That(initial.RootElement.GetProperty("2").GetArrayLength()).IsEqualTo(1);
            await context.RegistrationProviderBindings.Where(row => row.Id == seed.Binding.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.RegistrationProviderConnectionId, other.Id));
            using var changedSelection = JsonDocument.Parse(await ReadAsync(context, seed, true, final: true));
            // The original connection is still returned. Merely reading the newly referenced
            // connection at validation does not mark it as an original observed source.
            await Assert.That(changedSelection.RootElement.GetProperty("2").GetArrayLength()).IsEqualTo(2);
            int changed = await context.Database.ExecuteSqlRawAsync(
                $"UPDATE {Table(context, typeof(RegistrationProviderConnection))} SET adapter_policy_version = {{0}} WHERE id = {{1}}",
                "own-unobserved", other.Id);
            await Assert.That(changed).IsEqualTo(1);
            var rejected = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seed, true, final: true));
            await Assert.That(rejected!.SqlState).IsEqualTo("55000");
            await Assert.That(rejected.MessageText).IsEqualTo("human_event_provider_unobserved_write");
            await owner.RollbackAsync();
        }
        await using var replacement = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seed, true, final: true));
        await Assert.That(failure!.SqlState).IsEqualTo("55000");
        await replacement.RollbackAsync();
    }

    [Test]
    [Arguments("writer")]
    [Arguments("profile")]
    [Arguments("body")]
    public async Task ValidRuntimeLoginCannotBlessMissingWriterOrAlteredNativeProfile(string drift)
    {
        var seed = await SeedAsync();
        await using var migration = fixture.CreateDbContext();
        string table = Table(migration, typeof(RegistrationProviderCapability));
        string function = PostgresHumanEventProviderSourceContract.ObserveFunctionSql(migration)
            + "(uuid,uuid,uuid,integer,uuid,uuid,uuid,uuid,uuid,uuid,uuid)";
        string corrupt = drift switch
        {
            "writer" => $"ALTER TABLE {table} DISABLE TRIGGER zz_human_event_provider_source_write",
            "profile" => $"GRANT SELECT ON {table} TO {PostgresHumanEventProviderSourceContract.RuntimeRole}",
            _ => $"SET ROLE {PostgresHumanEventProviderSourceContract.OwnerRole}; ALTER FUNCTION {function} STABLE; RESET ROLE"
        };
        string restore = drift switch
        {
            "writer" => $"ALTER TABLE {table} ENABLE ALWAYS TRIGGER zz_human_event_provider_source_write",
            "profile" => $"REVOKE SELECT ON {table} FROM {PostgresHumanEventProviderSourceContract.RuntimeRole}",
            _ => $"SET ROLE {PostgresHumanEventProviderSourceContract.OwnerRole}; ALTER FUNCTION {function} VOLATILE; RESET ROLE"
        };
        await migration.Database.ExecuteSqlRawAsync(corrupt);
        try
        {
            await using var context = Open(seed.TenantId);
            var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadAsync(context, seed, false));
            await Assert.That(failure!.SqlState).IsEqualTo("55000");
        }
        finally { await migration.Database.ExecuteSqlRawAsync(restore); }
        await using var restored = Open(seed.TenantId);
        using var current = JsonDocument.Parse(await ReadAsync(restored, seed, false));
        await Assert.That(current.RootElement.GetProperty("1").GetArrayLength()).IsEqualTo(1);
    }

    private sealed record Seed(Guid TenantId, Guid UserId, Guid EventId, RegistrationProviderConnection Connection,
        RegistrationForm Form, RegistrationFormVersion Version, RegistrationForm OtherForm, RegistrationFormVersion OtherVersion,
        RegistrationProviderBinding Binding, DateTime Now);

    private async Task<Seed> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        await PostgresHumanEventProviderSourceContract.ApplyAsync(context, fixture.NativeQueueEnrollment);
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Provider source",
            Slug = $"provider-source-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User { Id = Guid.CreateVersion7(), CreatedAt = now, Pii = new UserPii { Email = string.Empty, FirstName = "Source", LastName = "Owner" } };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Source owner" },
            CreatedAt = now
        };
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Title = "Provider source",
            ActorId = actor.Id,
            Actor = actor,
            OrganizerActorId = actor.Id,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!,
            CreatedAt = now
        };
        var connection = RegistrationProviderConnection.Create(tenant.Id, "Source connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "source", "hosted", "1", "1", "source", "https://provider.example", "https://provider.example", "source", null, null, now);
        var form = RegistrationForm.Create(tenant.Id, parent.Id, "source", "form", "Form", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var other = RegistrationForm.Create(tenant.Id, parent.Id, "source", "other", "Other form", now);
        var otherVersion = RegistrationFormVersion.Create(other, 1, "en", null, null, now);
        other.AddVersion(otherVersion);
        var binding = RegistrationProviderBinding.Create(tenant.Id, connection.Id, form.Id, version.Id,
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null, now);
        binding.SetDraftProvisionedSurvey("source", "1");
        context.AddRange(tenant, user, actor, parent, connection, form, other, binding);
        await context.SaveChangesAsync();
        return new(tenant.Id, user.Id, parent.Id, connection, form, version, other, otherVersion, binding, now);
    }

    private static async Task<string> ReadAsync(ExploreDbContext context, Seed seed, bool acquire, bool final = false)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.CommandText = $"SELECT {(final ? PostgresHumanEventProviderSourceContract.ValidateFunctionSql(context)
            : acquire ? PostgresHumanEventProviderSourceContract.AcquireFunctionSql(context)
            : PostgresHumanEventProviderSourceContract.ObserveFunctionSql(context))}"
            + "(@actor,@tenant,@event,1,@binding,NULL,NULL,NULL,NULL,NULL,NULL)";
        command.Parameters.Add(new NpgsqlParameter("actor", seed.UserId));
        command.Parameters.Add(new NpgsqlParameter("tenant", seed.TenantId));
        command.Parameters.Add(new NpgsqlParameter("event", seed.EventId));
        command.Parameters.Add(new NpgsqlParameter("binding", seed.Binding.Id));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing source facts."));
    }

    private ExploreDbContext Open(Guid tenant)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.UseNpgsql(provider => provider.ExecutionStrategy(dependencies => new NonRetryingExecutionStrategy(dependencies)));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        return new ExploreDbContext(options.Options) { TenantContext = new TenantIdentity(tenant) };
    }
    private static string Table(ExploreDbContext context, Type type)
    {
        var entity = context.Model.FindEntityType(type)!;
        return context.GetService<ISqlGenerationHelper>().DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
    }
    private static string Epoch(ExploreDbContext context) => context.GetService<ISqlGenerationHelper>()
        .DelimitIdentifier("human_event_provider_source_epochs", context.Model.FindEntityType(typeof(User))!.GetSchema());
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
