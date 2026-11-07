using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.HealthChecks;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Event.Api.IntegrationTests.Authentication;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
public sealed class SmtpNativeInstanceHealthTests(PostgreSqlContainerFixture postgres)
{
    [Test]
    public async Task DisabledInstanceEmailIsHealthyFromTenantlessRuntimeScope()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var host = new TenantBrandingNativeAuthenticationHost(
            postgres, Guid.CreateVersion7(), [], configureServices: services =>
                services.AddScoped(provider =>
                {
                    if (provider.GetRequiredService<ExploreDbContextExecutionLease>().Context is { } selected)
                        return selected;
                    var owner = provider.GetRequiredService<IDbContextFactory<ExploreDbContext>>().CreateDbContext();
                    owner.BindTenantLease();
                    return owner;
                }));
        await using var scope = host.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That(context.TenantContext?.TenantId).IsNull();
        string selected = await context.Database.SqlQueryRaw<string>(
            "SELECT current_user::text AS \"Value\" WHERE current_user=session_user").SingleAsync(token);
        await Assert.That(selected).IsEqualTo(postgres.NativeQueueEnrollment.RuntimeLogin);

        var health = new SmtpHealthCheck(
            scope.ServiceProvider.GetRequiredService<IEmailDeliveryCapabilityResolver>(),
            scope.ServiceProvider.GetRequiredService<IEmailConnectionTester>());
        HealthCheckResult result = await health.CheckHealthAsync(new HealthCheckContext(), token);

        await Assert.That(result.Status).IsEqualTo(HealthStatus.Healthy);
        await Assert.That(result.Description).IsEqualTo("smtp_disabled");
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }
}
