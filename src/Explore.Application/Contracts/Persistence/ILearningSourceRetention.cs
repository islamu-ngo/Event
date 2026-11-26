using System.Collections.Immutable;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Settings.Groups;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Retains only the original selected tenant's guidance and workspace sources.
/// Observations and leases belong to their scoped issuer and grant no settings mutation authority.
/// </summary>
public interface ILearningSourceRetention
{
    Task<ILearningSourceObservation> ObserveAsync(CancellationToken cancellationToken);
    Task<ILearningSourceLease> AcquireAsync(
        ILearningSourceObservation observation, CancellationToken cancellationToken);
}

public interface ILearningSourceObservation
{
    LearningSourceFacts Facts { get; }
}

public interface ILearningSourceLease
{
    LearningSourceFacts Facts { get; }
    Task ValidateAsync(CancellationToken cancellationToken);
    void RequireCurrentAt(DateTimeOffset utcNow);
}

/// <summary>
/// Detached immutable evidence. Fingerprints identify observed sources; only the
/// issuing retention lease establishes their continued original-owner custody.
/// </summary>
public sealed record LearningSourceFacts
{
    public LearningSourceFacts(
        Guid tenantId,
        string workspaceSourceStamp,
        string policySourceStamp,
        bool personalCreation,
        bool allowTenantEdits,
        LearningGuidanceDocumentSource? guidanceDocument,
        IReadOnlyDictionary<string, ResolvedSetting> effectiveSettings)
    {
        TenantId = tenantId;
        WorkspaceSourceStamp = workspaceSourceStamp;
        PolicySourceStamp = policySourceStamp;
        PersonalCreation = personalCreation;
        AllowTenantEdits = allowTenantEdits;
        GuidanceDocument = guidanceDocument;
        EffectiveSettings = effectiveSettings.ToImmutableDictionary(StringComparer.Ordinal);
    }

    public Guid TenantId { get; }
    public string WorkspaceSourceStamp { get; }
    public string PolicySourceStamp { get; }
    public bool PersonalCreation { get; }
    public bool AllowTenantEdits { get; }
    public LearningGuidanceDocumentSource? GuidanceDocument { get; }
    public ImmutableDictionary<string, ResolvedSetting> EffectiveSettings { get; }

    /// <summary>The mutable settings-group parser never aliases the retained image.</summary>
    public AiAssistantSettingGroup CreateAiSettings()
    {
        var group = new AiAssistantSettingGroup();
        group.Populate(EffectiveSettings);
        return group;
    }
}

/// <summary>Null document evidence means actual absence, not a created default row.</summary>
public sealed record LearningGuidanceDocumentSource(
    Guid Id,
    Guid TenantId,
    int SchemaVersion,
    string DefaultsVersion,
    string PayloadJson,
    Guid ConcurrencyStamp);
