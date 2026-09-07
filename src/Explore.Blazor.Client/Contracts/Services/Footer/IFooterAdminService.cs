using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.Footer;

public interface IFooterAdminService
{
    Task<HalResourceOfTenantFooterSettingsDto?> GetTenantFooterSettingsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FooterLinkGroupListDto>> GetLinkGroupsAsync(CancellationToken cancellationToken = default);
    Task<FooterLinkGroupDetailsDto?> GetLinkGroupAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateLinkGroupAsync(CreateFooterLinkGroupRequest request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateLinkGroupAsync(Guid id, PatchFooterLinkGroupDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteLinkGroupAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> ReorderLinkGroupsAsync(IEnumerable<Guid> orderedIds, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> CreateLinkAsync(Guid groupId, CreateFooterLinkRequest request, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> UpdateLinkAsync(Guid id, PatchFooterLinkDto request, CancellationToken cancellationToken = default);
    Task<bool> DeleteLinkAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseCommandResponseOfGuid> PatchTenantFooterSettingsAsync(PatchTenantFooterSettingsDto request, CancellationToken cancellationToken = default);
}
