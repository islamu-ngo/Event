using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Contracts.Services.InstanceAdmin;

public interface IInstancePlanCatalogService
{
    Task<HalCollectionResourceOfInstanceTenantPlanListItemDto> GetPlansAsync(
        CancellationToken cancellationToken = default);

    Task<HalResourceOfInstanceTenantPlanDetailDto> GetPlanAsync(
        string key,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> CreatePlanDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> CreateVersionDraftAsync(
        string key,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> UpdateVersionDraftAsync(
        Guid versionId,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> PublishVersionAsync(
        Guid versionId,
        int existingTenantPolicy,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> ArchiveVersionAsync(
        Guid versionId,
        CancellationToken cancellationToken = default);

    Task<BaseCommandResponseOfGuid> ClonePlanAsync(
        Guid sourceVersionId,
        string key,
        string name,
        CancellationToken cancellationToken = default);

    Task<TenantPlanValidationResult> ValidateDraftAsync(
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default);

    Task<TenantPlanDiffResult> PreviewDiffAsync(
        TenantPlanEffectiveConfiguration current,
        TenantPlanDraft draft,
        CancellationToken cancellationToken = default);
}
