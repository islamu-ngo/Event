namespace Explore.Application.DTOs.RegistrationForms;

public sealed record RegistrationFormTemplateDto(
    Guid Id,
    Guid? TenantId,
    bool IsPlatformOwned,
    string Name,
    string Description,
    string Category,
    string? PackKey,
    Guid SourceEventId,
    Guid SourceRegistrationFormId,
    Guid SourceRegistrationFormVersionId,
    Guid ConcurrencyStamp);

public sealed record RegistrationFormTemplateInputDto(
    string Name,
    string Description,
    string Category,
    string? PackKey,
    Guid SourceEventId,
    Guid SourceRegistrationFormId,
    Guid SourceRegistrationFormVersionId,
    bool IsPlatformOwned);

public sealed record InstantiateRegistrationFormTemplateInputDto(
    Guid EventId,
    Guid WorkflowId,
    string Namespace,
    string Key,
    string Name,
    Guid ExpectedWorkflowConcurrencyStamp);
