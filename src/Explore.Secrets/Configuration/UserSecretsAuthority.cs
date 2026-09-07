using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Explore.Secrets.Configuration;

public sealed class UserSecretsAuthority
{
    private readonly IHostEnvironment _environment;
    private readonly Lazy<IConfiguration> _configuration;

    public UserSecretsAuthority(IHostEnvironment environment)
    {
        _environment = environment;
        _configuration = new(SecretAuthorityConfiguration.BuildUserSecrets);
    }

    internal UserSecretsAuthority(IHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = new(() => configuration);
    }

    public string? Get(string key)
    {
        SecretAuthorityConfiguration.EnsureUserSecretsEnvironment(_environment.EnvironmentName);
        return _configuration.Value[key];
    }

    public IEnumerable<KeyValuePair<string, string?>> GetByPrefix(string prefix)
    {
        SecretAuthorityConfiguration.EnsureUserSecretsEnvironment(_environment.EnvironmentName);
        return _configuration.Value.AsEnumerable()
            .Where(pair => pair.Value is not null
                && pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    public void EnsureAllowed() =>
        SecretAuthorityConfiguration.EnsureUserSecretsEnvironment(_environment.EnvironmentName);
}
