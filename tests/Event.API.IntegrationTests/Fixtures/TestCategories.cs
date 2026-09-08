namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Test category constants for the hybrid test taxonomy.
/// Use <c>[Category(TestCategories.Fast)]</c> to tag tests.
/// </summary>
public static class TestCategories
{
    /// <summary>
    /// Fast integration tests using TestAuthHandler and mocks.
    /// No Docker containers required. Default for developer inner-loop.
    /// </summary>
    public const string Fast = "Fast";

    /// <summary>
    /// Security integration tests using real Keycloak + Cerbos containers.
    /// Validates the actual OIDC token validation and ABAC policy pipeline.
    /// </summary>
    public const string Security = "Security";

    /// <summary>
    /// Policy contract tests that exercise Cerbos policies directly via gRPC.
    /// Does not go through the API layer; validates policy decisions in isolation.
    /// </summary>
    public const string PolicyContract = "PolicyContract";

    public const string Email = "Email";

    public const string RabbitMQ = "RabbitMQ";

    public const string Runtime = "Runtime";

    public const string Slow = "Slow";

    public const string Manual = "Manual";

    public const string Phase43Ticketing = "Phase43Ticketing";

}
