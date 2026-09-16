using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Responses;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class StartGuestRegistrationOrderCommandHandler(
    IRegistrationOrderStarter starter)
    : ICommandHandler<StartGuestRegistrationOrderCommand, GuestRegistrationOrderStartDto>
{
    public async Task<GuestRegistrationOrderStartDto> ExecuteAsync(
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
    : ICommandHandler<ContinueGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> ExecuteAsync(
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
    : ICommandHandler<FinalizeGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> ExecuteAsync(
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
    : ICommandHandler<CancelGuestRegistrationOrderCommand, GuestRegistrationOrderLifecycleResponseDto>
{
    public async Task<GuestRegistrationOrderLifecycleResponseDto> ExecuteAsync(
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
    ICommandHandler<LaunchNativeRegistrationAttemptCommand, NativeRegistrationAttemptResult> launchHandler)
    : ICommandHandler<LaunchGuestNativeRegistrationAttemptCommand, NativeRegistrationAttemptResult>
{
    public async Task<NativeRegistrationAttemptResult> ExecuteAsync(
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

        return await launchHandler.ExecuteAsync(new LaunchNativeRegistrationAttemptCommand(
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
    ICommandHandler<SubmitNativeRegistrationAttemptCommand, NativeRegistrationSubmissionResult> submitHandler)
    : ICommandHandler<SubmitGuestNativeRegistrationAttemptCommand, NativeRegistrationSubmissionResult>
{
    public async Task<NativeRegistrationSubmissionResult> ExecuteAsync(
        SubmitGuestNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, Guid.Empty, [], "registration_order_not_found");
        }

        return await submitHandler.ExecuteAsync(new SubmitNativeRegistrationAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId, request.AttemptId,
            request.AttemptCapabilityToken, request.IdempotencyKey, request.Answers), cancellationToken);
    }
}

public sealed class LaunchGuestRegistrationProviderAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    TimeProvider timeProvider,
    ICommandHandler<LaunchRegistrationProviderAttemptCommand, RegistrationProviderAttemptResult> launchProviderHandler)
    : ICommandHandler<LaunchGuestRegistrationProviderAttemptCommand, RegistrationProviderAttemptResult>
{
    public async Task<RegistrationProviderAttemptResult> ExecuteAsync(
        LaunchGuestRegistrationProviderAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, Guid.Empty, null, "registration_order_not_found");
        }

        return await launchProviderHandler.ExecuteAsync(new LaunchRegistrationProviderAttemptCommand(
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
    ICommandHandler<SkipNativeRegistrationRequirementCommand, NativeRegistrationSkipResult> skipHandler)
    : ICommandHandler<SkipGuestNativeRegistrationRequirementCommand, NativeRegistrationSkipResult>
{
    public async Task<NativeRegistrationSkipResult> ExecuteAsync(
        SkipGuestNativeRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetGuestOrderAsync(
                inventory, capabilities, tenant.TenantId, request.EventId, request.OrderId,
                request.CapabilityToken, timeProvider, cancellationToken) is null)
        {
            return new(false, null, "registration_order_not_found");
        }

        return await skipHandler.ExecuteAsync(new SkipNativeRegistrationRequirementCommand(
            tenant.TenantId,
            request.EventId,
            request.OrderId,
            request.RequirementId,
            request.AttemptId,
            request.AttemptCapabilityToken), cancellationToken);
    }
}
