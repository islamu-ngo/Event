namespace Explore.API.Authentication;

public sealed class AtprotoJwtOptions
{
    public const string SectionName = "Atproto:Jwt";
    public const string BootstrapIssuer = "islamu-event-blazor:atproto-bootstrap";
    public const string BootstrapAudience = "islamu-event-api:atproto-bootstrap";
    public const string SessionBridgeIssuer = "islamu-event-blazor:atproto-session-bridge";
    public const string SessionBridgeAudience = "islamu-event-api:atproto-session-bridge";
    public const string SessionIssuer = "islamu-event-api:atproto-session";
    public const string SessionAudience = "islamu-event-api";
    public const string BridgePath = "/api/auth/atproto/session";
    public const string CurrentSessionPath = "/api/auth/atproto/session/current";
    public const string BootstrapHeaderName = "X-Atproto-Bootstrap-Assertion";
    public const string SessionBridgeHeaderName = "X-Atproto-Session-Bridge-Assertion";
    public const string TenantClaim = "tenant_id";
    public const string UserClaim = "user_id";
    public const string DidClaim = "atproto_did";
    public const string ClassificationClaim = "subject_classification";
    public const string CanonicalActorIdClaim = "canonical_actor_id";
    public const string ExpectedCanonicalActorConcurrencyStampClaim = "expected_actor_concurrency_stamp";
    public const string MethodClaim = "http_method";
    public const string PathClaim = "http_path";
    public const int MaximumBootstrapTokenBytes = 4 * 1024;
    public const int MaximumSessionBridgeTokenBytes = 4 * 1024;
    public const int MaximumSessionTokenBytes = 8 * 1024;

    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromMinutes(15);
}
