using Explore.Application.DTOs.Storage;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.DTOs.Tenant.Validators;

public sealed class PatchTenantStorageSettingsDtoValidator : AbstractValidator<PatchTenantStorageSettingsDto>
{
    public PatchTenantStorageSettingsDtoValidator()
    {
        RuleFor(settings => settings)
            .Must(HasAnyUpdate)
            .WithMessage("At least one tenant storage setting update must be provided.");
    }

    private static bool HasAnyUpdate(PatchTenantStorageSettingsDto settings)
        => HasAnyPolicyUpdate(settings.Policy) || HasAnyS3Update(settings.S3);

    private static bool HasAnyPolicyUpdate(PatchTenantStoragePolicyDto? policy)
        => policy is not null &&
           (policy.Provider.HasValue ||
            policy.MaxUploadBytes.HasValue ||
            policy.TenantQuotaBytes.HasValue ||
            policy.Routes.HasValue);

    private static bool HasAnyS3Update(PatchTenantStorageS3Dto? s3)
        => s3 is not null &&
           (s3.Endpoint.HasValue ||
            s3.PublicEndpoint.HasValue ||
            s3.BucketName.HasValue ||
            s3.Region.HasValue ||
            s3.ForcePathStyle.HasValue ||
            s3.UploadUrlExpirationMinutes.HasValue);
}

public sealed class TenantStorageSettingsDtoValidator : AbstractValidator<TenantStorageSettingsDto>
{
    private static readonly string[] AllowedProviders =
    [
        StorageProviders.Local,
        StorageProviders.S3Compatible
    ];

    public TenantStorageSettingsDtoValidator(long instanceMaxUploadBytes)
    {
        var ceilingBytes = instanceMaxUploadBytes > 0 ? instanceMaxUploadBytes : 1;

        RuleFor(settings => settings.Provider)
            .Must(provider => AllowedProviders.Contains(NormalizeProvider(provider)))
            .WithMessage("Storage provider must be local or s3_compatible.");

        RuleFor(settings => settings.MaxUploadBytes)
            .GreaterThan(0)
            .WithMessage("Max upload bytes must be greater than zero.");

        RuleFor(settings => settings.MaxUploadBytes)
            .LessThanOrEqualTo(ceilingBytes)
            .WithMessage($"Max upload bytes cannot exceed the instance ceiling of {ceilingBytes} bytes.");

        RuleFor(settings => settings.TenantQuotaBytes)
            .GreaterThan(0)
            .WithMessage("Tenant quota bytes must be greater than zero.");



        RuleFor(settings => settings.Routes)
            .Must(HaveUniqueKnownRoutes)
            .WithMessage("Storage route matrix can only contain unique images, documents, and general route keys.");

        RuleForEach(settings => settings.Routes).ChildRules(route =>
        {
            route.RuleFor(item => item.RouteKey)
                .Must(routeKey => StorageRouteKeys.All.Contains(NormalizeRouteKey(routeKey)))
                .WithMessage("Storage route key must be images, documents, or general.");
            route.RuleFor(item => item.Provider)
                .Must(provider => AllowedProviders.Contains(NormalizeProvider(provider)))
                .WithMessage("Storage route provider must be local or s3_compatible.");
            route.RuleFor(item => item.MaxUploadBytes)
                .GreaterThan(0)
                .WithMessage("Storage route max upload bytes must be greater than zero.")
                .LessThanOrEqualTo(ceilingBytes)
                .WithMessage($"Storage route max upload bytes cannot exceed the instance ceiling of {ceilingBytes} bytes.");
        });

        RuleFor(settings => settings.S3Endpoint)
            .Must(BeEmptyOrAbsoluteHttpUri)
            .WithMessage("S3 endpoint must be an absolute HTTP or HTTPS URI.");

        RuleFor(settings => settings.S3PublicEndpoint)
            .Must(BeEmptyOrAbsoluteHttpUri)
            .WithMessage("S3 public endpoint must be an absolute HTTP or HTTPS URI.");

        RuleFor(settings => settings.S3UploadUrlExpirationMinutes)
            .InclusiveBetween(1, 1440)
            .WithMessage("S3 upload URL expiration must be between 1 and 1440 minutes.");
    }

    private static string NormalizeProvider(string? provider)
        => provider?.Trim().ToLowerInvariant() ?? string.Empty;

    private static string NormalizeRouteKey(string? routeKey)
        => routeKey?.Trim().ToLowerInvariant() ?? string.Empty;

    private static bool HaveUniqueKnownRoutes(IReadOnlyCollection<StorageRouteSettingsDto>? routes)
    {
        if (routes is null)
        {
            return true;
        }

        var normalized = routes.Select(route => NormalizeRouteKey(route.RouteKey)).ToArray();
        return normalized.All(StorageRouteKeys.All.Contains) &&
               normalized.Distinct(StringComparer.Ordinal).Count() == normalized.Length;
    }

    private static bool BeEmptyOrAbsoluteHttpUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
