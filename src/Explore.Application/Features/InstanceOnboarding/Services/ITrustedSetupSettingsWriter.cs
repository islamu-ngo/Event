using System.Collections.Immutable;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Models;
using Explore.Domain;

namespace Explore.Application.Features.InstanceOnboarding.Services;

/// <summary>
/// Executes only coordinator-created setup effects inside the original enrollment transaction.
/// Neither a public settings entity nor a caller-supplied callback can construct this capability.
/// </summary>
public interface ITrustedSetupSettingsWriter
{
    Task<T> ExecuteAsync<T>(TrustedSetupSettingsApply<T> apply, CancellationToken cancellationToken);
}

/// <summary>Fixed default-tenant effects available only to the original completion continuation.</summary>
public interface ITrustedSetupCompletionEffects
{
    Task<Guid> EnsureDefaultTenantAsync(CancellationToken cancellationToken);
    Task EnsureDefaultTenantAdministratorAsync(CancellationToken cancellationToken);
}

public interface ITrustedSetupSettingsAuthority
{
    Task<ITrustedSetupSettingsAuthorityLease> CaptureAsync(
        TrustedSetupSettingsSource source, CancellationToken cancellationToken);
}

public interface ITrustedSetupSettingsAuthorityLease
{
    Task RequireCurrentAsync(CancellationToken cancellationToken);
    void RequireCurrentTime();
}

/// <summary>Original setup/enrollment provenance, not a human administrator grant.</summary>
public sealed class TrustedSetupSettingsSource
{
    internal TrustedSetupSettingsSource(ProviderAccountKey? account,
        ConfiguredAdministratorBootstrapBinding? binding, LocalCredentialProvisioningSnapshot? local,
        ILocalBootstrapReceiptLease? localReceiptLease = null)
    {
        Account = account;
        ConfiguredAccount = binding?.AccountKey;
        ConfiguredGeneration = binding?.Generation;
        ConfiguredFingerprint = binding?.IdentityFingerprint;
        ConfiguredSettings = binding is null ? null : JsonSerializer.Serialize(binding.Settings);
        ConfiguredProfile = binding?.AdministratorProfile;
        Local = local;
        LocalReceiptLease = localReceiptLease;
    }

    public ProviderAccountKey? Account { get; }
    public ProviderAccountKey? ConfiguredAccount { get; }
    public long? ConfiguredGeneration { get; }
    public string? ConfiguredFingerprint { get; }
    public string? ConfiguredSettings { get; }
    public ConfiguredAdministratorProfile? ConfiguredProfile { get; }
    public LocalCredentialProvisioningSnapshot? Local { get; }
    public ILocalBootstrapReceiptLease? LocalReceiptLease { get; }
    public override string ToString() => nameof(TrustedSetupSettingsSource);
}

/// <summary>Immutable storage snapshot; mutable repository entities never carry admission.</summary>
public sealed class TrustedSetupSettingEffect
{
    internal TrustedSetupSettingEffect(SystemSetting setting)
    {
        Id = setting.Id == Guid.Empty ? Guid.CreateVersion7() : setting.Id;
        Key = setting.SettingKey;
        Value = setting.Value;
        ValueType = setting.ValueType;
        IsLocked = setting.IsLocked;
        AllowedValues = setting.AllowedValues;
        Description = setting.Description;
        Category = setting.Category;
        DisplayOrder = setting.DisplayOrder;
        CreatedAt = setting.CreatedAt;
        UpdatedAt = setting.UpdatedAt;
    }

    public Guid Id { get; }
    public string Key { get; }
    public string Value { get; }
    public SettingValueType ValueType { get; }
    public bool IsLocked { get; }
    public string? AllowedValues { get; }
    public string? Description { get; }
    public string? Category { get; }
    public int DisplayOrder { get; }
    public DateTime CreatedAt { get; }
    public DateTime? UpdatedAt { get; }
    public override string ToString() => nameof(TrustedSetupSettingEffect);
}

/// <summary>
/// The completion/profile coordinator fixes both effects and continuation after its original
/// pending-generation admission. The writer must verify the terminal predicate before commit.
/// </summary>
public sealed class TrustedSetupSettingsApply<T> : IDisposable
{
    private readonly Func<ITrustedSetupCompletionEffects, CancellationToken, Task<T>> complete;
    private readonly Func<CancellationToken, Task> requireTerminal;
    private int started;
    private int completed;
    private int closed;

    internal TrustedSetupSettingsApply(IUnitOfWork originalOwner, TrustedSetupSettingsSource source,
        IEnumerable<SystemSetting> settings, Func<ITrustedSetupCompletionEffects, CancellationToken, Task<T>> complete,
        Func<CancellationToken, Task> requireTerminal, TenantCreationRequest? defaultTenantCreation = null)
    {
        OriginalOwner = originalOwner;
        Source = source;
        Effects = settings.Select(setting => new TrustedSetupSettingEffect(setting)).ToImmutableArray();
        this.complete = complete;
        this.requireTerminal = requireTerminal;
        DefaultTenantCreation = defaultTenantCreation;
    }

    public TrustedSetupSettingsSource Source { get; }
    public IUnitOfWork OriginalOwner { get; }
    public ImmutableArray<TrustedSetupSettingEffect> Effects { get; }
    public TenantCreationRequest? DefaultTenantCreation { get; }

    public void ClaimOnce()
    {
        RequireActive();
        if (Interlocked.Exchange(ref started, 1) != 0)
            throw new UnauthorizedAccessException("A setup effect capsule cannot be replayed.");
    }

    public void RequireActive()
    {
        if (Volatile.Read(ref closed) != 0)
            throw new UnauthorizedAccessException("The original setup producer has ended.");
    }

    public Task<T> CompleteAsync(ITrustedSetupCompletionEffects effects, CancellationToken cancellationToken)
    {
        RequireActive();
        if (Volatile.Read(ref started) != 1 || Interlocked.Exchange(ref completed, 1) != 0)
            throw new UnauthorizedAccessException("The original setup continuation can execute only once.");
        return complete(effects, cancellationToken);
    }

    public void Dispose() => Interlocked.Exchange(ref closed, 1);

    public Task RequireTerminalAsync(CancellationToken cancellationToken) => requireTerminal(cancellationToken);
    public override string ToString() => nameof(TrustedSetupSettingsApply<T>);
}
