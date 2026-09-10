namespace Explore.Application.Services;

public sealed class AccountAuthorityLifecycleEmailOptions
{
    public const string SectionName = "AccountAuthorityLifecycleEmail";

    public bool Enabled { get; set; }
    public bool ProviderConfigured { get; set; }
}
