using Event.Api.IntegrationTests.Authentication;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using TUnit.Core;

namespace ApiIntegrationTests.Features;

[NotInParallel]
[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class StorageInstancePolicyReadTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task InstancePolicyResolvesWithoutSelectingARuntimeTenant()
    {
        await using var host = new TenantBrandingNativeAuthenticationHost(
            postgres, Guid.CreateVersion7(), [], configureServices: services =>
            {
                services.RemoveAll<ExploreDbContext>();
                services.AddScoped(_ =>
                {
                    var context = postgres.CreateEnrolledTenantlessDbContext();
                    context.BindTenantLease();
                    return context;
                });
            });
        await using var scope = host.Services.CreateAsyncScope();
        var resolver = scope.ServiceProvider.GetRequiredService<IStoragePolicyResolver>();

        var policy = await resolver.ResolveAsync(
            null, TestContext.Current!.Execution.CancellationToken);

        await Assert.That(policy.TenantId).IsNull();
        await Assert.That(policy.TenantOverridesAllowed).IsFalse();
        await Assert.That(policy.TenantStorageLocked).IsTrue();
        await Assert.That(policy.MaxUploadBytes <= policy.InstanceMaxUploadBytes).IsTrue();
        var runtime = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(runtime.TenantFilterTenantId).IsNull();
        var tenantReader = scope.ServiceProvider.GetRequiredService<IHierarchicalSettingsReader>();
        var denied = await Assert.ThrowsAsync<PostgresException>(() => tenantReader.ResolveBatchAsync(
            [GovernanceSettingKeys.Storage.Provider], new SettingContext(),
            TestContext.Current!.Execution.CancellationToken));
        await Assert.That(denied.SqlState).IsEqualTo("42501");
    }
}
