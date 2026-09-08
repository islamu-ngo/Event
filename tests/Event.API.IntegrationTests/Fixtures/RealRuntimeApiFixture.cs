// Rate limiting disabled; focused on end-to-end API behavior with migrations and data seeding.

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// RealRuntime test fixture: production-faithful PostgreSQL-backed testing.
/// Rate limiting disabled. Suitable for end-to-end API behavior verification
/// including tenant isolation, persistence, and cache variance.
/// </summary>
public class RealRuntimeApiFixture : PostgreSqlApiFixtureBase
{
    protected override Dictionary<string, string?> GetAdditionalConfiguration() => new()
    {
        ["Testing:HostProfile"] = TestHostProfile.RealRuntime,
        ["RateLimiting:DisableInTesting"] = "true",
    };
}
