using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.EventSessionTemplates;

public interface IEventSessionTemplateService
{
    Task<HalCollectionResourceOfEventSessionTemplateListDto> GetTemplatesAsync(Guid? eventTemplateId = null, int pageNumber = 1, int pageSize = 20, CancellationToken ct = default);
    Task<HalResourceOfEventSessionTemplateDto?> GetTemplateByIdAsync(Guid id, CancellationToken ct = default);
    Task<BaseCommandResponseOfGuid?> CreateTemplateAsync(CreateEventSessionTemplateDto dto, CancellationToken ct = default);
    Task<BaseCommandResponseOfGuid?> UpdateTemplateAsync(Guid id, Guid expectedConcurrencyStamp, UpdateEventSessionTemplateDto dto, CancellationToken ct = default);
    Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct = default);
}
