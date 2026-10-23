namespace Explore.Application.Contracts.Services.Registration;

/// <summary>
/// Supplies the currently admitted account and trusted purpose/capability scope.
/// Implemented by the authenticated host, never populated from command or body fields.
/// </summary>
public interface IRegistrationCreationPrincipal
{
    RegistrationCreationPrincipal? GetCurrent();
}

public sealed record RegistrationCreationPrincipal(Guid AccountUserId, string ScopeFingerprint);
