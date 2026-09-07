// ABOUTME: Defines authentication scheme names used by the API host for direct-consumer auth dispatch.
// ABOUTME: Avoids scattered magic strings across Program, handlers, middleware, and tests.

namespace Explore.Application.Constants;

public static class ApiAuthenticationSchemeNames
{
    public const string MultiAuth = "MultiAuth";

    public const string SetupSecret = "SetupSecret";

    public const string ApiKey = "ApiKey";

    public const string AdmissionScanner = "AdmissionScanner";

    public const string ManagedControlPlane = "ManagedControlPlane";

    public const string AtprotoBootstrap = "AtprotoBootstrap";

    public const string AtprotoSession = "AtprotoSession";

    public const string LocalIdentity = "LocalIdentityBearer";

    public const string LocalCredentialReplacement = "LocalCredentialReplacement";

    public const string PrivacyErasureReceipt = "PrivacyErasureReceipt";
}
