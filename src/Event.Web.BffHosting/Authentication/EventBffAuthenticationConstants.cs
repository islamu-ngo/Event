namespace Event.Web.BffHosting.Authentication;

public static class EventBffAuthenticationConstants
{
    public const string OidcSchemePropertyKey = "oidc_scheme";
    public const string AuthenticationProviderPropertyKey = "authentication_provider";
    public static readonly object TokenRefreshRejectedItemKey = new();
}
