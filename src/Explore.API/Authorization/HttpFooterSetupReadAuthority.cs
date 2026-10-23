using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;

namespace Explore.API.Authorization;

public static class FooterSetupReadRegistration
{
    public static IServiceCollection AddFooterSetupReads(this IServiceCollection services)
    {
        services.AddScoped<IFooterSetupReadAuthority, HttpFooterSetupReadAuthority>();
        return services;
    }
}

/// <summary>Retains the existing active setup-secret authority, never a request-body claim.</summary>
public sealed class HttpFooterSetupReadAuthority(
    IHttpContextAccessor accessor, ISetupSecretProvider secrets) : IFooterSetupReadAuthority
{
    public async Task<IFooterSetupReadAdmission?> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        var http = accessor.HttpContext;
        if (http is null || !http.User.Identities.Any(identity => identity.IsAuthenticated)
            || !http.Request.Headers.TryGetValue("X-Setup-Secret", out var values) || values.Count != 1)
            return null;
        string? secret = values[0];
        if (await secrets.ValidateSecretAsync(secret, cancellationToken) != SetupSecretValidationOutcome.Accepted)
            return null;
        var principal = http.User;
        return new Admission(async token =>
        {
            if (!ReferenceEquals(accessor.HttpContext, http) || !ReferenceEquals(http.User, principal)
                || !principal.Identities.Any(identity => identity.IsAuthenticated)
                || !http.Request.Headers.TryGetValue("X-Setup-Secret", out var current) || current.Count != 1
                || !string.Equals(current[0], secret, StringComparison.Ordinal)
                || await secrets.ValidateSecretAsync(secret, token) != SetupSecretValidationOutcome.Accepted)
                throw new UnauthorizedAccessException("The original setup authority is no longer valid.");
        });
    }

    private sealed class Admission(Func<CancellationToken, Task> validate) : IFooterSetupReadAdmission
    {
        public Task ValidateAsync(CancellationToken cancellationToken = default) => validate(cancellationToken);
    }
}
