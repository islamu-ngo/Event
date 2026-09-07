using System.Text.Json;
using FluentValidation;

namespace Explore.Application.DTOs.Management.Validators;

public sealed class ManagementTenantProvisioningRequestValidator
    : AbstractValidator<ManagementTenantProvisioningRequestDto>
{
    public ManagementTenantProvisioningRequestValidator()
    {
        RuleFor(request => request.SchemaVersion)
            .Equal(ManagementTenantProvisioningRequestDto.CurrentSchemaVersion);
        RuleFor(request => request.ExternalRequestId)
            .NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._:-]+$");
        RuleFor(request => request.ExternalCustomerReference)
            .NotEmpty().MaximumLength(200);
        RuleFor(request => request.TenantName)
            .NotEmpty().MaximumLength(200);
        RuleFor(request => request.TenantSlug)
            .NotEmpty().MaximumLength(100).Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$");
        RuleFor(request => request.Administrator).NotNull();
        RuleFor(request => request.Plan).NotNull();
        RuleFor(request => request.ApprovedModules)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(modules => modules.Count <= 32);
        RuleForEach(request => request.ApprovedModules)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .NotEmpty().MaximumLength(100).Matches("^[A-Za-z0-9._-]+$");
        RuleFor(request => request.ApprovedModules)
            .Must(HasUniqueValues)
            .When(request => request.ApprovedModules is not null)
            .WithMessage("ApprovedModules must not contain duplicates.");
        RuleFor(request => request.InitialSettings)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(settings => settings.Count <= 32);
        RuleForEach(request => request.InitialSettings)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .SetValidator(new InitialSettingValidator());
        RuleFor(request => request.InitialSettings)
            .Must(settings => settings.Any(setting => setting is null)
                || settings.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count() == settings.Count)
            .When(request => request.InitialSettings is not null)
            .WithMessage("InitialSettings must not contain duplicate keys.");

        When(request => request.Administrator is not null, () =>
        {
            RuleFor(request => request.Administrator)
                .Must(administrator => (administrator.ExternalIdentity is null) != (administrator.Invitation is null))
                .WithMessage("Administrator must contain exactly one external identity or invitation.");
            RuleFor(request => request.Administrator.ExternalIdentity!)
                .SetValidator(new ExternalIdentityValidator())
                .When(request => request.Administrator.ExternalIdentity is not null);
            RuleFor(request => request.Administrator.Invitation!)
                .SetValidator(new InvitationValidator())
                .When(request => request.Administrator.Invitation is not null);
        });

        When(request => request.Plan is not null, () =>
        {
            RuleFor(request => request.Plan.Key).NotEmpty().MaximumLength(100);
            RuleFor(request => request.Plan.VersionId).NotEmpty();
            RuleFor(request => request.Plan.Quotas)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .Must(quotas => quotas.Count <= 16);
            RuleForEach(request => request.Plan.Quotas)
                .Cascade(CascadeMode.Stop)
                .NotNull()
                .SetValidator(new QuotaValidator());
            RuleFor(request => request.Plan.Quotas)
                .Must(quotas => quotas.Any(quota => quota is null)
                    || quotas.Select(quota => quota.Key).Distinct(StringComparer.Ordinal).Count() == quotas.Count)
                .When(request => request.Plan.Quotas is not null)
                .WithMessage("Plan quotas must not contain duplicate keys.");
        });

        When(request => request.Domain is not null, () =>
        {
            RuleFor(request => request.Domain!.Subdomain)
                .MaximumLength(100)
                .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
                .When(request => !string.IsNullOrWhiteSpace(request.Domain!.Subdomain));
            RuleFor(request => request.Domain!.CustomDomain)
                .MaximumLength(253)
                .Must(IsHostName)
                .When(request => !string.IsNullOrWhiteSpace(request.Domain!.CustomDomain));
        });

        When(request => request.Branding is not null, () =>
        {
            RuleFor(request => request.Branding!.DisplayName).MaximumLength(200);
            RuleFor(request => request.Branding!.LogoUrl).MaximumLength(2048).Must(IsSafePublicUrl)
                .When(request => !string.IsNullOrWhiteSpace(request.Branding!.LogoUrl));
            RuleFor(request => request.Branding!.FaviconUrl).MaximumLength(2048).Must(IsSafePublicUrl)
                .When(request => !string.IsNullOrWhiteSpace(request.Branding!.FaviconUrl));
            RuleFor(request => request.Branding!.CustomCssUrl).MaximumLength(2048).Must(IsSafePublicUrl)
                .When(request => !string.IsNullOrWhiteSpace(request.Branding!.CustomCssUrl));
        });

        When(request => request.Callback is not null, () =>
        {
            RuleFor(request => request.Callback!.CorrelationId).MaximumLength(100);
            RuleFor(request => request.Callback!.CallbackReference).MaximumLength(200);
        });
    }

    private static bool HasUniqueValues(IReadOnlyList<string> values) =>
        values.Any(value => value is null)
        || values.Select(value => value.Trim()).Distinct(StringComparer.Ordinal).Count() == values.Count;

    private static bool IsHostName(string? value) =>
        Uri.CheckHostName(value?.Trim().TrimEnd('.') ?? string.Empty) is UriHostNameType.Dns;

    private static bool IsSafePublicUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && string.IsNullOrEmpty(uri.UserInfo);

    private sealed class ExternalIdentityValidator : AbstractValidator<ManagementTenantExternalIdentityDto>
    {
        public ExternalIdentityValidator()
        {
            RuleFor(identity => identity.IdentityProvider).NotEmpty().MaximumLength(100);
            RuleFor(identity => identity.Subject).NotEmpty().MaximumLength(200);
            RuleFor(identity => identity.Email).NotEmpty().MaximumLength(255).EmailAddress();
            RuleFor(identity => identity.FirstName).NotEmpty().MaximumLength(100);
            RuleFor(identity => identity.LastName).NotEmpty().MaximumLength(100);
            RuleFor(identity => identity.DisplayName).MaximumLength(200);
        }
    }

    private sealed class InvitationValidator : AbstractValidator<ManagementTenantAdministratorInvitationDto>
    {
        public InvitationValidator()
        {
            RuleFor(invitation => invitation.Email).NotEmpty().MaximumLength(255).EmailAddress();
            RuleFor(invitation => invitation.FirstName).NotEmpty().MaximumLength(100);
            RuleFor(invitation => invitation.LastName).NotEmpty().MaximumLength(100);
            RuleFor(invitation => invitation.DisplayName).MaximumLength(200);
        }
    }

    private sealed class QuotaValidator : AbstractValidator<ManagementTenantQuotaDto>
    {
        public QuotaValidator()
        {
            RuleFor(quota => quota.Key).NotEmpty().MaximumLength(128);
            RuleFor(quota => quota.Limit).GreaterThanOrEqualTo(0);
        }
    }

    private sealed class InitialSettingValidator : AbstractValidator<ManagementTenantInitialSettingDto>
    {
        public InitialSettingValidator()
        {
            RuleFor(setting => setting.Key).NotEmpty().MaximumLength(200);
            RuleFor(setting => setting.JsonValue)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().MaximumLength(4096).Must(IsValidJson)
                .WithMessage("Initial setting values must be valid JSON.");
        }

        private static bool IsValidJson(string value)
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(value);
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
