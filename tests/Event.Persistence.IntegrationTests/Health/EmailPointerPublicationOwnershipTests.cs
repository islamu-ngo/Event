using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Infrastructure;
using Explore.Infrastructure.Messaging;
using Explore.Domain;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Schema;
using Explore.Persistence.Services;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class EmailPointerPublicationOwnershipTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task EnabledPointerPublisherObservesEmptyQueueWithoutClaimingSmtpWork()
    {
        await fixture.ResetAsync();
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
        options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
        await using var runtime = new ExploreDbContext(options.Options);
        runtime.BindTenantLease();
        var settings = Substitute.For<IOptionsMonitor<EmailDispatchRabbitMqSettings>>();
        settings.CurrentValue.Returns(new EmailDispatchRabbitMqSettings { Enabled = true });
        var publisher = new EmailDispatchRabbitMqPointerPublisher(
            CreateStore(),
            Substitute.For<IEmailDispatchTransport>(),
            settings,
            NullLogger<EmailDispatchRabbitMqPointerPublisher>.Instance);

        var result = await publisher.PublishDuePointersAsync(CancellationToken.None);

        await Assert.That(result.EligibleCount).IsEqualTo(0);
        await Assert.That(result.ConfirmedCount).IsEqualTo(0);
        await Assert.That(result.FailedCount).IsEqualTo(0);
        await Assert.That(result.SkippedCount).IsEqualTo(0);
        await Assert.That(runtime.TenantFilterTenantId).IsNull();
        await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
    }

    [Test]
    public async Task GlobalPublicationCannotReadPayloadOrChangeSmtpAndPauseState()
    {
        await fixture.ResetAsync();
        await using var context = await CreateFactory().CreateDbContextAsync();
        string outbox = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchemaQualifiedTableName()!;
        string controls = context.Model.FindEntityType(typeof(EmailDispatchTenantControl))!.GetSchemaQualifiedTableName()!;
        string processors = context.Model.FindEntityType(typeof(EmailDispatchProcessorState))!.GetSchemaQualifiedTableName()!;
        foreach (string sql in new[]
        {
            $"SELECT * FROM {outbox}",
            $"UPDATE {outbox} SET status=status WHERE FALSE",
            $"UPDATE {outbox} SET next_attempt_at=next_attempt_at WHERE FALSE",
            $"UPDATE {outbox} SET content_redacted_at=content_redacted_at WHERE FALSE",
            $"UPDATE {controls} SET is_paused=is_paused WHERE FALSE",
            $"UPDATE {processors} SET is_paused=is_paused WHERE FALSE"
        })
        {
            var denial = await Assert.ThrowsAsync<PostgresException>(
                async () => await context.Database.ExecuteSqlRawAsync(sql));
            await Assert.That(denial?.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
    }

    [Test]
    public async Task SmtpColumnGrantDriftRejectsPublicationAndComposedRetention()
    {
        await fixture.ResetAsync();
        await using var context = await CreateFactory().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await PostgresEmailPointerPublicationPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresEmailRetentionReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Migrator));
        await using var migration = new ExploreDbContext(options.Options);
        string outbox = context.Model.FindEntityType(typeof(EmailDispatchOutbox))!.GetSchemaQualifiedTableName()!;
        string global = "\"" + fixture.NativeQueueEnrollment.GlobalLogin.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await migration.Database.ExecuteSqlRawAsync($"GRANT UPDATE (status) ON {outbox} TO {global}");
        try
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresEmailPointerPublicationPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
                await PostgresEmailRetentionReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
        }
        finally
        {
            await migration.Database.ExecuteSqlRawAsync($"REVOKE UPDATE (status) ON {outbox} FROM {global}");
        }
        await PostgresEmailPointerPublicationPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
        await PostgresEmailRetentionReadPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment);
    }

    [Test]
    public async Task MissingEnrollmentCannotMasqueradeAsAnEmptyQueue()
    {
        await fixture.ResetAsync();
        var store = new EmailDispatchPointerPublicationStore(CreateFactory());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            store.GetPublishBatchAsync(10, DateTime.UtcNow, DateTime.UtcNow.AddSeconds(-30), CancellationToken.None));
    }

    [Test]
    public async Task PublicationRejectsTenantBoundGlobalSession()
    {
        await fixture.ResetAsync();
        await using var context = await CreateFactory().CreateDbContextAsync();
        await context.Database.OpenConnectionAsync();
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_catalog.set_config('app.current_tenant_id',@tenant,false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = Guid.CreateVersion7().ToString("D");
        command.Parameters.Add(parameter);
        await command.ExecuteScalarAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(async () =>
            await PostgresEmailPointerPublicationPermissions.RequireReadyAsync(context, fixture.NativeQueueEnrollment));
    }

    private GlobalExploreDbContextFactory CreateFactory() =>
        new(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global));

    private EmailDispatchPointerPublicationStore CreateStore() =>
        new(CreateFactory(), fixture.NativeQueueEnrollment);
}
