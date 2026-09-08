using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Events;
using Explore.Blazor.Client.Pages.Studio.RegistrationForms;

namespace Explore.Blazor.Client.Services;

public sealed class RegistrationFormAuthoringService(IRegistrationFormsClient apiClient) : IRegistrationFormAuthoringService
{
    public Task<HalResourceOfRegistrationWorkflowDto> GetWorkflowAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationWorkflowAsync(eventId, "registration", cancellationToken: cancellationToken);

    public Task<HalCollectionResourceOfRegistrationFormTemplateDto> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationFormTemplatesAsync(cancellationToken: cancellationToken);

    public Task<HalResourceOfRegistrationFormDto> GetFormAsync(Guid eventId, Guid formId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationFormAsync(eventId, formId, cancellationToken: cancellationToken);

    public Task<HalResourceOfRegistrationFormVersionDto> GetVersionAsync(Guid eventId, Guid formId, Guid versionId, CancellationToken cancellationToken = default) =>
        apiClient.GetRegistrationFormVersionAsync(eventId, formId, versionId, cancellationToken: cancellationToken);

    public Task<HalResourceOfRegistrationAnswerAnalyticsDto> GetAnalyticsAsync(Guid eventId, Guid formId, Guid formVersionId, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "GET", $"/api/events/{eventId}/registration-answer-analytics");
        return apiClient.GetRegistrationAnswerAnalyticsAsync(eventId, formId, formVersionId, cancellationToken: cancellationToken);
    }

    public async Task<Guid> CreateFormAsync(Guid eventId, Guid workflowId, Guid stamp, RegistrationFormInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-workflows/{workflowId}/forms");
        return (await apiClient.CreateRegistrationFormAsync(eventId, workflowId, ETag(stamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task<Guid> InstantiateTemplateAsync(Guid templateId, InstantiateRegistrationFormTemplateInputDto input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/registration-form-templates/{templateId}/instantiate");
        return (await apiClient.InstantiateRegistrationFormTemplateAsync(templateId, input, cancellationToken: cancellationToken)).Id;
    }

    public async Task<Guid> CreateVersionAsync(Guid eventId, Guid formId, Guid stamp, RegistrationFormVersionInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions");
        return (await apiClient.CreateRegistrationFormVersionAsync(eventId, formId, ETag(stamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task<Guid> AddSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid stamp, RegistrationFormSectionInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections");
        return (await apiClient.AddRegistrationFormSectionAsync(eventId, formId, versionId, ETag(stamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task UpdateSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid stamp, RegistrationFormSectionInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PATCH", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}");
        await apiClient.UpdateRegistrationFormSectionAsync(eventId, formId, versionId, sectionId, ETag(stamp), input, cancellationToken: cancellationToken);
    }

    public async Task DeleteSectionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid stamp, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "DELETE", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}");
        await apiClient.DeleteRegistrationFormSectionAsync(eventId, formId, versionId, sectionId, ETag(stamp), cancellationToken: cancellationToken);
    }

    public async Task<Guid> AddFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid stamp, RegistrationFormFieldCreateInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields");
        return (await apiClient.AddRegistrationFormFieldAsync(eventId, formId, versionId, sectionId, ETag(stamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task UpdateFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, RegistrationFormFieldUpdateInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PATCH", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}");
        await apiClient.UpdateRegistrationFormFieldAsync(eventId, formId, versionId, sectionId, field.Id, ETag(field.ConcurrencyStamp), input, cancellationToken: cancellationToken);
    }

    public async Task DeleteFieldAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "DELETE", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}");
        await apiClient.DeleteRegistrationFormFieldAsync(eventId, formId, versionId, sectionId, field.Id, ETag(field.ConcurrencyStamp), cancellationToken: cancellationToken);
    }

    public async Task<Guid> AddOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, RegistrationFormFieldDto field, RegistrationFormOptionInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{field.Id}/options");
        return (await apiClient.AddRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, field.Id, ETag(field.ConcurrencyStamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task UpdateOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormFieldOptionDto option, RegistrationFormOptionInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PATCH", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{fieldId}/options/{option.Id}");
        await apiClient.UpdateRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, fieldId, option.Id, ETag(option.ConcurrencyStamp), input, cancellationToken: cancellationToken);
    }

    public async Task RetireOptionAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid fieldId, RegistrationFormFieldOptionDto option, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "DELETE", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/{fieldId}/options/{option.Id}");
        await apiClient.RetireRegistrationFormFieldOptionAsync(eventId, formId, versionId, sectionId, fieldId, option.Id, ETag(option.ConcurrencyStamp), cancellationToken: cancellationToken);
    }

    public async Task<Guid> AddRuleAsync(Guid eventId, Guid formId, Guid versionId, Guid stamp, RegistrationFormRuleInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules");
        return (await apiClient.AddRegistrationFormRuleAsync(eventId, formId, versionId, ETag(stamp), input, cancellationToken: cancellationToken)).Id;
    }

    public async Task UpdateRuleAsync(Guid eventId, Guid formId, Guid versionId, RegistrationFormRuleDto rule, RegistrationFormRuleInput input, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PATCH", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules/{rule.Id}");
        await apiClient.UpdateRegistrationFormRuleAsync(eventId, formId, versionId, rule.Id, ETag(rule.ConcurrencyStamp), input, cancellationToken: cancellationToken);
    }

    public async Task DeleteRuleAsync(Guid eventId, Guid formId, Guid versionId, RegistrationFormRuleDto rule, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "DELETE", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/rules/{rule.Id}");
        await apiClient.DeleteRegistrationFormRuleAsync(eventId, formId, versionId, rule.Id, ETag(rule.ConcurrencyStamp), cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationFormVersionDto> ReorderSectionsAsync(Guid eventId, Guid formId, Guid versionId, Guid stamp, IReadOnlyList<Guid> orderedIds, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PUT", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/reorder");
        return apiClient.ReorderRegistrationFormSectionsAsync(eventId, formId, versionId, ETag(stamp),
            new RegistrationFormReorderInput { OrderedIds = orderedIds.ToArray() }, cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationFormVersionDto> ReorderFieldsAsync(Guid eventId, Guid formId, Guid versionId, Guid sectionId, Guid stamp, IReadOnlyList<Guid> orderedIds, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "PUT", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/sections/{sectionId}/fields/reorder");
        return apiClient.ReorderRegistrationFormFieldsAsync(eventId, formId, versionId, sectionId, ETag(stamp),
            new RegistrationFormReorderInput { OrderedIds = orderedIds.ToArray() }, cancellationToken: cancellationToken);
    }

    public Task<HalResourceOfRegistrationFormPublishPreflightDto> PreflightAsync(Guid eventId, Guid formId, Guid versionId, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/preflight");
        return apiClient.GetRegistrationFormPublishPreflightAsync(eventId, formId, versionId, cancellationToken: cancellationToken);
    }

    public async Task PublishAsync(Guid eventId, Guid formId, Guid versionId, Guid stamp, HalLink link, CancellationToken cancellationToken = default)
    {
        RegistrationFormHal.Require(link, "POST", $"/api/events/{eventId}/registration-forms/{formId}/versions/{versionId}/publish");
        await apiClient.PublishRegistrationFormVersionAsync(eventId, formId, versionId, ETag(stamp), cancellationToken: cancellationToken);
    }

    private static string ETag(Guid stamp) => $"\"{stamp}\"";
}
