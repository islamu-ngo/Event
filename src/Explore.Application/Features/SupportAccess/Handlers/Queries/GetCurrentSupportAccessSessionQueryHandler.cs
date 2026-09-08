using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.SupportAccess;
using Explore.Application.Features.SupportAccess;
using Explore.Application.Features.SupportAccess.Requests.Queries;
using Explore.Application.Settings;
using Explore.Application.Settings.Groups;
using Explore.Domain.Enums;
using MediatR;

namespace Explore.Application.Features.SupportAccess.Handlers.Queries;

public sealed class GetCurrentSupportAccessSessionQueryHandler(
    ISupportAccessSessionService supportAccessSessionService,
    IAdminContext adminContext,
    IHierarchicalSettingsResolver settingsResolver,
    ISupportAccessSessionRepository sessionRepository)
    : IRequestHandler<GetCurrentSupportAccessSessionQuery, CurrentSupportAccessSessionDto>
{
    public async Task<CurrentSupportAccessSessionDto> Handle(
        GetCurrentSupportAccessSessionQuery request,
        CancellationToken cancellationToken)
    {
        var context = await supportAccessSessionService.GetCurrentAsync(cancellationToken);
        if (!context.IsActive || !context.SessionId.HasValue)
        {
            return await DiscoverActiveSessionAsync(cancellationToken);
        }

        return new CurrentSupportAccessSessionDto
        {
            IsActive = true,
            Session = new SupportAccessSessionDto
            {
                Id = context.SessionId.Value,
                ActorUserId = context.ActorUserId ?? Guid.Empty,
                TargetTenantId = context.TargetTenantId ?? Guid.Empty,
                TargetTenantUserId = context.TargetTenantUserId,
                StatusId = (int)SupportAccessSessionStatusEnum.Active,
                StatusName = SupportAccessSessionStatusEnum.Active.ToString(),
                ModeId = (int)(context.Mode ?? 0),
                ModeName = context.Mode?.ToString() ?? string.Empty,
                AllowsWrites = context.AllowsWrites,
                ReasonCode = context.ReasonCode ?? string.Empty,
                TicketReference = context.TicketReference ?? string.Empty,
                StartedAtUtc = context.StartedAtUtc ?? DateTimeOffset.MinValue,
                ExpiresAtUtc = context.ExpiresAtUtc ?? DateTimeOffset.MinValue,
                IsActive = true
            }
        };
    }

    private async Task<CurrentSupportAccessSessionDto> DiscoverActiveSessionAsync(
        CancellationToken cancellationToken)
    {
        var actorUserId = await adminContext.ResolveUserIdAsync(cancellationToken);
        if (!actorUserId.HasValue)
        {
            return new CurrentSupportAccessSessionDto();
        }

        var settings = await settingsResolver.ResolveGroupAsync<SupportAccessSettingGroup>(
            new SettingContext(),
            cancellationToken);
        if (!settings.Enabled)
        {
            return new CurrentSupportAccessSessionDto();
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var session = await sessionRepository.GetActiveForActorAsync(actorUserId.Value, nowUtc, cancellationToken);
        if (session is null)
        {
            return new CurrentSupportAccessSessionDto();
        }

        if ((SupportAccessModeEnum)session.ModeId == SupportAccessModeEnum.Write && !settings.AllowWriteMode)
        {
            return new CurrentSupportAccessSessionDto();
        }

        return new CurrentSupportAccessSessionDto
        {
            IsActive = true,
            Session = SupportAccessMapper.ToDto(session, nowUtc)
        };
    }
}
