using FluentValidation;

namespace Explore.Application.DTOs.Onboarding.Validators;

public class KeycloakBootstrapRequestDtoValidator : AbstractValidator<KeycloakBootstrapRequestDto>
{
    private const int IdentifierMaxLength = 128;
    private const int SecretMaxLength = 4096;
    private const int UrlMaxLength = 2048;

    public KeycloakBootstrapRequestDtoValidator()
    {
        RuleFor(x => x.KeycloakBaseUrl)
            .NotEmpty()
            .WithMessage("Keycloak base URL is required.")
            .MaximumLength(UrlMaxLength)
            .WithMessage("Keycloak base URL is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Keycloak base URL must not contain control characters.")
            .Must(BeSafeHttpUrl)
            .WithMessage("Keycloak base URL must be an absolute HTTP or HTTPS URL without user info or fragments.");

        RuleFor(x => x.Realm)
            .NotEmpty()
            .WithMessage("Keycloak realm is required.")
            .MaximumLength(IdentifierMaxLength)
            .WithMessage("Keycloak realm is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Keycloak realm must not contain control characters.");

        RuleFor(x => x.BlazorClientId)
            .NotEmpty()
            .WithMessage("Blazor client ID is required.")
            .MaximumLength(IdentifierMaxLength)
            .WithMessage("Blazor client ID is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Blazor client ID must not contain control characters.");

        RuleFor(x => x.BlazorClientSecret)
            .NotEmpty()
            .WithMessage("Blazor client secret is required.")
            .MaximumLength(SecretMaxLength)
            .WithMessage("Blazor client secret is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Blazor client secret must not contain control characters.");

        RuleForEach(x => x.BlazorRedirectUris)
            .MaximumLength(UrlMaxLength)
            .WithMessage("Blazor redirect URI is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Blazor redirect URI must not contain control characters.")
            .Must(BeSafeRedirectUri)
            .WithMessage("Blazor redirect URI must be an absolute HTTP or HTTPS URL, may end with /*, and must not include user info, query, or fragments.");

        RuleForEach(x => x.BlazorWebOrigins)
            .MaximumLength(UrlMaxLength)
            .WithMessage("Blazor web origin is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Blazor web origin must not contain control characters.")
            .Must(BeSafeWebOrigin)
            .WithMessage("Blazor web origin must be '+', or an absolute HTTP or HTTPS origin without path, user info, query, or fragments.");

        RuleFor(x => x.ApiClientId)
            .MaximumLength(IdentifierMaxLength)
            .WithMessage("API client ID is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("API client ID must not contain control characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.ApiClientId));

        RuleFor(x => x.ApiClientSecret)
            .MaximumLength(SecretMaxLength)
            .WithMessage("API client secret is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("API client secret must not contain control characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.ApiClientSecret));

        RuleFor(x => x.BootstrapAdminUsername)
            .NotEmpty()
            .WithMessage("Bootstrap admin username is required.")
            .MaximumLength(IdentifierMaxLength)
            .WithMessage("Bootstrap admin username is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Bootstrap admin username must not contain control characters.");

        RuleFor(x => x.BootstrapAdminPassword)
            .NotEmpty()
            .WithMessage("Bootstrap admin password is required.")
            .MaximumLength(SecretMaxLength)
            .WithMessage("Bootstrap admin password is too long.")
            .Must(NotContainControlCharacters)
            .WithMessage("Bootstrap admin password must not contain control characters.");

        RuleFor(x => x.Mode)
            .IsInEnum()
            .WithMessage("Keycloak bootstrap mode is not supported.");
    }

    private static bool BeSafeHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool BeSafeRedirectUri(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var value = url.Trim();
        if (value.EndsWith("/*", StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Query)
               && string.IsNullOrEmpty(uri.Fragment);
    }

    private static bool BeSafeWebOrigin(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (origin == "+")
            return true;

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
               && string.IsNullOrEmpty(uri.UserInfo)
               && string.IsNullOrEmpty(uri.Query)
               && string.IsNullOrEmpty(uri.Fragment)
               && uri.AbsolutePath == "/";
    }

    private static bool NotContainControlCharacters(string? value)
    {
        return value is null || !value.Any(char.IsControl);
    }
}
