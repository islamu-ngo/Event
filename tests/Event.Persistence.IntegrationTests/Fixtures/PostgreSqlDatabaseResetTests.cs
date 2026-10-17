using System.Data;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Fixtures;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class PostgreSqlDatabaseResetTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ResetDeletesPopulatedRestrictCyclesAndPreservesLookupAndNativeMetadata()
    {
        await fixture.ResetAsync();
        var seed = await SeedAsync();
        await using (var context = fixture.CreateDbContext())
        {
            var report = EventReport.Create(seed.TenantId, seed.EventId, seed.UserId, seed.ActorId,
                EventReporterKind.AuthenticatedUser, EventReportSourceKind.UserReport, "spam", "misleading",
                EventReportPriority.Normal, EventReportSeverityHint.Medium, false, true, "en", null, null);
            var reportCase = EventReportCase.Create(seed.TenantId, report.Id, "default", EventReportPriority.Normal, null);
            var decision = EventReportDecision.Create(seed.TenantId, reportCase.Id, report.Id,
                EventReportDecisionSource.LocalModerator, EventReportDecisionKind.NoViolation,
                "not_violation", "Reset fixture coverage.", seed.UserId, null);
            var definition = new CustomPropertyDefinition
            {
                Id = Guid.CreateVersion7(),
                TenantId = seed.TenantId,
                Tenant = null!,
                EntityTypeName = EntityTypeName.Organization,
                Namespace = "event",
                Key = "format",
                DisplayName = "Format",
                PropertyType = PropertyType.Option,
                IsActive = true,
                ExposureLevel = ExposureLevel.Internal,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.AddRange(report, reportCase, decision, definition);
            await context.SaveChangesAsync();
            var option = new CustomPropertyOption
            {
                Id = Guid.CreateVersion7(),
                CustomPropertyDefinitionId = definition.Id,
                Namespace = "event",
                Key = "default",
                DisplayName = "Default",
                Value = "default",
                IsDefault = true,
                IsActive = true,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            context.Add(option);
            await context.SaveChangesAsync();
            definition.DefaultOptionId = option.Id;
            reportCase.SelectDecision(decision, DateTime.UtcNow);
            await context.SaveChangesAsync();
            await Assert.That(await context.EventReportCases.AsNoTracking()
                .Where(row => row.Id == reportCase.Id).Select(row => row.CurrentDecisionId).SingleAsync())
                .IsEqualTo(decision.Id);
            await Assert.That(await context.CustomPropertyDefinitions.AsNoTracking()
                .Where(row => row.Id == definition.Id).Select(row => row.DefaultOptionId).SingleAsync())
                .IsEqualTo(option.Id);
        }

        await using var verify = fixture.CreateDbContext();
        int lookupCount = await verify.TenantStatuses.CountAsync();
        await Assert.That(lookupCount).IsGreaterThan(0);
        string epoch = Epoch(verify);
        var before = await ReadWriterAsync(verify, epoch, seed.ConnectionId);
        await fixture.ResetAsync();

        await Assert.That(await verify.EventReportCases.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.EventReportDecisions.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.CustomPropertyDefinitions.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.CustomPropertyOptions.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.RegistrationProviderConnections.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.Tenants.IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        await Assert.That(await verify.TenantStatuses.CountAsync()).IsEqualTo(lookupCount);
        var after = await ReadWriterAsync(verify, epoch, seed.ConnectionId);
        await Assert.That(after.Writer).IsNotEqualTo(before.Writer);
        await Assert.That(after.Deleted).IsTrue();

        // Reinstallation validates the retained native metadata, roles, policies and trigger bodies.
        await PostgresHumanEventProviderSourceContract.ApplyAsync(verify, fixture.NativeQueueEnrollment);
        await PostgresEventResourceProviderContract.ApplyAsync(verify, fixture.NativeQueueEnrollment);
        await fixture.ResetAsync();
        await Assert.That(await verify.TenantStatuses.CountAsync()).IsEqualTo(lookupCount);
        await Assert.That(await ReadWriterAsync(verify, epoch, seed.ConnectionId)).IsEqualTo(after);
    }

    [Test]
    public async Task ResetInvalidatesAnExistingSerializableSourceObservation()
    {
        await fixture.ResetAsync();
        var seed = await SeedAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.Instance);
        await using var reader = new ExploreDbContext(options.Options)
        {
            TenantContext = new TenantIdentity(seed.TenantId)
        };
        await using var owner = await reader.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        using var observed = JsonDocument.Parse(await ReadSourceAsync(reader, seed, acquire: false));
        await Assert.That(observed.RootElement.GetProperty("1").GetArrayLength()).IsEqualTo(1);

        await fixture.ResetAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ReadSourceAsync(reader, seed, acquire: true));
        await Assert.That(failure!.SqlState).IsEqualTo("40001");
        await owner.RollbackAsync();
    }

    private async Task<Seed> SeedAsync()
    {
        await using var context = fixture.CreateDbContext();
        DateTime now = DateTime.UtcNow;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = "Native reset",
            Slug = $"reset-{Guid.CreateVersion7():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            CreatedAt = now,
            Pii = new UserPii { Email = string.Empty, FirstName = "Reset", LastName = "Owner" }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Reset owner" },
            CreatedAt = now
        };
        var parent = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            Title = "Native reset",
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
        var connection = RegistrationProviderConnection.Create(tenant.Id, "Reset connection",
            RegistrationProviderKindEnum.ExternalForm, RegistrationProviderDeploymentKindEnum.HostedSaas,
            "reset", "hosted", "1", "1", "reset", "https://provider.example", "https://provider.example",
            "reset", null, null, now);
        var form = RegistrationForm.Create(tenant.Id, parent.Id, "reset", "form", "Form", now);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        form.AddVersion(version);
        var binding = RegistrationProviderBinding.Create(tenant.Id, connection.Id, form.Id, version.Id,
            RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.ProviderHosted,
            RegistrationProviderCompletionModeEnum.Manual, RegistrationProviderTrustLevelEnum.FullSync, null, now);
        binding.SetDraftProvisionedSurvey("reset", "1");
        context.AddRange(tenant, user, actor, parent, connection, form, binding);
        await context.SaveChangesAsync();
        return new(tenant.Id, user.Id, actor.Id, parent.Id, connection.Id, binding.Id);
    }

    private static async Task<string> ReadSourceAsync(ExploreDbContext context, Seed seed, bool acquire)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = $"SELECT {(acquire ? PostgresHumanEventProviderSourceContract.AcquireFunctionSql(context)
            : PostgresHumanEventProviderSourceContract.ObserveFunctionSql(context))}"
            + "(@actor,@tenant,@event,1,@binding,NULL,NULL,NULL,NULL,NULL,NULL)";
        command.Parameters.Add(new NpgsqlParameter("actor", seed.UserId));
        command.Parameters.Add(new NpgsqlParameter("tenant", seed.TenantId));
        command.Parameters.Add(new NpgsqlParameter("event", seed.EventId));
        command.Parameters.Add(new NpgsqlParameter("binding", seed.BindingId));
        return (string)(await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing source facts."));
    }

    private static async Task<(string Writer, bool Deleted)> ReadWriterAsync(
        ExploreDbContext context, string epoch, Guid connectionId)
    {
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"SELECT writer_tx::text, before_hash IS NOT NULL AND after_hash IS NULL FROM {epoch}"
            + " WHERE kind LIKE 'row:%' AND source_id = @id";
        command.Parameters.Add(new NpgsqlParameter("id", connectionId));
        await using var rows = await command.ExecuteReaderAsync();
        if (!await rows.ReadAsync())
            throw new InvalidOperationException("The real source writer did not retain its row epoch.");
        return (rows.GetString(0), rows.GetBoolean(1));
    }

    private static string Epoch(ExploreDbContext context) => context.GetService<ISqlGenerationHelper>()
        .DelimitIdentifier("human_event_provider_source_epochs", context.Model.FindEntityType(typeof(User))!.GetSchema());

    private sealed record Seed(Guid TenantId, Guid UserId, Guid ActorId, Guid EventId, Guid ConnectionId, Guid BindingId);
    private sealed record TenantIdentity(Guid TenantId) : ITenantContext;
}
