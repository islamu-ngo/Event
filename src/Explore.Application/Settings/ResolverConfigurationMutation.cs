using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.DTOs.Onboarding.Validators;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using FluentValidation;

namespace Explore.Application.Settings;

public interface IResolverConfigurationMutationAuthority
{
    Task<IResolverConfigurationMutationSource> CaptureAsync(UpdateResolverConfigurationCommand command,
        CancellationToken cancellationToken);
    void RequireIssued(IResolverConfigurationMutationSource source);
}

/// <summary>The original authenticated resolver command, never setup or a configured worker grant.</summary>
public interface IResolverConfigurationMutationSource
{
    UpdateResolverConfigurationCommand OriginalCommand { get; }
    HumanCommitSubject Subject { get; }
    HumanAuthenticationLifetime Lifetime { get; }
    Guid Actor { get; }
    Task RequireSourcesAsync(CancellationToken cancellationToken);
    void RequireTime();
}

public interface IResolverConfigurationSettingsWriter
{
    /// <summary>Represents data timestamps on the selected writer before immutable compilation.</summary>
    DateTime NormalizeEffectTimestamp(DateTime timestamp);
    Task<ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?>> ObserveAsync(
        IResolverConfigurationMutationSource source, IUnitOfWork originalOwner, CancellationToken cancellationToken);
    Task ExecuteAsync(ResolverConfigurationMutation mutation, CancellationToken cancellationToken);
    /// <summary>Final admission rejects any attempt that skipped or replaced the fixed writer.</summary>
    Task RequireAppliedAsync(ResolverConfigurationMutation mutation, CancellationToken cancellationToken);
}

