using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class EmailPolicyMutationPoolCompositionTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("username")]
    [Arguments("password")]
    [Arguments("database")]
    [Arguments("schema")]
    [Arguments("provider")]
    [Arguments("tls")]
    [Arguments("role")]
    public async Task InvalidPurposeInputsNeverConstructFallbackPools(string defect)
    {
        var runtime = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime);
        var instance = fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation);
        var broken = defect switch
        {
            "username" => instance with { Username = runtime.Username },
            "password" => instance with { Password = runtime.Password },
            "database" => instance with { Database = instance.Database + "_different" },
            "schema" => instance with { Schema = instance.Schema + "_different" },
            "provider" => instance with { Provider = PrimaryDatabaseProvider.Sqlite },
            "tls" => instance with { TrustServerCertificate = !instance.TrustServerCertificate },
            "role" => instance with { Role = PrimaryDatabaseRole.Global },
            _ => throw new ArgumentOutOfRangeException(nameof(defect))
        };
        await Assert.That(() => new EmailPolicyMutationDbContextFactories(runtime,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global), broken,
            fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyTenantMutation)))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task StableEnrolledAccountsOpenDistinctSealedPurposeConnections()
    {
        Guid firstId = await SeedTenantAsync();
        Guid secondId = await SeedTenantAsync();
        var pools = Pools();
        await using var instance = await pools.CreateAsync(PolicyMutationScope.EmailInstance, default);
        await using var first = await pools.CreateAsync(PolicyMutationScope.EmailTenant(firstId), default);
        await using var second = await pools.CreateAsync(PolicyMutationScope.EmailTenant(secondId), default);

        await Assert.That(await LoginAsync(instance)).IsEqualTo(fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
        await Assert.That(await LoginAsync(first)).IsEqualTo(fixture.NativeQueueEnrollment.EmailTenantMutationLogin);
        await Assert.That(await LoginAsync(second)).IsEqualTo(fixture.NativeQueueEnrollment.EmailTenantMutationLogin);
        await Assert.That(instance.TenantFilterTenantId).IsNull();
        await Assert.That(first.TenantFilterTenantId).IsEqualTo((Guid?)firstId);
        await Assert.That(second.TenantFilterTenantId).IsEqualTo((Guid?)secondId);
        await Assert.That(ReferenceEquals(first, second)).IsFalse();
        await Assert.That(instance.IsTenantFilterBypassed || first.IsTenantFilterBypassed || second.IsTenantFilterBypassed).IsFalse();
        await Assert.That(async () =>
        {
            await using var rejected = await pools.CreateAsync(PolicyMutationScope.Instance, default);
        }).Throws<ArgumentException>();
        await Assert.That(async () =>
        {
            await using var rejected = await pools.CreateAsync(PolicyMutationScope.EmailTenant(Guid.CreateVersion7()), default);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NativeSettingReceiptAndRevisionShareThePurposeTransaction(bool tenantPurpose)
    {
        Guid tenantId = await SeedTenantAsync();
        var selection = tenantPurpose ? PolicyMutationScope.EmailTenant(tenantId) : PolicyMutationScope.EmailInstance;
        await using var context = await Pools().CreateAsync(selection, default);
        var unitOfWork = new EfCoreUnitOfWork(context);
        var mutationLock = new RelationalSettingMutationLock(context, unitOfWork);
        var writer = EmailDispatchSqliteFixture.CreateInternalEmailSettingsWriter(context, mutationLock);
        string value = $"\"purpose-{Guid.CreateVersion7():N}\"";

        await Assert.That(async () => await mutationLock.ExecuteOrderedGroupsAsync(
            [EmailDeliverySettingKeys.All], token => unitOfWork.ExecuteSerializableAsync<bool>(async inner =>
            {
                var result = await writer.ApplyInternalAsync(
                    [new(selection.TenantId, GovernanceSettingKeys.Email.FromName,
                        EmailDeliverySettingMutationKind.SetValue, value)],
                    actorUserId: null, inner);
                await Assert.That(result.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
                // Applied requires the native receipt in this transaction, including its original
                // login, before the unchanged C# evaluator may reconcile control revisions.
                await Assert.That(await LoginAsync(context)).IsEqualTo(tenantPurpose
                    ? fixture.NativeQueueEnrollment.EmailTenantMutationLogin
                    : fixture.NativeQueueEnrollment.EmailInstanceMutationLogin);
                throw new RollbackProbeException();
            }, token))).Throws<RollbackProbeException>();

        await using var observer = fixture.CreateDbContext();
        await Assert.That(await observer.TenantSettingOverrides.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId && row.Value == value)).IsFalse();
        await Assert.That(await observer.SystemSettings.AsNoTracking().AnyAsync(row => row.Value == value)).IsFalse();
        await Assert.That(await observer.EmailDispatchTenantControls.AsNoTracking()
            .AnyAsync(row => row.TenantId == tenantId)).IsFalse();
    }

    private EmailPolicyMutationDbContextFactories Pools() => new(
        fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime),
        fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global),
        fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyInstanceMutation),
        fixture.GetNativeQueueOptions(PrimaryDatabaseRole.EmailPolicyTenantMutation));

    private async Task<Guid> SeedTenantAsync()
    {
        await using var context = fixture.CreateDbContext();
        Guid id = Guid.CreateVersion7();
        context.Tenants.Add(new Tenant
        {
            Id = id,
            FullName = "Email purpose composition",
            Slug = $"email-purpose-{id:N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
        return id;
    }

    private static Task<string> LoginAsync(ExploreDbContext context) =>
        context.Database.SqlQueryRaw<string>("SELECT session_user::text AS \"Value\"").SingleAsync();

    private sealed class RollbackProbeException : Exception;
}
