using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Onboarding;

[NotInParallel("PersistenceDb")]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class TenantOnboardingAtomicRollbackTests(PostgreSqlContainerFixture database)
{
    [Test]
    public async Task MandatoryIdentityWriteFailure_RollsBackPolicyBrandingOnboardingAndIdentity()
    {
        await database.ResetAsync();
        Guid tenantId = Guid.CreateVersion7();
        const string policyKey = GovernanceSettingKeys.Branding.DisplayName;
        await using (var seed = database.CreateDbContext())
        {
            seed.Tenants.Add(new Tenant
            {
                Id = tenantId,
                FullName = "Atomic onboarding",
                Slug = $"atomic-{tenantId:N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            });
            await seed.SaveChangesAsync();
        }
        var failure = new FailMandatoryIdentitySaveInterceptor();
        await using var authenticated = await AuthenticatedSettingsFixture.CreateAsync(
            () => database.CreateDbContext(), database, failure);
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            authenticated.RunAsync(tenantId, session => session.ValueAsync(policyKey, "Atomic Policy", "\"Atomic Policy\"", async token =>
        {
            var write = session.Context;
            var policyRepository = new TenantSettingRepository(write, session.MutationLock, session.Boundary);
            var documentRepository = new TenantSettingsDocumentRepository(write);
            var onboardingRepository = new TenantOnboardingStateRepository(write);
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

            await policyRepository.SetValueAsync(tenantId, policyKey, "\"Atomic Policy\"", token, session.ActorId);
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
            return true;
        })));
        await Assert.That(rejected!.Message).IsEqualTo("Injected mandatory identity write failure.");
        await Assert.That(failure.Reached).IsTrue();

        await using var verify = database.CreateDbContext();
        await Assert.That(await verify.TenantSettingOverrides.AnyAsync(
            setting => setting.TenantId == tenantId && setting.SettingKey == policyKey)).IsFalse();
        await Assert.That(await verify.TenantSettingsDocuments.AnyAsync(
            document => document.TenantId == tenantId
                && (document.DocumentKey == SettingsDocumentKeys.Tenant.Branding
                    || document.DocumentKey == SettingsDocumentKeys.Tenant.DirectoryOperatorIdentity))).IsFalse();
        await Assert.That(await verify.TenantOnboardingStates.AnyAsync(state => state.TenantId == tenantId)).IsFalse();
    }

    private sealed class FailMandatoryIdentitySaveInterceptor : SaveChangesInterceptor
    {
        internal bool Reached { get; private set; }
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
            {
                Reached = true;
                throw new InvalidOperationException("Injected mandatory identity write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }
}
