using Microsoft.Extensions.Configuration;
using SecretKit.Core;
using SecretKit.Infisical;
using System.Globalization;

namespace Explore.ServiceDefaults.Secrets;

public static class SecretAuthorityBootstrap
{
    public static async ValueTask<SecretAuthorityAcquisition> OpenAsync(
        IConfiguration bootstrap,
        string environmentName,
        SecretConsumerRole consumer,
        IEnumerable<SecretAcquisitionEntry> entries,
        SecretAuthorityLoginAttempt loginAttempt,
        IConfiguration? testingAuthority = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(loginAttempt);
        ArgumentNullException.ThrowIfNull(entries);
        var selection = SecretAuthoritySelection.Select(bootstrap, environmentName, consumer);
        SecretAcquisitionEntry[] requested = entries.ToArray();
        SecretAcquisitionPolicy.Admit(selection, requested);
        if (testingAuthority is not null
            && !string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("secret_authority_local_source_invalid");
        if (selection.Provider != SecretAuthorityKind.Infisical)
        {
            var local = SecretAuthorityAcquisition.OpenLocal(selection, testingAuthority);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                return local;
            }
            catch
            {
                await local.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        if (testingAuthority is not null)
            throw new InvalidOperationException("secret_authority_local_source_invalid");

        // Machine credentials are process inputs, never values overlaid by a vault
        // or an application's JSON configuration. Testing uses an explicit fake root.
        using var environment = string.Equals(environmentName, "Testing", StringComparison.OrdinalIgnoreCase)
            ? null
            : (ConfigurationRoot)new ConfigurationBuilder().AddEnvironmentVariables().Build();
        IConfiguration authority = environment ?? bootstrap;
        string Required(string name, string flatName)
        {
            string? value = authority[$"SecretProvider:Infisical:{name}"] ?? authority[flatName];
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("secret_authority_bootstrap_invalid");
            return value;
        }
        const string prefix = "SecretProvider:Infisical:";
        string? lifetimeName = bootstrap[prefix + "TokenLifetime"];
        if (lifetimeName is null
            || !Enum.GetNames<InfisicalTokenLifetime>().Contains(lifetimeName, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse(lifetimeName, true, out InfisicalTokenLifetime lifetime))
            throw new InvalidOperationException("secret_authority_token_policy_invalid");
        TimeSpan Seconds(string name, double? fallback = null)
        {
            string? raw = bootstrap[prefix + name];
            double value;
            if (raw is null && fallback.HasValue)
                value = fallback.Value;
            else if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                throw new InvalidOperationException("secret_authority_token_policy_invalid");
            if (!double.IsFinite(value) || value <= 0 || value > 86400)
                throw new InvalidOperationException("secret_authority_token_policy_invalid");
            return TimeSpan.FromSeconds(value);
        }
        TimeSpan? period = lifetime == InfisicalTokenLifetime.Periodic
            ? Seconds("TokenPeriodSeconds")
            : null;
        if (lifetime == InfisicalTokenLifetime.Finite
            && !string.IsNullOrWhiteSpace(bootstrap[prefix + "TokenPeriodSeconds"]))
            throw new InvalidOperationException("secret_authority_token_policy_invalid");
        if (!Uri.TryCreate(Required("Url", "INFISICAL_URL"), UriKind.Absolute, out Uri? origin))
            throw new InvalidOperationException("secret_authority_bootstrap_invalid");
        var options = new InfisicalAuthorityOptions(origin,
            Required("ProjectId", "INFISICAL_PROJECT_ID"),
            Required("Environment", "INFISICAL_ENV"),
            Required("ClientId", "INFISICAL_CLIENT_ID"),
            lifetime, period, Seconds("RenewBeforeSeconds", 60), Seconds("RequestTimeoutSeconds", 15));
        using var secret = SecretMaterial.FromText(Required("ClientSecret", "INFISICAL_CLIENT_SECRET"));
        var opened = await SecretAuthorityAcquisition.OpenInfisicalAsync(
            selection, requested, options, secret, loginAttempt,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!opened.IsSuccess)
            throw new InvalidOperationException("secret_authority_unavailable");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return opened.Value;
        }
        catch
        {
            await opened.Value.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
