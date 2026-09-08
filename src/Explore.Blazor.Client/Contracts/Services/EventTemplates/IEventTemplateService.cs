using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventTemplates;

public interface IEventTemplateService
{
    Task<HalCollectionResourceOfEventTemplateListDto> GetTemplatesAsync(int? eventTypeId = null, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default);
    Task<HalResourceOfEventTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);
    Task<BaseCommandResponseOfGuid?> CreateTemplateAsync(CreateEventTemplateDto dto, CancellationToken ct = default);
    Task<BaseCommandResponseOfGuid?> UpdateTemplateAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventTemplateDto dto, CancellationToken ct = default);
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}
