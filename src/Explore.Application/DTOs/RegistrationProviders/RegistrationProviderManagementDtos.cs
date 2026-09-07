namespace Explore.Application.DTOs.RegistrationProviders;

public sealed record RegistrationProviderBindingHealthDto
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid BindingId { get; init; }
    public Guid ConnectionId { get; init; }
    public string ProviderKind { get; init; } = string.Empty;
    public string BindingStatus { get; init; } = string.Empty;
    public string ConnectionValidity { get; init; } = string.Empty;
    public DateTime? LastCallbackAt { get; init; }
    public int? LastCallbackAgeSeconds { get; init; }
    public string CallbackAgeClass { get; init; } = string.Empty;
    public string DriftClass { get; init; } = string.Empty;
    public int ReconciliationLagSeconds { get; init; }
    public string ReconciliationLagClass { get; init; } = string.Empty;
    public int ParkedQueueDepth { get; init; }
    public IReadOnlyList<string> CapabilityCodes { get; init; } = [];
}

public sealed record RegistrationProviderParkedQueueItemDto
{
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid BindingId { get; init; }
    public Guid? SubmissionId { get; init; }
    public Guid? EffectOutboxId { get; init; }
    public string Status { get; init; } = string.Empty;
    public string FailureCategory { get; init; } = string.Empty;
    public IReadOnlyList<string> IssueCodes { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? NextAttemptAt { get; init; }
    public int ProcessingGeneration { get; init; }
}

public sealed record ManualRegistrationProviderImportRequestDto
{
    public Guid BindingId { get; init; }
    public string StorageReference { get; init; } = string.Empty;
    public string SourceReference { get; init; } = string.Empty;
}

public sealed record RetryRegistrationProviderParkedItemRequestDto
{
    public Guid? SubmissionId { get; init; }
    public Guid? EffectOutboxId { get; init; }
    public int? ExpectedProcessingGeneration { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed record ResolveRegistrationProviderQueueItemRequestDto
{
    public Guid? SubmissionId { get; init; }
    public Guid? EffectOutboxId { get; init; }
    public string DecisionCode { get; init; } = string.Empty;
    public string NoteReference { get; init; } = string.Empty;
}

public sealed record RegistrationProviderConnectionDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ProviderKindId { get; init; }
    public int DeploymentKindId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ProviderDeploymentCode { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = string.Empty;
    public string AdapterPolicyVersion { get; init; } = string.Empty;
    public string ConformanceEvidenceRevision { get; init; } = string.Empty;
    public string ManagementApiBaseUrl { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string ProviderWorkspaceId { get; init; } = string.Empty;
    public string GrantedOAuthScopes { get; init; } = string.Empty;
    public string ProviderIdentity { get; init; } = string.Empty;
    public string PubSubConfigurationReference { get; init; } = string.Empty;
    public DateTime? LastCredentialRefreshAt { get; init; }
    public DateTime? LastAccessValidatedAt { get; init; }
    public Guid? ApiTokenSecretBindingId { get; init; }
    public Guid? WebhookSecretBindingId { get; init; }
    public IReadOnlyList<string> ApprovedOrigins { get; init; } = [];
}

public sealed record RegistrationProviderConnectionRequestDto
{
    public string Name { get; init; } = string.Empty;
    public int ProviderKindId { get; init; }
    public int DeploymentKindId { get; init; }
    public string ProviderCode { get; init; } = string.Empty;
    public string ProviderDeploymentCode { get; init; } = string.Empty;
    public string ApiVersion { get; init; } = string.Empty;
    public string AdapterPolicyVersion { get; init; } = string.Empty;
    public string ConformanceEvidenceRevision { get; init; } = string.Empty;
    public string ManagementApiBaseUrl { get; init; } = string.Empty;
    public string PublicBaseUrl { get; init; } = string.Empty;
    public string ProviderWorkspaceId { get; init; } = string.Empty;
    public string GrantedOAuthScopes { get; init; } = string.Empty;
    public string ProviderIdentity { get; init; } = string.Empty;
    public string PubSubConfigurationReference { get; init; } = string.Empty;
    public Guid ApiTokenSecretBindingId { get; init; }
    public Guid WebhookSecretBindingId { get; init; }
}

public sealed record ReplaceRegistrationProviderApprovedOriginsRequestDto
{
    public IReadOnlyList<string> Origins { get; init; } = [];
}

public sealed record RegistrationProviderBindingDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid ConnectionId { get; init; }
    public Guid FormId { get; init; }
    public Guid FormVersionId { get; init; }
    public string? ProviderSurveyId { get; init; }
    public string? ProviderSurveyRevisionId { get; init; }
    public string? ProviderWebhookId { get; init; }
    public Guid? WebhookSecretBindingId { get; init; }
    public int PresentationModeId { get; init; }
    public int CollectionModeId { get; init; }
    public int CompletionModeId { get; init; }
    public int TrustLevelId { get; init; }
    public int DriftClassId { get; init; }
    public int StateId { get; init; }
    public DateTime? PublishedAt { get; init; }
    public IReadOnlyList<string> CapabilityCodes { get; init; } = [];
    public IReadOnlyList<RegistrationProviderFieldMappingDto> FieldMappings { get; init; } = [];
    public IReadOnlyList<RegistrationProviderOptionMappingDto> OptionMappings { get; init; } = [];
}

public sealed record RegistrationProviderFieldMappingDto
{
    public string PlatformFieldKey { get; init; } = string.Empty;
    public string ProviderFieldKey { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
}

public sealed record RegistrationProviderOptionMappingDto
{
    public string PlatformFieldKey { get; init; } = string.Empty;
    public string PlatformOptionKey { get; init; } = string.Empty;
    public string ProviderOptionKey { get; init; } = string.Empty;
}

public sealed record RegistrationProviderBindingRequestDto
{
    public Guid ConnectionId { get; init; }
    public Guid FormId { get; init; }
    public Guid FormVersionId { get; init; }
    public string? ProviderSurveyId { get; init; }
    public string? ProviderSurveyRevisionId { get; init; }
    public string? ProviderWebhookId { get; init; }
    public Guid? WebhookSecretBindingId { get; init; }
    public int PresentationModeId { get; init; }
    public int CollectionModeId { get; init; }
    public int CompletionModeId { get; init; }
    public int TrustLevelId { get; init; }
}

public sealed record ReplaceRegistrationProviderMappingsRequestDto
{
    public IReadOnlyList<RegistrationProviderFieldMappingDto> FieldMappings { get; init; } = [];
    public IReadOnlyList<RegistrationProviderOptionMappingDto> OptionMappings { get; init; } = [];
}

public sealed record ImportExternalRegistrationProviderFormVersionRequestDto
{
    public Guid? FormId { get; init; }
    public string Namespace { get; init; } = "external";
    public string Key { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string LanguageTag { get; init; } = "en";
    public string ProviderSurveyId { get; init; } = string.Empty;
    public string? ProviderSurveyRevisionId { get; init; }
}

public sealed record RegistrationChannelDto
{
    public Guid Id { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid RegistrationWorkflowId { get; init; }
    public Guid RegistrationRequirementId { get; init; }
    public int Ordinal { get; init; }
    public bool IsNative { get; init; }
    public Guid? RegistrationProviderBindingId { get; init; }
}

public sealed record RegistrationChannelRequestDto
{
    public int Ordinal { get; init; }
    public bool IsNative { get; init; }
    public Guid? RegistrationProviderBindingId { get; init; }
}

public sealed record RegistrationProviderLaunchDescriptorDto
{
    public Guid BindingId { get; init; }
    public Guid ChannelId { get; init; }
    public Guid TenantId { get; init; }
    public Guid EventId { get; init; }
    public Guid WorkflowId { get; init; }
    public Guid RequirementId { get; init; }
    public string Mode { get; init; } = "unavailable";
    public bool Available { get; init; }
    public string? Url { get; init; }
    public string Title { get; init; } = string.Empty;
    public bool OpenInNewTab { get; init; }
    public string FallbackMode { get; init; } = "manual";
    public string Reason { get; init; } = string.Empty;
}
