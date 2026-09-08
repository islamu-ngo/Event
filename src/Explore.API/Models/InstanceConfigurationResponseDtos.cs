namespace Explore.API.Models;

public sealed record SetupSecretValidationResultDto(bool Valid);

public sealed record SmtpConnectionTestResultDto(bool Success, string Message);

public sealed record ProviderConfigurationStatusDto(
    bool Configured,
    bool AuthorizationProviderManagedByDeployment = false,
    string? AuthorizationProviderBootstrapStatus = null);
