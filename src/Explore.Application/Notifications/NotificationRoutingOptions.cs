namespace Explore.Application.Notifications;

public sealed class NotificationRoutingOptions
{
    public const string SectionName = "NotificationRouting";

    public NotificationOwnership IdentityLifecycleOwner { get; set; } = NotificationOwnership.AccountAuthority;
    public AccountAuthorityKind DefaultAccountAuthorityKind { get; set; } = AccountAuthorityKind.Keycloak;

    public NotificationOwnership ProductLifecycleOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership EventLifecycleOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership RegistrationLifecycleOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership TrustSafetyReportingOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership TrustSafetyModerationOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership ProviderInternalOwner { get; set; } = NotificationOwnership.ExternalWorkflowProvider;
    public NotificationOwnership PlatformOperationsOwner { get; set; } = NotificationOwnership.IslamuEvent;
    public NotificationOwnership MarketingOwner { get; set; } = NotificationOwnership.IslamuEvent;

    public bool AllowExternalUserFacingModerationEmails { get; set; }
    public ExternalWorkflowProviderKind ExternalUserFacingModerationProvider { get; set; } = ExternalWorkflowProviderKind.None;
    public ExternalWorkflowProviderKind ProviderInternalProvider { get; set; } = ExternalWorkflowProviderKind.Other;

    public NotificationOwnership GetOwner(NotificationCategory category) => category switch
    {
        NotificationCategory.IdentityLifecycle => IdentityLifecycleOwner,
        NotificationCategory.ProductLifecycle => ProductLifecycleOwner,
        NotificationCategory.EventLifecycle => EventLifecycleOwner,
        NotificationCategory.RegistrationLifecycle => RegistrationLifecycleOwner,
        NotificationCategory.TrustSafetyReporting => TrustSafetyReportingOwner,
        NotificationCategory.TrustSafetyModeration => TrustSafetyModerationOwner,
        NotificationCategory.ProviderInternal => ProviderInternalOwner,
        NotificationCategory.PlatformOperations => PlatformOperationsOwner,
        NotificationCategory.Marketing => MarketingOwner,
        _ => NotificationOwnership.Disabled
    };

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        ValidateDefinedOwners(errors);
        ValidateAccountAuthority(errors);
        ValidateIslamuOwnedCategories(errors);
        ValidateExternalProviderRules(errors);

        return errors;
    }

    private void ValidateDefinedOwners(List<string> errors)
    {
        foreach (var (name, owner) in EnumerateOwners())
        {
            if (!Enum.IsDefined(owner))
            {
                errors.Add($"{name} has unsupported owner '{owner}'.");
            }
        }
    }

    private void ValidateAccountAuthority(List<string> errors)
    {
        if (IdentityLifecycleOwner != NotificationOwnership.AccountAuthority)
        {
            errors.Add("IdentityLifecycleOwner must be AccountAuthority because credential-token lifecycle email is owned by the account authority.");
        }

        if (!Enum.IsDefined(DefaultAccountAuthorityKind) || DefaultAccountAuthorityKind == AccountAuthorityKind.None)
        {
            errors.Add("DefaultAccountAuthorityKind must name the account authority for identity lifecycle email.");
        }
    }

    private void ValidateIslamuOwnedCategories(List<string> errors)
    {
        ValidateIslamuOwned(nameof(ProductLifecycleOwner), ProductLifecycleOwner, errors);
        ValidateIslamuOwned(nameof(EventLifecycleOwner), EventLifecycleOwner, errors);
        ValidateIslamuOwned(nameof(RegistrationLifecycleOwner), RegistrationLifecycleOwner, errors);
        ValidateIslamuOwned(nameof(PlatformOperationsOwner), PlatformOperationsOwner, errors);
        ValidateIslamuOwned(nameof(MarketingOwner), MarketingOwner, errors);
    }

    private static void ValidateIslamuOwned(string name, NotificationOwnership owner, List<string> errors)
    {
        if (owner != NotificationOwnership.IslamuEvent && owner != NotificationOwnership.Disabled)
        {
            errors.Add($"{name} must be IslamuEvent or Disabled; product-domain notifications cannot be owned by account authorities or workflow providers.");
        }
    }

    private void ValidateExternalProviderRules(List<string> errors)
    {
        ValidateTrustSafetyOwner(nameof(TrustSafetyReportingOwner), TrustSafetyReportingOwner, errors);
        ValidateTrustSafetyOwner(nameof(TrustSafetyModerationOwner), TrustSafetyModerationOwner, errors);

        if (ProviderInternalOwner == NotificationOwnership.ExternalWorkflowProvider
            && (!Enum.IsDefined(ProviderInternalProvider) || ProviderInternalProvider == ExternalWorkflowProviderKind.None))
        {
            errors.Add("ProviderInternalProvider must be set when provider-internal notifications are externally owned.");
        }

        if (ProviderInternalOwner == NotificationOwnership.AccountAuthority)
        {
            errors.Add("ProviderInternalOwner cannot be AccountAuthority.");
        }
    }

    private void ValidateTrustSafetyOwner(string name, NotificationOwnership owner, List<string> errors)
    {
        if (owner == NotificationOwnership.AccountAuthority)
        {
            errors.Add($"{name} cannot be AccountAuthority; trust-safety notifications are product or delegated workflow decisions.");
        }

        if (owner == NotificationOwnership.ExternalWorkflowProvider && !AllowExternalUserFacingModerationEmails)
        {
            errors.Add($"{name} cannot be ExternalWorkflowProvider unless external user-facing moderation email delegation is explicitly enabled.");
        }

        if (owner == NotificationOwnership.ExternalWorkflowProvider
            && (!Enum.IsDefined(ExternalUserFacingModerationProvider)
                || ExternalUserFacingModerationProvider == ExternalWorkflowProviderKind.None))
        {
            errors.Add("ExternalUserFacingModerationProvider must be set when trust-safety notifications are delegated externally.");
        }
    }

    private IEnumerable<(string Name, NotificationOwnership Owner)> EnumerateOwners()
    {
        yield return (nameof(IdentityLifecycleOwner), IdentityLifecycleOwner);
        yield return (nameof(ProductLifecycleOwner), ProductLifecycleOwner);
        yield return (nameof(EventLifecycleOwner), EventLifecycleOwner);
        yield return (nameof(RegistrationLifecycleOwner), RegistrationLifecycleOwner);
        yield return (nameof(TrustSafetyReportingOwner), TrustSafetyReportingOwner);
        yield return (nameof(TrustSafetyModerationOwner), TrustSafetyModerationOwner);
        yield return (nameof(ProviderInternalOwner), ProviderInternalOwner);
        yield return (nameof(PlatformOperationsOwner), PlatformOperationsOwner);
        yield return (nameof(MarketingOwner), MarketingOwner);
    }
}
