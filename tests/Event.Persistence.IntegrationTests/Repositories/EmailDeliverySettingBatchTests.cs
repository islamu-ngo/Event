// ABOUTME: Verifies SMTP batch atomicity and category boundaries against real relational settings.
// ABOUTME: Uses database-backed administrator authority and observes notifications only after commit.

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using static Event.Persistence.IntegrationTests.Fixtures.EmailDispatchSqliteFixture;

namespace Event.Persistence.IntegrationTests.Repositories;

[NotInParallel("SqliteEmailDispatchEligibility")]
public sealed class EmailDeliverySettingBatchTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task SmtpBatch_CommitsAllFieldsAndNotificationsOrRollsBack(bool rejectPort)
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-batch-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using ExploreDbContext context = CreateContext(databasePath);
            Guid administratorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            using var fixture = new InstanceSettingsCommandFixture(context, administratorId);
            await SetEmailSettingAsync(context, GovernanceSettingKeys.Email.SmtpPort, "587", mutationLock: fixture.MutationLock);
            if (rejectPort)
            {
                var entity = context.Model.FindEntityType(typeof(SystemSetting))!;
                var sql = context.GetService<ISqlGenerationHelper>();
                string table = sql.DelimitIdentifier(entity.GetTableName()!);
                string key = sql.DelimitIdentifier(entity.FindProperty(nameof(SystemSetting.SettingKey))!.GetColumnName());
                await context.Database.ExecuteSqlRawAsync($"""
                    CREATE TRIGGER reject_smtp_port_update BEFORE UPDATE ON {table}
                    WHEN NEW.{key} = 'email.smtp_port'
                    BEGIN SELECT RAISE(ABORT, 'SMTP port write rejected by test database'); END;
                    """);
            }

            var request = new UpdateSettingBatchCommand
            {
                Category = EmailSettingDefinitions.SmtpHost.Category,
                Scope = SettingScope.Instance,
                Mode = BatchUpdateMode.Strict,
                Values = new Dictionary<string, string>
                {
                    [GovernanceSettingKeys.Email.SmtpHost] = "smtp.changed.test",
                    [GovernanceSettingKeys.Email.SmtpPort] = "465"
                }
            };
            if (rejectPort)
                await Assert.That(async () => await CreateHandler(fixture).Handle(request, CancellationToken.None))
                    .Throws<DbUpdateException>();
            else
            {
                var result = await CreateHandler(fixture).Handle(request, CancellationToken.None);
                await Assert.That(result.Success).IsTrue();
                await Assert.That(result.Results.All(result => result.Applied)).IsTrue();
            }

            await using ExploreDbContext verification = CreateContext(databasePath);
            string host = await verification.SystemSettings
                .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost)
                .Select(setting => setting.Value).SingleAsync();
            string port = await verification.SystemSettings
                .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpPort)
                .Select(setting => setting.Value).SingleAsync();
            await Assert.That(host).IsEqualTo(rejectPort ? "\"smtp.fixture.test\"" : "\"smtp.changed.test\"");
            await Assert.That(port).IsEqualTo(rejectPort ? "587" : "465");
            await Assert.That(fixture.Notifications.Published.Count).IsEqualTo(rejectPort ? 0 : 2);
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task StrictSmtpBatch_RejectsForeignCategoryWithoutWritingValidEmailField()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-batch-category-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using ExploreDbContext context = CreateContext(databasePath);
            Guid administratorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            using var fixture = new InstanceSettingsCommandFixture(context, administratorId);
            var result = await CreateHandler(fixture).Handle(new UpdateSettingBatchCommand
            {
                Category = EmailSettingDefinitions.SmtpHost.Category,
                Scope = SettingScope.Instance,
                Mode = BatchUpdateMode.Strict,
                Values = new Dictionary<string, string>
                {
                    [GovernanceSettingKeys.Email.SmtpHost] = "smtp.changed.test",
                    [GovernanceSettingKeys.TenantDelegation.LockSmtp] = "false"
                }
            }, CancellationToken.None);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Results.All(result => !result.Applied)).IsTrue();
            await using ExploreDbContext verification = CreateContext(databasePath);
            string host = await verification.SystemSettings
                .Where(setting => setting.SettingKey == GovernanceSettingKeys.Email.SmtpHost)
                .Select(setting => setting.Value).SingleAsync();
            await Assert.That(host).IsEqualTo("\"smtp.fixture.test\"");
            await Assert.That(fixture.Notifications.Published).IsEmpty();
        }
        finally
        {
            DeleteDatabase(databasePath);
        }
    }

    [Test]
    public async Task BestEffortEventsBatch_AppliesPublicationPolicyAndRejectsForeignSmtpWithoutNestedTransaction()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"email-batch-best-effort-{Guid.CreateVersion7():N}.db");
        try
        {
            await CreateDatabaseAsync(databasePath);
            await using var context = CreateContext(databasePath);
            Guid administratorId = await InstanceSettingsCommandFixture.SeedAdministratorAsync(context);
            using var fixture = new InstanceSettingsCommandFixture(context: context, userId: administratorId);
            long revision = await context.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => row.DeliveryPolicyRevision).SingleAsync();

            var result = await CreateHandler(fixture).Handle(new UpdateSettingBatchCommand
            {
                Category = EventSettingDefinitions.RequireApproval.Category,
                Scope = SettingScope.Instance,
                Mode = BatchUpdateMode.BestEffort,
                Values = new Dictionary<string, string>
                {
                    [EventSettingDefinitions.RequireApproval.Key] = "true",
                    [GovernanceSettingKeys.Email.SmtpHost] = "smtp.foreign-category.test"
                }
            }, CancellationToken.None);

            await Assert.That(result.Results.Single(row => row.Key == EventSettingDefinitions.RequireApproval.Key).Applied).IsTrue();
            var smtpResult = result.Results.Single(row => row.Key == GovernanceSettingKeys.Email.SmtpHost);
            await Assert.That(smtpResult.Applied).IsFalse();
            await Assert.That(smtpResult.SkipReason).IsNotNull();
            await using var observer = CreateContext(databasePath);
            await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == EventSettingDefinitions.RequireApproval.Key)).Value).IsEqualTo("true");
            await Assert.That((await observer.SystemSettings.AsNoTracking().SingleAsync(
                row => row.SettingKey == GovernanceSettingKeys.Email.SmtpHost)).Value).IsEqualTo("\"smtp.fixture.test\"");
            await Assert.That(await observer.EmailDispatchProcessorStates.AsNoTracking()
                .Select(row => row.DeliveryPolicyRevision).SingleAsync()).IsEqualTo(revision);
            await Assert.That(fixture.Notifications.Published.Single().Key).IsEqualTo(EventSettingDefinitions.RequireApproval.Key);
        }
        finally { DeleteDatabase(databasePath); }
    }

    private static void DeleteDatabase(string databasePath)
    {
        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
        File.Delete(databasePath + "-shm");
        File.Delete(databasePath + "-wal");
    }

    private static UpdateSettingBatchCommandHandler CreateHandler(InstanceSettingsCommandFixture fixture) =>
        new(fixture.Settings, new UserPreferenceRepository(fixture.Context), fixture,
            fixture.CurrentUserService, fixture.AdminContext, fixture.Mediator,
            NullLogger<UpdateSettingBatchCommandHandler>.Instance,
            fixture.PublicationPolicyBoundary, fixture.UnitOfWork, fixture.MutationLock, fixture.EmailDeliverySettingsWriter);
}
