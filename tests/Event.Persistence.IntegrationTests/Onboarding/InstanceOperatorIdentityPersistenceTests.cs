namespace Event.Persistence.IntegrationTests.Onboarding;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TUnit.Assertions;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class InstanceOperatorIdentityPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments(SystemMutation.Upsert)]
    [Arguments(SystemMutation.UpsertLock)]
    public async Task GenericSystemSettingMutationsRejectOperatorIdentityKeyBeforeSave(SystemMutation mutation)
    {
        await fixture.ResetAsync();

        var saveObserver = new SaveObserver();
        await using ExploreDbContext context = fixture.CreateDbContext(saveObserver);
        var repository = new SystemSettingRepository(
            context,
            new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));
        var candidate = new SystemSetting
        {
            SettingKey = InstanceOperatorIdentitySettingKeys.OperatorIdentity,
            Value = "{}",
            ValueType = SettingValueType.Json,
            IsLocked = false,
            CreatedAt = DateTime.UtcNow
        };
        Func<Task> guardedMutation = mutation switch
        {
            SystemMutation.Upsert => () => repository.UpsertAsync(candidate),
            SystemMutation.UpsertLock => () => repository.UpsertLockAsync(candidate),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        await Assert.ThrowsAsync<InvalidOperationException>(guardedMutation);
        await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
        await Assert.That(context.ChangeTracker.Entries().Any(entry =>
            entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)).IsFalse();

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.SystemSettings
            .AsNoTracking()
            .AnyAsync(setting => setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity))
            .IsFalse();
    }

    [Test]
    public async Task TenantSettingMutationsRejectOperatorIdentityKeyBeforeSave()
    {
        await fixture.ResetAsync();
        Guid tenantId;
        await using (ExploreDbContext seedContext = fixture.CreateDbContext())
        {
            var tenant = new Tenant
            {
                FullName = "Operator Identity Guard Tenant",
                Slug = $"operator-identity-{Guid.NewGuid():N}",
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            seedContext.Tenants.Add(tenant);
            await seedContext.SaveChangesAsync();
            tenantId = tenant.Id;
        }

        var saveObserver = new SaveObserver();
        await using (ExploreDbContext context = fixture.CreateDbContext(saveObserver))
        {
            var repository = new TenantSettingRepository(context,
                new RelationalSettingMutationLock(context, new EfCoreUnitOfWork(context)));

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SetValueAsync(
                tenantId,
                InstanceOperatorIdentitySettingKeys.OperatorIdentity,
                "{}"));
            await Assert.That(saveObserver.SaveCount).IsEqualTo(0);
        }

        await using ExploreDbContext verificationContext = fixture.CreateDbContext();
        await Assert.That(await verificationContext.TenantSettingOverrides
            .AsNoTracking()
            .AnyAsync(setting =>
                setting.TenantId == tenantId
                && setting.SettingKey == InstanceOperatorIdentitySettingKeys.OperatorIdentity))
            .IsFalse();
    }

    [Test]
    public async Task DedicatedServiceSave_RoundTripsDocumentAndRotatesRevisionUnderRealTransaction()
    {
        await fixture.ResetAsync();
        await using ExploreDbContext context = fixture.CreateDbContext();
        var unitOfWork = new EfCoreUnitOfWork(context);
        var service = new InstanceOperatorIdentityService(
            new SystemSettingRepository(context, new RelationalSettingMutationLock(context, unitOfWork)),
            new InstanceBootstrapStateRepository(context),
            unitOfWork);

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> first =
            await service.SaveAsync(ValidCandidate(), expectedRevision: null);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(first.Id.Readiness.IsReady).IsTrue();
        Guid firstRevision = first.Id.Revision;

        InstanceOperatorIdentityDocument document = await service.GetCurrentAsync();
        await Assert.That(document.Settings).IsNotNull();
        await Assert.That(document.Settings!.OperatorId!.Value.Version).IsEqualTo(7);
        await Assert.That(document.Settings.Revision).IsEqualTo(firstRevision);
        await Assert.That(document.Settings.PublicName).IsEqualTo("Independent Operator");
        await Assert.That(document.Readiness.IsReady).IsTrue();
        await Assert.That(document.Readiness.Identity).IsNotNull();
        await Assert.That(document.Readiness.DocumentRevision).IsEqualTo(firstRevision);

        BaseCommandResponse<InstanceOperatorIdentitySavedDocument> second =
            await service.SaveAsync(ValidCandidate() with { PublicName = "Updated Operator" }, firstRevision);

        await Assert.That(second.IsSuccess).IsTrue();
        await Assert.That(second.Id.Revision).IsNotEqualTo(firstRevision);

        InstanceOperatorIdentityDocument updated = await service.GetCurrentAsync();
        await Assert.That(updated.Settings!.Revision).IsEqualTo(second.Id.Revision);
        await Assert.That(updated.Settings.PublicName).IsEqualTo("Updated Operator");

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => service.SaveAsync(ValidCandidate(), firstRevision));
    }

    public enum SystemMutation
    {
        Upsert,
        UpsertLock
    }

    private static InstanceOperatorIdentitySettings ValidCandidate() => new()
    {
        PublicName = "Independent Operator",
        LegalName = "Independent Operator ASBL",
        OperatorKindCode = "registered_organization",
        JurisdictionCountryCode = "BE",
        RegistrationIdentifier = "BE 0123.456.789",
        PublicContactEmail = "contact@example.test",
        WebsiteUrl = "https://example.test",
        LegalNoticeUrl = "https://example.test/legal",
        TermsUrl = "https://example.test/terms",
        PrivacyUrl = "https://example.test/privacy",
        OfficialOrigin = "https://event.example.org"
    };

    private sealed class SaveObserver : SaveChangesInterceptor
    {
        public int SaveCount { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return ValueTask.FromResult(result);
        }
    }
}
