using Explore.Application.DTOs.RegistrationForms;
using Explore.Domain;

namespace Explore.Application.Features.RegistrationForms;

internal static class RegistrationFormTemplateMapper
{
    public static RegistrationFormTemplateDto ToDto(RegistrationFormTemplate template) => new(
        template.Id,
        template.TenantId,
        template.IsPlatformOwned,
        template.Name,
        template.Description,
        template.Category,
        template.PackKey,
        template.SourceEventId,
        template.SourceRegistrationFormId,
        template.SourceRegistrationFormVersionId,
        template.ConcurrencyStamp);
}
