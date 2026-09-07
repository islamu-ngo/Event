namespace Explore.Application.Contracts.Services;

public enum SetupSecretValidationOutcome
{
    Rejected,
    Accepted,
    SetupCompleted
}

/// <summary>
/// Manages the setup secret lifecycle for instance onboarding.
/// Registered as a singleton — the secret is resolved once at startup and locked after onboarding completion.
/// </summary>
public interface ISetupSecretProvider
{
    /// <summary>
    /// Asynchronously loads bootstrap state from the database so that
    /// <see cref="IsSetupModeActive"/> can return a cached value without blocking.
    /// Must be called once at startup before any request processing.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the instance has not completed onboarding and is not locked.
    /// Returns false if <see cref="InitializeAsync"/> has not been called yet.
    /// </summary>
    bool IsSetupModeActive { get; }

    /// <summary>
    /// True when interactive setup endpoints require SETUP_SECRET validation.
    /// Defaults to true and may only become false for explicitly trusted managed-provisioning deployments.
    /// </summary>
    bool IsSetupSecretRequired { get; }

    /// <summary>
    /// True when the secret was loaded from the SETUP_SECRET environment variable (vs auto-generated).
    /// </summary>
    bool IsFromEnvironmentVariable { get; }

    string? GeneratedSecretFilePath => null;

    /// <summary>
    /// Validates the provided secret against the stored secret using timing-safe comparison.
    /// Returns false if the secret is wrong, null, or setup has been locked.
    /// </summary>
    bool ValidateSecret(string? secret);

    /// <summary>
    /// Consults the durable bootstrap state before comparing a presented secret. Acceptance
    /// surfaces must use this method rather than the process-local snapshot above.
    /// </summary>
    Task<SetupSecretValidationOutcome> ValidateSecretAsync(
        string? secret,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(!IsSetupModeActive
            ? SetupSecretValidationOutcome.SetupCompleted
            : ValidateSecret(secret)
                ? SetupSecretValidationOutcome.Accepted
                : SetupSecretValidationOutcome.Rejected);

    /// <summary>
    /// Consults durable bootstrap state before setup-only work that has already authenticated
    /// the caller and therefore no longer has access to the raw secret.
    /// </summary>
    Task<bool> IsSetupModeActiveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsSetupModeActive);

    /// <summary>
    /// Transitions the provider to locked mode after onboarding completion.
    /// The lock is a process-local optimization; durable validation remains authoritative.
    /// </summary>
    void Lock();

}
