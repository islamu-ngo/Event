namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Host profile identifiers controlling which WebApplicationFactory configuration is active.
/// </summary>
public static class TestHostProfile
{
    /// <summary>Fast API contract checks: serializers, ProblemDetails, HAL, headers. Uses EF InMemory.</summary>
    public const string Contract = "Contract";

    /// <summary>Production-faithful end-to-end: real PostgreSQL, migrations, tenant isolation.</summary>
    public const string RealRuntime = "RealRuntime";

    /// <summary>Timing-sensitive scenarios: rate limiting, timeouts, auth conflicts.</summary>
    public const string Stress = "Stress";

    /// <summary>Security infrastructure: real Keycloak JWT validation, real Cerbos PDP authorization.</summary>
    public const string Security = "Security";
}
