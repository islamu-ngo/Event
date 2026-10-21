namespace Explore.Persistence.Security;

/// <summary>Fixed scanner storage permissions; paired authentication selection remains in C#.</summary>
public static class PostgresAdmissionScannerAuthenticationContract
{
    public const string OwnerRole = "event_scanner_authentication_owner";
    public const string MigratorRole = "event_scanner_authentication_migrator";
    public const string GlobalRole = "event_scanner_authentication_global";
    private static readonly PostgresAuthenticationSourceProfile Profile = new(typeof(Explore.Domain.AdmissionScannerCapability), GlobalRole,
        "scanner_authentication", [OwnerRole, MigratorRole],
        ["Id", "TenantId", "IssueRequestId", "EventId", "AdmissionTargetId", "LookupKeyVersion", "LookupDigest", "DeviceLabel",
            "Actions", "ExpiresAt", "IssuedByActorId", "IssuedAt", "RevokedByActorId", "RevokedAt", "RevocationReason", "ConcurrencyStamp"],
        [], ["_for_authentication(integer[],text[])"]);
    // Machine-purpose guards still use this name to detect forbidden retired artifacts.
    public static string FunctionSql(ExploreDbContext context) => Profile.RetiredName(context, "_for_authentication");
    public static Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default) => Profile.ApplyAsync(context, enrollment, cancellationToken);
    public static Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment? enrollment,
        CancellationToken cancellationToken = default) => Profile.RequireReadyAsync(context, enrollment, cancellationToken);
    internal static string GlobalPolicyException(ExploreDbContext context, string globalLogin) => Profile.PolicyException(context, globalLogin);
}
