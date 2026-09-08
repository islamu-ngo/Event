using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.PaidEventPolicies;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class ConfigurationManifestPaidPolicyCeilingTests(
    PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task ExistingTenantOutsideProposedInstanceCeiling_IsRejectedAtomically()
    {
        await fixture.ResetAsync();
        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            await new PaidEventPolicyRepository(seed).AddAsync(
                PaidEventPolicyVersion.CreateDefaultInstance(),
                CancellationToken.None);
            await seed.SaveChangesAsync();
        }

        await using (ExploreDbContext bootstrap = fixture.CreateDbContext())
        {
            var failureRecorder =
                Substitute.For<IConfigurationManifestFailureRecorder>();
            var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                bootstrap,
                new ConfigurationManifestApplicationTestSupport.ExistencePreflight(
                    new TenantRepository(bootstrap),
                    new PaidEventPolicyRepository(bootstrap)),
                new ConfigurationManifestOperationRepository(bootstrap),
                failureRecorder);

            var applied = await handler.Handle(
                new ApplyConfigurationManifestCommand(
                    ConfigurationManifestApplicationTestSupport.PaidPolicySource(
                        new string('f', ConfigurationManifestOperation.DigestLength),
                        "usd-community")),
                CancellationToken.None);

            await Assert.That(applied.IsSuccess).IsTrue();
            await failureRecorder.DidNotReceiveWithAnyArgs()
                .RecordAsync(default!, default);
        }

        await using (ExploreDbContext revision = fixture.CreateDbContext())
        {
            var unitOfWork = new EfCoreUnitOfWork(revision);
            var boundary = new PaidEventPolicyMutationBoundary(
                new PaidEventPolicyRepository(revision),
                unitOfWork,
                new RelationalSettingMutationLock(revision, unitOfWork));

            PaidEventPolicyMutationResult result = await boundary.ReviseInstanceAsync(
                EurOnlyRevision(),
                CancellationToken.None);

            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.FailureCode)
                .IsEqualTo(PaidEventPolicyMutationFailureCodes.ValidationFailed);
        }

        await using ExploreDbContext verification = fixture.CreateDbContext();
        PaidEventPolicyVersion[] instanceVersions = await verification
            .PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(policy => policy.TenantId == null)
            .ToArrayAsync();
        PaidEventPolicyVersion tenantPolicy = await verification
            .PaidEventPolicyVersions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(policy => policy.TenantId != null && policy.IsActive);

        await Assert.That(instanceVersions.Length).IsEqualTo(1);
        await Assert.That(instanceVersions[0].IsActive).IsTrue();
        await Assert.That(instanceVersions[0].VersionNumber).IsEqualTo(1);
        await Assert.That(tenantPolicy.AllowedCurrencyCodes)
            .IsEquivalentTo(["USD"]);
        await Assert.That(await verification.ConfigurationManifestOperations
            .CountAsync()).IsEqualTo(1);
    }

    private static RevisePaidEventPolicyDto EurOnlyRevision() => new()
    {
        IsPaymentsEnabled = false,
        AllowedOrganizerKindIds = [(int)ActorTypeEnum.Organization],
        RequiresLocalVerification = false,
        AllowedCurrencyCodes = ["EUR"],
        DefaultCurrencyCode = "EUR",
        RefundProtectionIds = Enum.GetValues<PaidEventRefundProtection>()
            .Select(protection => (int)protection)
            .ToArray(),
        CurrencyRiskLimits = [],
        RequiresFirstPaidEventReview = false,
        FarFutureReviewThresholdDays = null
    };
}
