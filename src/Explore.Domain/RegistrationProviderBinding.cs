using Explore.Domain.Enums;
using Explore.Domain.Interfaces;

namespace Explore.Domain;

public sealed class RegistrationProviderBinding : ITenantEntity, IAuditableEntity, ISoftDeletable, IConcurrencyAware
{
    private readonly List<RegistrationProviderFieldMapping> _fieldMappings = [];
    private readonly List<RegistrationProviderOptionMapping> _optionMappings = [];
    private readonly List<RegistrationProviderCapability> _capabilities = [];

    private RegistrationProviderBinding() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; set; }
    public Guid RegistrationProviderConnectionId { get; private set; }
    public RegistrationProviderConnection? Connection { get; private set; }
    public Guid RegistrationFormId { get; private set; }
    public Guid RegistrationFormVersionId { get; private set; }
    public string? ProviderSurveyId { get; private set; }
    public string? ProviderSurveyRevisionId { get; private set; }
    public string? ProviderWebhookId { get; private set; }
    public Guid? WebhookSecretBindingId { get; private set; }
    public int PresentationModeId { get; private set; }
    public int CollectionModeId { get; private set; }
    public int CompletionModeId { get; private set; }
    public int TrustLevelId { get; private set; }
    public int DriftClassId { get; private set; }
    public int StateId { get; private set; }
    public RegistrationEvidenceHash? PublishedMappingRevisionHash { get; private set; }
    public string PublishedMappingRevisionHashKey { get; private set; } = string.Empty;
    public DateTime? PublishedAt { get; private set; }
    public IReadOnlyList<RegistrationProviderFieldMapping> FieldMappings => _fieldMappings;
    public IReadOnlyList<RegistrationProviderOptionMapping> OptionMappings => _optionMappings;
    public IReadOnlyList<RegistrationProviderCapability> Capabilities => _capabilities;
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public Guid ConcurrencyStamp { get; set; }

    public static RegistrationProviderBinding Create(Guid tenantId, Guid connectionId, Guid formId, Guid formVersionId,
        RegistrationProviderPresentationModeEnum presentationMode, RegistrationProviderCollectionModeEnum collectionMode,
        RegistrationProviderCompletionModeEnum completionMode, RegistrationProviderTrustLevelEnum trustLevel,
        Guid? webhookSecretBindingId, DateTime createdAt)
    {
        if (new[] { tenantId, connectionId, formId, formVersionId }.Any(id => id == Guid.Empty) || webhookSecretBindingId == Guid.Empty ||
            !Enum.IsDefined(presentationMode) || !Enum.IsDefined(collectionMode) || !Enum.IsDefined(completionMode) || !Enum.IsDefined(trustLevel))
        {
            throw new ArgumentException("Provider binding identities and lookup values must be valid.");
        }

        return new RegistrationProviderBinding
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            RegistrationProviderConnectionId = connectionId,
            RegistrationFormId = formId,
            RegistrationFormVersionId = formVersionId,
            PresentationModeId = (int)presentationMode,
            CollectionModeId = (int)collectionMode,
            CompletionModeId = (int)completionMode,
            TrustLevelId = (int)trustLevel,
            WebhookSecretBindingId = webhookSecretBindingId,
            DriftClassId = (int)RegistrationProviderDriftClassEnum.NoDrift,
            StateId = (int)RegistrationProviderBindingStateEnum.Draft,
            CreatedAt = RegistrationProviderConnection.EnsureUtc(createdAt, nameof(createdAt))
        };
    }

    public void ReplaceDraftMappings(
        IReadOnlyList<RegistrationProviderFieldMapping> fieldMappings,
        IReadOnlyList<RegistrationProviderOptionMapping> optionMappings)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(fieldMappings);
        ArgumentNullException.ThrowIfNull(optionMappings);
        _fieldMappings.Clear();
        _optionMappings.Clear();
        foreach (RegistrationProviderFieldMapping mapping in fieldMappings) AddFieldMapping(mapping);
        foreach (RegistrationProviderOptionMapping mapping in optionMappings) AddOptionMapping(mapping);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void SetDriftClass(RegistrationProviderDriftClassEnum driftClass)
    {
        EnsureDraft();
        if (!Enum.IsDefined(driftClass)) throw new ArgumentException("Drift class must be valid.", nameof(driftClass));
        DriftClassId = (int)driftClass;
        if (BlocksPublication(driftClass)) StateId = (int)RegistrationProviderBindingStateEnum.DriftBlocked;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void AddFieldMapping(RegistrationProviderFieldMapping mapping)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.TenantId != TenantId || mapping.RegistrationProviderBindingId != Id || _fieldMappings.Any(x => !x.IsDeleted && (x.Id == mapping.Id || x.PlatformFieldKey == mapping.PlatformFieldKey)))
            throw new ArgumentException("Field mapping must be unique and owned by this binding.", nameof(mapping));
        _fieldMappings.Add(mapping);
    }

    public void AddCapability(RegistrationProviderCapability capability)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(capability);
        if (capability.TenantId != TenantId || capability.RegistrationProviderBindingId != Id || _capabilities.Any(x => !x.IsDeleted && (x.Id == capability.Id || x.TupleKey == capability.TupleKey)))
            throw new ArgumentException("Capability must be unique and owned by this binding.", nameof(capability));
        _capabilities.Add(capability);
    }

    public void ReplaceDraftCapabilities(IReadOnlyList<RegistrationProviderCapability> capabilities)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(capabilities);
        _capabilities.Clear();
        foreach (RegistrationProviderCapability capability in capabilities) AddCapability(capability);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void AddOptionMapping(RegistrationProviderOptionMapping mapping)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(mapping);
        if (mapping.TenantId != TenantId || mapping.RegistrationProviderBindingId != Id || _optionMappings.Any(x => !x.IsDeleted && x.Id == mapping.Id))
            throw new ArgumentException("Option mapping must be unique and owned by this binding.", nameof(mapping));
        _optionMappings.Add(mapping);
    }

    public void Publish(RegistrationEvidenceHash revisionHash, DateTime publishedAt)
    {
        EnsureDraft();
        ArgumentNullException.ThrowIfNull(revisionHash);
        if (BlocksPublication((RegistrationProviderDriftClassEnum)DriftClassId))
            throw new InvalidOperationException("Blocking schema drift prevents provider binding publication.");
        PublishedMappingRevisionHash = revisionHash;
        PublishedMappingRevisionHashKey = revisionHash.Value;
        PublishedAt = RegistrationProviderConnection.EnsureUtc(publishedAt, nameof(publishedAt));
        StateId = (int)RegistrationProviderBindingStateEnum.Published;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void UpdateDraft(Guid connectionId, Guid formId, Guid formVersionId,
        RegistrationProviderPresentationModeEnum presentationMode, RegistrationProviderCollectionModeEnum collectionMode,
        RegistrationProviderCompletionModeEnum completionMode, RegistrationProviderTrustLevelEnum trustLevel, Guid? webhookSecretBindingId)
    {
        EnsureDraft();
        if (new[] { connectionId, formId, formVersionId }.Any(id => id == Guid.Empty) || webhookSecretBindingId == Guid.Empty ||
            !Enum.IsDefined(presentationMode) || !Enum.IsDefined(collectionMode) || !Enum.IsDefined(completionMode) || !Enum.IsDefined(trustLevel))
        {
            throw new ArgumentException("Provider binding identities and lookup values must be valid.");
        }

        RegistrationProviderConnectionId = connectionId;
        RegistrationFormId = formId;
        RegistrationFormVersionId = formVersionId;
        PresentationModeId = (int)presentationMode;
        CollectionModeId = (int)collectionMode;
        CompletionModeId = (int)completionMode;
        TrustLevelId = (int)trustLevel;
        WebhookSecretBindingId = webhookSecretBindingId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void SetDraftProvisionedSurvey(string providerSurveyId, string? providerSurveyRevisionId)
    {
        EnsureDraft();
        ProviderSurveyId = NormalizeProviderId(providerSurveyId, nameof(providerSurveyId), 200);
        ProviderSurveyRevisionId = string.IsNullOrWhiteSpace(providerSurveyRevisionId)
            ? null
            : NormalizeProviderId(providerSurveyRevisionId, nameof(providerSurveyRevisionId), 200);
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void SetDraftProvisionedSubscription(string providerWebhookId, Guid webhookSecretBindingId)
    {
        EnsureDraft();
        if (webhookSecretBindingId == Guid.Empty) throw new ArgumentException("Webhook secret binding id must be valid.", nameof(webhookSecretBindingId));
        ProviderWebhookId = NormalizeProviderId(providerWebhookId, nameof(providerWebhookId), 200);
        WebhookSecretBindingId = webhookSecretBindingId;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void SetDraftProvisionedSubscription(string providerWebhookId)
    {
        EnsureDraft();
        ProviderWebhookId = NormalizeProviderId(providerWebhookId, nameof(providerWebhookId), 200);
        WebhookSecretBindingId = null;
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    public void Remove(DateTime removedAt)
    {
        if (StateId == (int)RegistrationProviderBindingStateEnum.Published)
        {
            throw new InvalidOperationException("Published provider bindings cannot be deleted; disable replacement behavior instead.");
        }

        IsDeleted = true;
        DeletedAt = RegistrationProviderConnection.EnsureUtc(removedAt, nameof(removedAt));
        ConcurrencyStamp = Guid.CreateVersion7();
    }

    internal void EnsureDraft()
    {
        if (StateId != (int)RegistrationProviderBindingStateEnum.Draft)
            throw new InvalidOperationException("Published or pinned provider mappings are immutable; create a new binding revision.");
    }

    private static bool BlocksPublication(RegistrationProviderDriftClassEnum driftClass) => driftClass is
        RegistrationProviderDriftClassEnum.MappingRequired or
        RegistrationProviderDriftClassEnum.RequiredFieldRemoved or
        RegistrationProviderDriftClassEnum.TypeChanged or
        RegistrationProviderDriftClassEnum.OptionSetChanged or
        RegistrationProviderDriftClassEnum.UnsupportedChange;

    private static string NormalizeProviderId(string value, string parameterName, int maxLength)
    {
        string normalized = value?.Trim() ?? string.Empty;
        return normalized.Length is > 0 && normalized.Length <= maxLength && !normalized.Any(char.IsControl)
            ? normalized
            : throw new ArgumentException($"Provider id must be non-blank and at most {maxLength} characters.", parameterName);
    }
}
