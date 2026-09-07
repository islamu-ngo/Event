using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Events;

public interface IRegistrationFormAuthoringService
{
    Task<HalResourceOfRegistrationWorkflowDto> GetWorkflowAsync(Guid eventId, CancellationToken cancellationToken = default);
    Task<HalCollectionResourceOfRegistrationFormTemplateDto> GetTemplatesAsync(CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationFormDto> GetFormAsync(Guid eventId, Guid formId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationFormVersionDto> GetVersionAsync(Guid eventId, Guid formId, Guid versionId, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationAnswerAnalyticsDto> GetAnalyticsAsync(Guid eventId, Guid formId, Guid formVersionId, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> CreateFormAsync(Guid eventId, Guid workflowId, Guid concurrencyStamp, RegistrationFormInput input, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> InstantiateTemplateAsync(Guid templateId, InstantiateRegistrationFormTemplateInputDto input, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> CreateVersionAsync(Guid eventId, Guid formId, Guid concurrencyStamp, RegistrationFormVersionInput input, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> AddSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid concurrencyStamp, RegistrationFormSectionInput input, HalLink link, CancellationToken cancellationToken = default);
    Task UpdateSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid concurrencyStamp, RegistrationFormSectionInput input, HalLink link, CancellationToken cancellationToken = default);
    Task DeleteSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid concurrencyStamp, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> AddFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid concurrencyStamp, RegistrationFormFieldCreateInput input, HalLink link, CancellationToken cancellationToken = default);
    Task UpdateFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, RegistrationFormFieldUpdateInput input, HalLink link, CancellationToken cancellationToken = default);
    Task DeleteFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> AddOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, RegistrationFormOptionInput input, HalLink link, CancellationToken cancellationToken = default);
    Task UpdateOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormFieldOptionDto option, RegistrationFormOptionInput input, HalLink link, CancellationToken cancellationToken = default);
    Task RetireOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormFieldOptionDto option, HalLink link, CancellationToken cancellationToken = default);
    Task<Guid> AddRuleAsync(Guid eventId, Guid formId, Guid versionId, Guid concurrencyStamp, RegistrationFormRuleInput input, HalLink link, CancellationToken cancellationToken = default);
    Task UpdateRuleAsync(Guid eventId, Guid formId, Guid versionId, RegistrationFormRuleDto rule, RegistrationFormRuleInput input, HalLink link, CancellationToken cancellationToken = default);
    Task DeleteRuleAsync(Guid eventId, Guid formId, Guid versionId, RegistrationFormRuleDto rule, HalLink link, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationFormVersionDto> ReorderSectionsAsync(Guid eventId, Guid formId, Guid versionId, Guid concurrencyStamp, IReadOnlyList<Guid> orderedIds, HalLink link, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationFormVersionDto> ReorderFieldsAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid concurrencyStamp, IReadOnlyList<Guid> orderedIds, HalLink link, CancellationToken cancellationToken = default);
    Task<HalResourceOfRegistrationFormPublishPreflightDto> PreflightAsync(Guid eventId, Guid formId, Guid versionId, HalLink link, CancellationToken cancellationToken = default);
    Task PublishAsync(Guid eventId, Guid formId, Guid versionId, Guid concurrencyStamp, HalLink link, CancellationToken cancellationToken = default);
}
