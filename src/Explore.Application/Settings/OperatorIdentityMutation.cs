using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Application;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Domain;

namespace Explore.Application.Settings;

public interface IOperatorIdentityMutationAuthority
{
    Task<IOperatorIdentityMutationSource> CaptureAsync(SaveInstanceOperatorIdentityCommand command,
        CancellationToken cancellationToken);
    Task<IOperatorIdentityMutationSource> CaptureAsync(ImportInstanceOperatorIdentityCommand command,
        Guid actor, CancellationToken cancellationToken);
    void RequireIssued(IOperatorIdentityMutationSource source);
}

/// <summary>
/// Original authenticated request, including the actual setup branch when selected.
/// Only a genuine setup-secret ticket has no human actor; a human with a setup header
/// retains the same principal, credential lifetime and current human source admission.
/// </summary>
public interface IOperatorIdentityMutationSource
{
    object OriginalCommand { get; }
    bool IsSetup { get; }
    bool IsHuman { get; }
    Guid? Actor { get; }
    Task RequireSourcesAsync(CancellationToken cancellationToken);
    void RequireTime();
}

public interface IOperatorIdentitySettingsWriter
{
    DateTime NormalizeEffectTimestamp(DateTime timestamp);
    Task<string?> NormalizeAuditPayloadAsync(OperatorIdentityImportAudit? audit, CancellationToken cancellationToken);
    Task<OperatorIdentitySettingSnapshot?> ObserveAsync(IOperatorIdentityMutationSource source,
        IUnitOfWork originalOwner, CancellationToken cancellationToken);
    Task ExecuteAsync(OperatorIdentityMutation mutation, CancellationToken cancellationToken);
}

/// <summary>All scalar source and audit columns, without a tracked entity or navigation.</summary>
public sealed record OperatorIdentitySettingSnapshot(Guid Id, string Value, SettingValueType ValueType,
    bool IsLocked, string? AllowedValues, string? Description, string? Category, int DisplayOrder,
    DateTime CreatedAt, Guid? CreatedBy, DateTime? UpdatedAt, Guid? UpdatedBy)
{
    public static OperatorIdentitySettingSnapshot From(SystemSetting row) => new(row.Id, row.Value, row.ValueType,
        row.IsLocked, row.AllowedValues, row.Description, row.Category, row.DisplayOrder,
        row.CreatedAt, row.CreatedBy, row.UpdatedAt, row.UpdatedBy);
}

/// <summary>
/// One exact operator edit, compiled by the identity service before human preparation.
/// It cannot be created by a controller, replayed, used after settlement, or expanded into
/// another settings family. The bootstrap seed never uses this editor capability.
/// </summary>
public sealed class OperatorIdentityMutation : IDisposable
{
    private int claimed;
    private bool active;
    private bool disposed;

    internal OperatorIdentityMutation(IOperatorIdentityMutationSource source, IUnitOfWork originalOwner,
        OperatorIdentitySettingSnapshot? before, OperatorIdentitySettingSnapshot after,
        Guid auditId, OperatorIdentityImportAudit? audit, string? auditPayload, DateTime occurredAt)
    {
        Source = source;
        OriginalOwner = originalOwner;
        Before = before;
        After = after;
        AuditId = auditId;
        Audit = audit;
        AuditPayload = auditPayload;
        OccurredAt = occurredAt;
    }

    public IOperatorIdentityMutationSource Source { get; }
    public object OriginalCommand => Source.OriginalCommand;
    public IUnitOfWork OriginalOwner { get; }
    public Guid? Actor => Source.Actor;
    public OperatorIdentitySettingSnapshot? Before { get; }
    public OperatorIdentitySettingSnapshot After { get; }
    public Guid AuditId { get; }
    public OperatorIdentityImportAudit? Audit { get; }
    public string? AuditPayload { get; }
    public DateTime OccurredAt { get; }

    internal void Activate()
    {
        if (active || disposed || Volatile.Read(ref claimed) != 0)
            throw new UnauthorizedAccessException("An operator identity mutation belongs to one attempt.");
        active = true;
    }

    public void ClaimOnce()
    {
        RequireActive();
        if (Interlocked.Exchange(ref claimed, 1) != 0)
            throw new UnauthorizedAccessException("An operator identity mutation cannot be replayed.");
    }

    public void RequireActive()
    {
        if (!active || disposed)
            throw new UnauthorizedAccessException("The original operator identity editor is not active.");
    }

    public void Dispose() { disposed = true; active = false; }
    public override string ToString() => nameof(OperatorIdentityMutation);
}
