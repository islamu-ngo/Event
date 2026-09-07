using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services.Footer;
using Microsoft.Extensions.Logging;

namespace Explore.Blazor.Client.Services;

public sealed class FooterAdminService(
    IFooterClient apiClient,
    ILogger<FooterAdminService> logger) : IFooterAdminService
{
    public async Task<HalResourceOfTenantFooterSettingsDto?> GetTenantFooterSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetTenantFooterSettingsAsync(cancellationToken: cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FOOTER ADMIN] Error fetching footer settings");
            return null;
        }
    }

    public async Task<IReadOnlyList<FooterLinkGroupListDto>> GetLinkGroupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await apiClient.GetFooterLinkGroupsAsync(cancellationToken: cancellationToken);
            return groups.ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FOOTER ADMIN] Error fetching link groups");
            return [];
        }
    }

    public async Task<FooterLinkGroupDetailsDto?> GetLinkGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await apiClient.GetFooterLinkGroupByIdAsync(id, cancellationToken: cancellationToken);
        }
        catch (ApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FOOTER ADMIN] Error fetching link group {Id}", id);
            return null;
        }
    }

    public Task<BaseCommandResponseOfGuid> CreateLinkGroupAsync(
        CreateFooterLinkGroupRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.CreateFooterLinkGroupAsync(request, cancellationToken: ct),
            "create link group",
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateLinkGroupAsync(
        Guid id,
        PatchFooterLinkGroupDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.UpdateFooterLinkGroupAsync(id, request, cancellationToken: ct),
            $"update link group {id}",
            cancellationToken);

    public Task<bool> DeleteLinkGroupAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteDeleteAsync(
            ct => apiClient.DeleteFooterLinkGroupAsync(id, cancellationToken: ct),
            $"delete link group {id}",
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> ReorderLinkGroupsAsync(
        IEnumerable<Guid> orderedIds,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.ReorderFooterLinkGroupsAsync(orderedIds, cancellationToken: ct),
            "reorder link groups",
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> CreateLinkAsync(
        Guid groupId,
        CreateFooterLinkRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.CreateFooterLinkAsync(groupId, request, cancellationToken: ct),
            $"create link in group {groupId}",
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> UpdateLinkAsync(
        Guid id,
        PatchFooterLinkDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.UpdateFooterLinkAsync(id, request, cancellationToken: ct),
            $"update link {id}",
            cancellationToken);

    public Task<bool> DeleteLinkAsync(Guid id, CancellationToken cancellationToken = default) =>
        ExecuteDeleteAsync(
            ct => apiClient.DeleteFooterLinkAsync(id, cancellationToken: ct),
            $"delete link {id}",
            cancellationToken);

    public Task<BaseCommandResponseOfGuid> PatchTenantFooterSettingsAsync(
        PatchTenantFooterSettingsDto request,
        CancellationToken cancellationToken = default) =>
        ExecuteCommandAsync(
            ct => apiClient.PatchTenantFooterSettingsAsync(request, cancellationToken: ct),
            "update tenant footer settings",
            cancellationToken);

    private async Task<BaseCommandResponseOfGuid> ExecuteCommandAsync(
        Func<CancellationToken, Task<BaseCommandResponseOfGuid>> action,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ApiException ex)
        {
            logger.LogWarning(
                ex,
                "[FOOTER ADMIN] API rejected operation {Operation} with status {StatusCode}",
                operation,
                ex.StatusCode);
            return Failure($"API error ({ex.StatusCode}).");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FOOTER ADMIN] Error during operation {Operation}", operation);
            return Failure("An unexpected error occurred.");
        }
    }

    private async Task<bool> ExecuteDeleteAsync(
        Func<CancellationToken, Task<bool>> action,
        string operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[FOOTER ADMIN] Error during operation {Operation}", operation);
            return false;
        }
    }

    private static BaseCommandResponseOfGuid Failure(string message) => new()
    {
        Success = false,
        Message = message,
        Errors = [message]
    };
}
