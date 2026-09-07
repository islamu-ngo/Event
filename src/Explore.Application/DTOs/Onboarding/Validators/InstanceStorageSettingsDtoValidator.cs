using Explore.Application.DTOs.Storage;
using Explore.Domain;
using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public sealed class InstanceStorageSettingsDtoValidator : AbstractValidator<InstanceStorageSettingsDto>
{
    public InstanceStorageSettingsDtoValidator()
    {
        RuleFor(settings => settings.Provider)
            .Must(provider => StorageProviders.All.Contains(NormalizeProvider(provider)))
            .WithMessage("Storage provider must be local or s3_compatible.");

        RuleFor(settings => settings.DefaultMaxUploadBytes)
            .GreaterThan(0)
            .WithMessage("Default max upload bytes must be greater than zero.");

        RuleFor(settings => settings.DefaultTenantQuotaBytes)
            .GreaterThan(0)
            .WithMessage("Default tenant quota bytes must be greater than zero.");

        RuleFor(settings => settings.InstanceMaxUploadBytes)
            .GreaterThan(0)
            .WithMessage("Instance max upload bytes must be greater than zero.");

        RuleFor(settings => settings.DefaultMaxUploadBytes)
            .LessThanOrEqualTo(settings => settings.InstanceMaxUploadBytes)
            .WithMessage("Default max upload bytes cannot exceed the instance max upload ceiling.");



        RuleFor(settings => settings.Routes)
            .Must(HaveUniqueKnownRoutes)
            .WithMessage("Storage route matrix can only contain unique images, documents, and general route keys.")
            .Must((settings, routes) => RoutesFitInstanceCeiling(routes, settings.InstanceMaxUploadBytes))
            .WithMessage("Storage route max upload bytes cannot exceed the instance max upload ceiling.");

        RuleForEach(settings => settings.Routes).ChildRules(route =>
        {
            route.RuleFor(item => item.RouteKey)
                .Must(routeKey => StorageRouteKeys.All.Contains(NormalizeRouteKey(routeKey)))
                .WithMessage("Storage route key must be images, documents, or general.");
            route.RuleFor(item => item.Provider)
                .Must(provider => StorageProviders.All.Contains(NormalizeProvider(provider)))
                .WithMessage("Storage route provider must be local or s3_compatible.");
            route.RuleFor(item => item.MaxUploadBytes)
                .GreaterThan(0)
                .WithMessage("Storage route max upload bytes must be greater than zero.");
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

    private static bool RoutesFitInstanceCeiling(IReadOnlyCollection<StorageRouteSettingsDto>? routes, long instanceMaxUploadBytes)
        => routes is null || routes.All(route => route.MaxUploadBytes <= instanceMaxUploadBytes);

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
