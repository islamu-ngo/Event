// ABOUTME: PostgreSQL failure-injection coverage for tenant onboarding's mandatory identity write.
// ABOUTME: Proves policy, branding, onboarding, and identity persistence share one rollback boundary.

using Explore.Domain;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Onboarding;

[NotInParallel("PersistenceDb")]
public sealed class TenantOnboardingAtomicRollbackTests
{
    [Test]
    public async Task MandatoryIdentityWriteFailure_RollsBackPolicyBrandingOnboardingAndIdentity()
    {
        await using var database = new PostgreSqlBuilder("postgres:18-alpine")
            .WithDatabase("atomic_onboarding")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();
        await database.StartAsync();
        DbContextOptions<ExploreDbContext> seedOptions = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .Options;
        await using (var schema = new NpgsqlConnection(database.GetConnectionString()))
        {
            await schema.OpenAsync();
            await using NpgsqlCommand command = schema.CreateCommand();
            command.CommandText = """
                CREATE SCHEMA islamu_event;
                CREATE TABLE islamu_event."TenantSettingOverrides" (
                    "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "SettingKey" varchar(256) NOT NULL,
                    "Value" text NOT NULL, "IsLocked" boolean NOT NULL DEFAULT false, "CreatedAt" timestamptz NOT NULL DEFAULT now(),
                    "CreatedBy" uuid NULL, "UpdatedAt" timestamptz NULL, "UpdatedBy" uuid NULL);
                CREATE UNIQUE INDEX "IX_TenantSettingOverrides_TenantId_SettingKey"
                    ON islamu_event."TenantSettingOverrides" ("TenantId", "SettingKey");
                CREATE TABLE islamu_event."TenantSettingsDocuments" (
                    "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "DocumentKey" varchar(128) NOT NULL,
                    "SchemaVersion" integer NOT NULL, "DefaultsVersion" varchar(64) NOT NULL, "PayloadJson" jsonb NOT NULL,
                    "ConcurrencyStamp" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL DEFAULT now(), "CreatedBy" uuid NULL,
                    "UpdatedAt" timestamptz NULL, "UpdatedBy" uuid NULL);
                CREATE UNIQUE INDEX "IX_TenantSettingsDocuments_TenantId_DocumentKey"
                    ON islamu_event."TenantSettingsDocuments" ("TenantId", "DocumentKey");
                CREATE TABLE islamu_event."TenantOnboardingStates" (
                    "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "IsCompleted" boolean NOT NULL,
                    "CurrentStep" integer NOT NULL, "TotalSteps" integer NOT NULL, "CompletedStepsJson" jsonb NULL,
                    "CreatedAt" timestamptz NOT NULL DEFAULT now(), "CompletedAt" timestamptz NULL, "CompletedByUserId" uuid NULL);
                CREATE UNIQUE INDEX "IX_TenantOnboardingStates_TenantId"
                    ON islamu_event."TenantOnboardingStates" ("TenantId");
                """;
            await command.ExecuteNonQueryAsync();
        }

        Guid tenantId = Guid.CreateVersion7();

        DbContextOptions<ExploreDbContext> writeOptions = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(database.GetConnectionString())
            .AddInterceptors(new FailMandatoryIdentitySaveInterceptor())
            .Options;
        await using (var write = new ExploreDbContext(writeOptions))
        {
            write.EnableTenantFilterBypass("Atomic onboarding rollback test.");
            var policyRepository = new TenantSettingRepository(write);
            var documentRepository = new TenantSettingsDocumentRepository(write);
            var onboardingRepository = new TenantOnboardingStateRepository(write);
            var unitOfWork = new EfCoreUnitOfWork(write);
            TenantSettingsDocument branding = TenantBrandingSettingsDocumentDefaults.Create(tenantId, "Atomic Brand");
            TenantSettingsDocument identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(
                tenantId,
                new TenantDirectoryOperatorIdentitySettings
                {
                    PublicName = "Atomic Operator",
                    LegalName = "Atomic Operator ASBL",
                    OperatorKindCode = "registered_organization",
                    JurisdictionCountryCode = "BE",
                    PublicContactEmail = "legal@example.test",
                    LegalNoticeUrl = "https://example.test/legal",
                    PrivacyUrl = "https://example.test/privacy"
                });

            await Assert.That(async () => await unitOfWork.ExecuteInTransactionAsync(async _ =>
            {
                await policyRepository.SetValueAsync(tenantId, "onboarding.atomic.policy", "true");
                await documentRepository.Create(branding);
                await onboardingRepository.Create(new TenantOnboardingState
                {
                    TenantId = tenantId,
                    Tenant = null!,
                    IsCompleted = true,
                    CurrentStep = 4,
                    TotalSteps = 4
                });
                await documentRepository.Create(identity);
            })).Throws<InvalidOperationException>();
        }

        await using var verify = new ExploreDbContext(seedOptions);
        verify.EnableTenantFilterBypass("Atomic onboarding rollback test.");
        await Assert.That(await verify.TenantSettingOverrides.AnyAsync(
            setting => setting.TenantId == tenantId && setting.SettingKey == "onboarding.atomic.policy")).IsFalse();
        await Assert.That(await verify.TenantSettingsDocuments.AnyAsync(
            document => document.TenantId == tenantId
                && (document.DocumentKey == SettingsDocumentKeys.Tenant.Branding
                    || document.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity))).IsFalse();
        await Assert.That(await verify.TenantOnboardingStates.AnyAsync(state => state.TenantId == tenantId)).IsFalse();
    }

    private sealed class FailMandatoryIdentitySaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            bool mandatoryIdentityWrite = eventData.Context?.ChangeTracker
                .Entries<TenantSettingsDocument>()
                .Any(entry => entry.State is EntityState.Added or EntityState.Modified
                    && entry.Entity.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity) == true;
            if (mandatoryIdentityWrite)
                throw new InvalidOperationException("Injected mandatory identity write failure.");
            return ValueTask.FromResult(result);
        }
    }
}
