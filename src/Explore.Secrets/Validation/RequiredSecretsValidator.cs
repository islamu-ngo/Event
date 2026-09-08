using Explore.Secrets.Abstractions;
using Explore.Secrets.Configuration;
using Microsoft.Extensions.Options;

namespace Explore.Secrets.Validation;

/// <summary>
/// Validates <see cref="SecretProviderOptions"/> configuration.
/// Ensures required settings are present based on the selected provider type.
/// </summary>
public sealed class SecretProviderOptionsValidator : IValidateOptions<SecretProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, SecretProviderOptions options)
    {
        var errors = new List<string>();

        switch (options.Provider)
        {
            case SecretProviderType.Environment:
            case SecretProviderType.UserSecrets:
                // No validation needed for environment-only mode
                break;

            case SecretProviderType.Infisical:
                ValidateInfisicalOptions(options.Infisical, errors);
                break;

            default:
                errors.Add("Secret provider must explicitly be Environment, Infisical, or UserSecrets.");
                break;
        }

        return errors.Count > 0
            ? ValidateOptionsResult.Fail(errors)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateInfisicalOptions(InfisicalOptions options, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(options.Url))
            errors.Add("secret_authority_url_required");

        if (string.IsNullOrWhiteSpace(options.ProjectId))
            errors.Add("secret_authority_project_required");

        if (string.IsNullOrWhiteSpace(options.ClientId))
            errors.Add("secret_authority_client_required");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            errors.Add("secret_authority_credential_required");

        if (string.IsNullOrWhiteSpace(options.Environment))
            errors.Add("secret_authority_environment_required");

        if (!Uri.TryCreate(options.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            errors.Add("secret_authority_url_invalid");
        }
    }

}
