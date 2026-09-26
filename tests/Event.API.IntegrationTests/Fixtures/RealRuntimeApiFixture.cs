// Rate limiting disabled; focused on end-to-end API behavior with migrations and data seeding.

using Event.Api.IntegrationTests.Builders;
using Explore.Domain.Constants;
using Explore.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// RealRuntime test fixture: production-faithful PostgreSQL-backed testing.
/// Rate limiting disabled. Suitable for end-to-end API behavior verification
/// including tenant isolation, persistence, and cache variance.
/// </summary>
public class RealRuntimeApiFixture : PostgreSqlApiFixtureBase
{
    public async Task ResetWithActiveDefaultTenantAsync()
    {
        await ResetDatabaseAsync();
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        db.Tenants.Add(new TenantBuilder().WithId(PlatformDefaults.DefaultTenantId).Build());
        await db.SaveChangesAsync();
    }

    protected override Dictionary<string, string?> GetAdditionalConfiguration() => new()
    {
        ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
        ["RateLimiting:DisableInTesting"] = "true",
    };
}
