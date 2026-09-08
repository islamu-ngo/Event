using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Responses;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class StartGuestRegistrationOrderCommandHandler(
    IRegistrationOrderStarter starter)
    : IRequestHandler<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>
{
    public async Task<GuestRegistrationOrderStartDto> Handle(
        StartGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken)
    {
        var authority = request.ChallengeAuthority;
        if (authority is null || !authority.Matches(request))
        {
            return GuestRegistrationOrderStartDto.Failure(BaseCommandResponse.Failure<Guid>(
                "registration_order_challenge_invalid", "Registration challenge is invalid.", id: request.EventId));
        }
        BaseCommandResponse<Guid> response = await starter.StartAsync(new CreateRegistrationOrderWithHoldCommand
        {
            EventId = request.EventId,
            TicketCatalogVersionId = request.TicketCatalogVersionId,
            BookingPartyType = request.BookingPartyType,
            GuestAccessTokenHash = authority.GuestAccessTokenHash,
            ChallengeAuthority = authority,
            PlatformContributionBasisPoints = request.PlatformContributionBasisPoints,
            Lines = request.Lines
        }, cancellationToken);

        return response.IsSuccess
            ? GuestRegistrationOrderStartDto.Success(response.Id, response.Message, authority.GuestCapabilityToken)
            : GuestRegistrationOrderStartDto.Failure(response);
    }
}

public sealed class ContinueGuestRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<ContinueGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> Handle(
        ContinueGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken) => GuestRegistrationOrderLifecycleResponseDto.From(
        await RegistrationOrderAccessGuard.ExecuteGuestAsync(
            request,
            inventory,
            capabilities,
            tenant,
            timeProvider,
            (orderId, tenantId, token) => lifecycle.SubmitAsync(
                orderId,
                tenantId,
                request.PlatformContributionBasisPoints,
                token),
            cancellationToken));
}

public sealed class FinalizeGuestRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<FinalizeGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> Handle(
        FinalizeGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken) => GuestRegistrationOrderLifecycleResponseDto.From(
        await RegistrationOrderAccessGuard.ExecuteGuestAsync(
            request, inventory, capabilities, tenant, timeProvider, lifecycle.FinalizeFreeAsync, cancellationToken));
}

public sealed class CancelGuestRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider)
    : IRequestHandler<CancelGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> Handle(
        CancelGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken) => GuestRegistrationOrderLifecycleResponseDto.From(
        await RegistrationOrderAccessGuard.ExecuteGuestAsync(
            request, inventory, capabilities, tenant, timeProvider, lifecycle.CancelAsync, cancellationToken));
}

public sealed class LaunchGuestNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<LaunchGuestNativeRegistrationAttemptCommand, NativeRegistrationAttemptResult>
{
    public async Task<NativeRegistrationAttemptResult> Handle(
        LaunchGuestNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, Guid.Empty, request.RequirementId, request.ChannelId, request.FormId, request.FormVersionId,
                default, null, [], null, false, null, "registration_order_not_found");
        }

        return await sender.Send(new LaunchNativeRegistrationAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId,
            request.ChannelId, request.FormId, request.FormVersionId, request.BindingId,
            request.SupersededAttemptId), cancellationToken);
    }
}

public sealed class SubmitGuestNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<SubmitGuestNativeRegistrationAttemptCommand, NativeRegistrationSubmissionResult>
{
    public async Task<NativeRegistrationSubmissionResult> Handle(
        SubmitGuestNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, Guid.Empty, [], "registration_order_not_found");
        }

        return await sender.Send(new SubmitNativeRegistrationAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId, request.AttemptId,
            request.AttemptCapabilityToken, request.IdempotencyKey, request.Answers), cancellationToken);
    }
}

public sealed class LaunchGuestRegistrationProviderAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<LaunchGuestRegistrationProviderAttemptCommand, RegistrationProviderAttemptResult>
{
    public async Task<RegistrationProviderAttemptResult> Handle(
        LaunchGuestRegistrationProviderAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, Guid.Empty, null, "registration_order_not_found");
        }

        return await sender.Send(new LaunchRegistrationProviderAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId,
            request.ChannelId, request.BindingId, request.FormId, request.FormVersionId,
            request.SupersededAttemptId), cancellationToken);
    }
}

public sealed class SkipGuestNativeRegistrationRequirementCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ISender sender)
    : IRequestHandler<SkipGuestNativeRegistrationRequirementCommand, NativeRegistrationSkipResult>
{
    public async Task<NativeRegistrationSkipResult> Handle(
        SkipGuestNativeRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, null, "registration_order_not_found");
        }

        return await sender.Send(new SkipNativeRegistrationRequirementCommand(
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            request.RequirementId,
            request.AttemptId,
            request.AttemptCapabilityToken), cancellationToken);
    }
}
