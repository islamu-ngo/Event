using Microsoft.Extensions.Configuration;
using SecretKit.Infisical;
using System.Text.Json.Serialization;

namespace Explore.ServiceDefaults.Secrets;

public enum SecretAuthorityKind { Environment, UserSecrets, Infisical }
public enum SecretConsumerRole { Api, Bff, Combined, Migration, ModerationMeasurement }

public sealed record SecretAuthoritySelection(
    SecretAuthorityKind Provider,
    string EnvironmentName,
    SecretConsumerRole Consumer)
{
    /// <summary>Null means absent; an empty value preserves an invalid declaration for fail-closed observation.</summary>
    public Guid? AuthenticationProfileRevision { get; init; }

    public static SecretAuthoritySelection Select(
        IConfiguration bootstrap,
        string environmentName,
        SecretConsumerRole consumer)
    {
        ArgumentNullException.ThrowIfNull(bootstrap);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);
        string? external = bootstrap["SECRET_PROVIDER"];
        string? structured = bootstrap["SecretProvider:Provider"];
        if (external is not null && structured is not null
            && !string.Equals(external, structured, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("secret_authority_selection_conflict");
        string? selected = external ?? structured;
        if (selected is null
            || !Enum.GetNames<SecretAuthorityKind>().Contains(selected, StringComparer.OrdinalIgnoreCase)
            || !Enum.TryParse(selected, true, out SecretAuthorityKind provider)
            || !Enum.IsDefined(consumer))
            throw new InvalidOperationException("secret_authority_selection_invalid");
        var selection = new SecretAuthoritySelection(provider, environmentName, consumer)
        {
            AuthenticationProfileRevision = bootstrap["AUTHENTICATION_CREDENTIAL_PROFILE_REVISION"] is not { } configuredRevision
                ? null
                : Guid.TryParse(configuredRevision, out Guid revision) ? revision : Guid.Empty
        };
        selection.Validate();
        return selection;
    }

    internal void Validate()
    {
        if (!Enum.IsDefined(Provider) || !Enum.IsDefined(Consumer)
            || string.IsNullOrWhiteSpace(EnvironmentName))
            throw new InvalidOperationException("secret_authority_selection_invalid");
        if (Provider == SecretAuthorityKind.UserSecrets
            && !string.Equals(EnvironmentName, "Development", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("secret_authority_user_secrets_environment_invalid");
    }

    public override string ToString() => $"secret_authority:{Provider}:{Consumer}";
}

public sealed record SecretAcquisitionEntry(
    [property: JsonIgnore] string Folder,
    [property: JsonIgnore] string Key,
    [property: JsonIgnore] string ConfigurationKey)
{
    public override string ToString() => "secret_acquisition_entry";
}

public sealed record InfisicalAuthorityOptions(
    [property: JsonIgnore] Uri Origin,
    [property: JsonIgnore] string ProjectId,
    [property: JsonIgnore] string VaultEnvironment,
    [property: JsonIgnore] string ClientId,
    InfisicalTokenLifetime Lifetime,
    TimeSpan? Period,
    TimeSpan RenewBefore,
    TimeSpan RequestTimeout)
{
    public override string ToString() => "infisical_authority_options";
}
