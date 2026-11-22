using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Infrastructure;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Features.Announcements;

public sealed class GetInstanceAnnouncementSourceQueryHandler(IPolicyMutationExecutionScopeFactory scopes)
    : IQueryHandler<GetInstanceAnnouncementSourceQuery, AnnouncementSourceSnapshot>
{
    public async Task<AnnouncementSourceSnapshot> QueryAsync(
        GetInstanceAnnouncementSourceQuery request, CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(PolicyMutationScope.Instance, cancellationToken);
        var row = await selected.Services.GetRequiredService<IAnnouncementSettingsReader>()
            .ReadInstanceSourceAsync(cancellationToken);
        return new(AnnouncementOwner.Instance, AnnouncementSettingValueCodec.DeserializeGroup(row?.Value), row?.IsLocked ?? false);
    }
}

public sealed class GetTenantAnnouncementSourceQueryHandler(
    IPolicyMutationExecutionScopeFactory scopes, ITenantContext tenantContext)
    : IQueryHandler<GetTenantAnnouncementSourceQuery, AnnouncementSourceSnapshot>
{
    public async Task<AnnouncementSourceSnapshot> QueryAsync(
        GetTenantAnnouncementSourceQuery request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty || request.TenantId != tenantContext.TenantId)
            throw new UnauthorizedAccessException("The source query must retain the authenticated tenant.");
        await using var selected = await scopes.CreateAsync(new(request.TenantId), cancellationToken);
        var row = await selected.Services.GetRequiredService<IAnnouncementSettingsReader>()
            .ReadTenantSourceAsync(request.TenantId, cancellationToken);
        return new(AnnouncementOwner.ForTenant(request.TenantId),
            AnnouncementSettingValueCodec.DeserializeGroup(row?.Value), row?.IsLocked ?? false);
    }
}

public sealed class GetManagedTenantAnnouncementSourceQueryHandler(IPolicyMutationExecutionScopeFactory scopes)
    : IQueryHandler<GetManagedTenantAnnouncementSourceQuery, AnnouncementSourceSnapshot>
{
    public async Task<AnnouncementSourceSnapshot> QueryAsync(
        GetManagedTenantAnnouncementSourceQuery request, CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
            throw new ArgumentException("An instance-managed source query requires an actual target tenant.", nameof(request));
        await using var selected = await scopes.CreateAsync(new(request.TenantId), cancellationToken);
        var row = await selected.Services.GetRequiredService<IAnnouncementSettingsReader>()
            .ReadManagedTenantSourceAsync(request.TenantId, cancellationToken);
        return new(AnnouncementOwner.ForTenant(request.TenantId),
            AnnouncementSettingValueCodec.DeserializeGroup(row?.Value), row?.IsLocked ?? false);
    }
}
