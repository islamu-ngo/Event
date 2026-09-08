// ABOUTME: Options for enabling account-authority-owned identity lifecycle email delegation.
// ABOUTME: Keeps provider readiness explicit before Application records local delegation audit rows.

namespace Explore.Application.Services;

public sealed class AccountAuthorityLifecycleEmailOptions
{
    public const string SectionName = "AccountAuthorityLifecycleEmail";

    public bool Enabled { get; set; }
    public bool ProviderConfigured { get; set; }
}
