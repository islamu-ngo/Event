using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Secrets;
using Microsoft.Extensions.Configuration;

namespace Explore.Application.Features.InstanceOnboarding.Services;

public interface IProviderConfigurationMutationAuthority
{
    Task<IProviderConfigurationMutationSource> CaptureAsync(object command, CancellationToken token);
    void RequireIssued(IProviderConfigurationMutationSource source);
}

public interface IProviderConfigurationMutationSource
{
    object OriginalCommand { get; }
    IConfiguration Deployment { get; }
    Task<ProviderConfigurationMaterials> ObserveMaterialsAsync(ProviderConfigurationState state, CancellationToken token);
    Task RequireExternalSourcesAsync(CancellationToken token);
    Task RequireSourcesAsync(CancellationToken token);
    void RequireTime();
}

public interface IProviderConfigurationHumanSource : IProviderConfigurationMutationSource
{
    HumanCommitSubject Subject { get; }
    HumanAuthenticationLifetime Lifetime { get; }
    /// <summary>Checks the original human request without observing idle-only database sources.</summary>
    Task RequireRequestAsync(CancellationToken token);
    /// <summary>Compares fresh facts supplied by the original Settings coordinator.</summary>
    void RequireAcquiredFacts(HumanPrincipalSourceFacts principal, HumanSupportAccessSourceFacts support);
}

public interface IProviderConfigurationSetupSource : IProviderConfigurationMutationSource
{
    ITrustedSetupSettingsAuthorityLease Setup { get; }
}

public sealed record ProviderConfigurationMaterials(KeycloakConnectionResolution Keycloak, SecretResolutionResult Google,
    bool? PublicationReady = null)
{
    public override string ToString() => nameof(ProviderConfigurationMaterials);
}

/// <summary>All persisted columns, including identity and audit state, precede compilation.</summary>
public sealed record ProviderConfigurationRow(string Key, Guid Id, string Value, SettingValueType ValueType,
    bool IsLocked, string? AllowedValues, string? Description, string? Category, int DisplayOrder,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    public static ProviderConfigurationRow From(SystemSetting row) => new(row.SettingKey, row.Id,
        row.Value, row.ValueType, row.IsLocked, row.AllowedValues, row.Description, row.Category,
        row.DisplayOrder, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);

    public SystemSetting ToEntity() => new()
    {
        SettingKey = Key,
        Id = Id,
        Value = Value,
        ValueType = ValueType,
        IsLocked = IsLocked,
        AllowedValues = AllowedValues,
        Description = Description,
        Category = Category,
        DisplayOrder = DisplayOrder,
        CreatedAt = CreatedAt,
        CreatedBy = CreatedBy,
        UpdatedAt = UpdatedAt,
        UpdatedBy = UpdatedBy
    };
}

/// <summary>
/// Original configuration and participation inputs. Empty arrays preserve absence; the native
/// revision excludes insertions, deletion, replacement and changes outside the current result set.
/// </summary>
public sealed record ProviderConfigurationState(
    ImmutableDictionary<string, ProviderConfigurationRow?> System,
    string Bindings, string Tenants, string Participation, string Events, string Bootstrap,
    string Users, string Logins, long Revision)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string? NativeImage { get; init; }

    public static ImmutableArray<string> Keys { get; } =
        VisitorAccessCapabilityResolver.AuthoritySettingKeys.Concat(new[]
        {
            GovernanceSettingKeys.Security.AuthorizationProvider,
            GovernanceSettingKeys.Cerbos.GrpcEndpoint,
            GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
        }).Order(StringComparer.Ordinal).ToImmutableArray();

    public static ImmutableArray<string> SecretKeys { get; } =
    [
        SecretDefinitionRegistry.Keys.Keycloak.Endpoint, SecretDefinitionRegistry.Keys.Keycloak.Realm,
        SecretDefinitionRegistry.Keys.Keycloak.ClientId, SecretDefinitionRegistry.Keys.Keycloak.BlazorClientSecret,
        SecretDefinitionRegistry.Keys.Authentication.GoogleClientSecret
    ];

    public void RequireShape()
    {
        if (Revision < 0 || System.Count != Keys.Length
            || Keys.Any(key => !System.ContainsKey(key) || System[key] is { } row && row.Key != key))
            throw new UnauthorizedAccessException("Provider configuration requires its complete first observation.");
        foreach (string rows in new[] { Bindings, Tenants, Participation, Events, Bootstrap, Users, Logins })
            if (JsonSerializer.Deserialize<JsonElement>(rows).ValueKind != JsonValueKind.Array)
                throw new UnauthorizedAccessException("Provider source absence must be explicit.");
    }

    public bool Same(ProviderConfigurationState other) => Revision == other.Revision
        && Keys.All(key => System[key] == other.System[key])
        && SameRows(Bindings, other.Bindings) && SameRows(Tenants, other.Tenants)
        && SameRows(Participation, other.Participation) && SameRows(Events, other.Events)
        && SameRows(Bootstrap, other.Bootstrap) && SameRows(Users, other.Users) && SameRows(Logins, other.Logins);

    private static bool SameRows(string left, string right) =>
        JsonElement.DeepEquals(JsonSerializer.Deserialize<JsonElement>(left), JsonSerializer.Deserialize<JsonElement>(right));

    public bool HasOriginalUser(Guid actor) => JsonSerializer.Deserialize<JsonElement>(Users).EnumerateArray()
        .Any(row => row.GetProperty("Id").GetGuid() == actor && !row.GetProperty("IsDeleted").GetBoolean());

    public IReadOnlyCollection<UserExternalLogin> OriginalLogins(Guid actor) =>
        JsonSerializer.Deserialize<JsonElement>(Logins).EnumerateArray()
            .Where(row => row.GetProperty("UserId").GetGuid() == actor)
            .Select(row => new UserExternalLogin
            {
                Id = row.GetProperty("Id").GetGuid(),
                UserId = actor,
                User = null!,
                AuthenticationProviderId = row.GetProperty("AuthenticationProviderId").GetInt32(),
                AuthenticationProvider = null!,
                ProviderKey = row.GetProperty("ProviderKey").GetString()!
            }).ToArray();

    public override string ToString() => nameof(ProviderConfigurationState);
}