/// <summary>Complete immutable scalar storage state, including canonical audit attribution.</summary>
public sealed record ResolverConfigurationSettingSnapshot(string Key, Guid Id, string Value,
    SettingValueType ValueType, bool IsLocked, string? AllowedValues, string? Description,
    string? Category, int DisplayOrder, DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    public static ResolverConfigurationSettingSnapshot From(SystemSetting row) => new(
        row.SettingKey, row.Id, row.Value, row.ValueType, row.IsLocked, row.AllowedValues,
        row.Description, row.Category, row.DisplayOrder, row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
}

/// <summary>
/// Compiles only the actual resolver PATCH's seven scalar leaves. The complete validation
/// projection and fixed effects are frozen before the original non-replaying physical attempt.
/// </summary>
public sealed class ResolverConfigurationMutation : IDisposable
{
    public static ImmutableArray<string> Keys { get; } =
    [
        GovernanceSettingKeys.Routing.ResolverHeaderEnabled,
        GovernanceSettingKeys.Routing.ResolverCustomDomainEnabled,
        GovernanceSettingKeys.Routing.ResolverSubdomainEnabled,
        GovernanceSettingKeys.Routing.ResolverPathEnabled,
        GovernanceSettingKeys.Routing.PathPrefix,
        GovernanceSettingKeys.Domains.InstanceBaseDomain,
        GovernanceSettingKeys.Domains.AllowTenantCustomDomain
    ];

    private int claimed;
    private bool disposed;

    private ResolverConfigurationMutation(IResolverConfigurationMutationSource source, IUnitOfWork owner,
        ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> before,
        ImmutableArray<ResolverConfigurationSettingSnapshot> effects)
    {
        Source = source;
        OriginalOwner = owner;
        Before = before;
        Effects = effects;
    }

    public IResolverConfigurationMutationSource Source { get; }
    public UpdateResolverConfigurationCommand OriginalCommand => Source.OriginalCommand;
    public Guid Actor => Source.Actor;
    public IUnitOfWork OriginalOwner { get; }
    public ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> Before { get; }
    public ImmutableArray<ResolverConfigurationSettingSnapshot> Effects { get; }

    public static async Task<ResolverConfigurationMutation> CompileAsync(
        UpdateResolverConfigurationCommand command, IResolverConfigurationMutationSource source,
        IUnitOfWork owner, ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> before,
        DateTime occurredAt, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(command, source.OriginalCommand) || source.Actor != command.UserId
            || source.Actor == Guid.Empty || before.Count != Keys.Length
            || Keys.Any(key => !before.ContainsKey(key) || before[key] is { } row && row.Key != key))
            throw new UnauthorizedAccessException("Resolver compilation requires its original command and complete source.");
        var patch = command.Patch;
        var configuration = Project(before);
        configuration.HeaderEnabled = patch.HeaderEnabled.HasValue ? patch.HeaderEnabled.Value : configuration.HeaderEnabled;
        configuration.CustomDomainEnabled = patch.CustomDomainEnabled.HasValue ? patch.CustomDomainEnabled.Value : configuration.CustomDomainEnabled;
        configuration.SubdomainEnabled = patch.SubdomainEnabled.HasValue ? patch.SubdomainEnabled.Value : configuration.SubdomainEnabled;
        configuration.PathEnabled = patch.PathEnabled.HasValue ? patch.PathEnabled.Value : configuration.PathEnabled;
        configuration.PathPrefix = patch.PathPrefix.HasValue ? patch.PathPrefix.Value ?? string.Empty : configuration.PathPrefix;
        configuration.InstanceBaseDomain = patch.InstanceBaseDomain.HasValue ? patch.InstanceBaseDomain.Value ?? string.Empty : configuration.InstanceBaseDomain;
        configuration.AllowTenantCustomDomains = patch.AllowTenantCustomDomains.HasValue
            ? patch.AllowTenantCustomDomains.Value : configuration.AllowTenantCustomDomains;
        await new ResolverConfigurationDtoValidator().ValidateAndThrowAsync(configuration, cancellationToken);

        var effects = ImmutableArray.CreateBuilder<ResolverConfigurationSettingSnapshot>();
        void Add(string key, string value, SettingValueType type, bool locked, string category, int order, string description)
        {
            var original = before[key];
            effects.Add(new(key, original?.Id ?? Guid.CreateVersion7(), value, type, locked, null,
                description, category, order, original?.CreatedAt ?? occurredAt, original is null ? source.Actor : original.CreatedBy,
                occurredAt, source.Actor));
        }
        if (patch.HeaderEnabled.HasValue)
            Add(Keys[0], "true", SettingValueType.Boolean, true, "Routing", 20,
                "Whether the header tenant resolver is enabled. Must remain enabled for YARP propagation.");
        if (patch.CustomDomainEnabled.HasValue)
            Add(Keys[1], JsonSerializer.Serialize(configuration.CustomDomainEnabled), SettingValueType.Boolean, false, "Routing", 21,
                "Whether the custom-domain tenant resolver is enabled.");
        if (patch.SubdomainEnabled.HasValue)
            Add(Keys[2], JsonSerializer.Serialize(configuration.SubdomainEnabled), SettingValueType.Boolean, false, "Routing", 22,
                "Whether the subdomain tenant resolver is enabled.");
        if (patch.PathEnabled.HasValue)
            Add(Keys[3], JsonSerializer.Serialize(configuration.PathEnabled), SettingValueType.Boolean, false, "Routing", 23,
                "Whether the path-based tenant resolver is enabled.");
        if (patch.PathPrefix.HasValue)
            Add(Keys[4], JsonSerializer.Serialize(NormalizePathPrefix(configuration.PathPrefix)), SettingValueType.String, false, "Routing", 24,
                "Path prefix used by the path-based tenant resolver.");
        if (patch.InstanceBaseDomain.HasValue)
            Add(Keys[5], JsonSerializer.Serialize(NormalizeHost(configuration.InstanceBaseDomain)), SettingValueType.String, false, "Domains", 1,
                "Base domain used for tenant subdomain generation.");
        if (patch.AllowTenantCustomDomains.HasValue)
            Add(Keys[6], JsonSerializer.Serialize(configuration.AllowTenantCustomDomains), SettingValueType.Boolean, false, "Domains", 2,
                "Whether tenant administrators may configure a custom domain.");
        return new(source, owner, before, effects.ToImmutable());
    }

    public void RequireActive()
    {
        if (disposed)
            throw new UnauthorizedAccessException("The original resolver mutation has ended.");
    }

    public void ClaimOnce()
    {
        RequireActive();
        if (Interlocked.Exchange(ref claimed, 1) != 0)
            throw new UnauthorizedAccessException("A resolver mutation cannot be replayed.");
    }

    public void Dispose() => disposed = true;
    public override string ToString() => nameof(ResolverConfigurationMutation);

    public static ResolverConfigurationDto Project(ImmutableDictionary<string, ResolverConfigurationSettingSnapshot?> rows) => new()
    {
        HeaderEnabled = true,
        CustomDomainEnabled = Boolean(rows[Keys[1]]?.Value, false),
        SubdomainEnabled = Boolean(rows[Keys[2]]?.Value, false),
        PathEnabled = Boolean(rows[Keys[3]]?.Value, true),
        PathPrefix = NormalizePathPrefix(Text(rows[Keys[4]]?.Value, string.Empty)),
        InstanceBaseDomain = NormalizeHost(Text(rows[Keys[5]]?.Value, string.Empty)),
        AllowTenantCustomDomains = Boolean(rows[Keys[6]]?.Value, true),
        ReservedSlugs = ReservedTenantSlugs.All.OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase).ToArray()
    };

    private static string NormalizePathPrefix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Trim();
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        return normalized.TrimEnd('/');
    }

    private static string NormalizeHost(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Trim().ToLowerInvariant()
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('/');
    }

    private static bool Boolean(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try { return JsonSerializer.Deserialize<bool>(value); }
        catch (JsonException) { return bool.TryParse(value, out bool parsed) ? parsed : fallback; }
    }

    private static string Text(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        try
        {
            string? parsed = JsonSerializer.Deserialize<string>(value);
            return string.IsNullOrWhiteSpace(parsed) ? fallback : parsed;
        }
        catch (JsonException) { return value.Trim('"'); }
    }
}
