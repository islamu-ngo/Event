using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Features.RegistrationOrders.Validators;
using Explore.Application.Features.RegistrationSubmissions.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Handlers.Commands;

public sealed class StartAuthenticatedRegistrationOrderCommandHandler(
    IRegistrationOrderStarter starter,
    ICurrentUserService currentUser)
    : IRequestHandler<StartAuthenticatedRegistrationOrderCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(
        StartAuthenticatedRegistrationOrderCommand request,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            return Task.FromResult(BaseCommandResponse.Failure<Guid>(
                "registration_order_authentication_required",
                "Registration order requires an authenticated account.",
                ["Registration order requires an authenticated account."],
                request.EventId));
        }

        return starter.StartAsync(new CreateRegistrationOrderWithHoldCommand
        {
            EventId = request.EventId,
            TicketCatalogVersionId = request.TicketCatalogVersionId,
            AccountUserId = userId,
            BookingPartyType = request.BookingPartyType,
            PlatformContributionBasisPoints = request.PlatformContributionBasisPoints,
            Lines = request.Lines
        }, cancellationToken);
    }
}

public sealed class ClaimGuestRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IGuestCapabilityTokenService capabilities,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    IUserRepository users,
    TimeProvider timeProvider,
    IUnitOfWork unitOfWork)
    : IRequestHandler<ClaimGuestRegistrationOrderCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ClaimGuestRegistrationOrderCommand request,
        CancellationToken cancellationToken)
    {
        if (!(await new GuestRegistrationOrderAccessCommandValidator<ClaimGuestRegistrationOrderCommand>()
                .ValidateAsync(request, cancellationToken)).IsValid)
        {
            return NotFound(request.OrderId);
        }

        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId)
        {
            return BaseCommandResponse.Failure<Guid>(
                "registration_order_authentication_required",
                "Registration order claim requires an authenticated account.",
                ["Registration order claim requires an authenticated account."],
                request.OrderId);
        }

        User? user = await users.GetUserWithDetails(userId, cancellationToken);
        string? verifiedEmail = user is { EmailVerified: true } ? user.Pii.Email.Trim().ToUpperInvariant() : null;
        if (string.IsNullOrWhiteSpace(verifiedEmail))
        {
            return Invalid(request.OrderId, "registration_order_verified_email_required", "A verified account email is required to claim this registration order.");
        }

        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                RegistrationOrder? order = await inventory.GetOrderForUpdateWithPiiAsync(request.OrderId, tenant.TenantId, token);
                if (order is null || order.EventId != request.EventId || order.GuestAccessTokenHash is null ||
                    order.ExpiresAt is { } expiresAt && expiresAt <= timeProvider.GetUtcNow().UtcDateTime ||
                    !capabilities.Matches(request.CapabilityToken, order.GuestAccessTokenHash))
                {
                    return NotFound(request.OrderId);
                }

                bool linked = order.TryLinkGuestOrderToAccount(userId, verifiedEmail);
                await inventory.SaveChangesAsync(token);
                return BaseCommandResponse.Success(
                    order.Id,
                    linked
                        ? "Registration order linked to the current account."
                        : "Registration order already linked to the current account.");
            }, cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("another account", StringComparison.Ordinal))
        {
            return Invalid(request.OrderId, "registration_order_already_linked", exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return Invalid(request.OrderId, "registration_order_email_mismatch", exception.Message);
        }
        catch (ArgumentException exception)
        {
            return Invalid(request.OrderId, "registration_order_claim_invalid", exception.Message);
        }
    }

    private static BaseCommandResponse<Guid> NotFound(Guid orderId) => BaseCommandResponse.Failure<Guid>(
        "registration_order_not_found", "Registration order was not found.", id: orderId);

    private static BaseCommandResponse<Guid> Invalid(Guid orderId, string code, string message) =>
        BaseCommandResponse.Failure<Guid>(code, message, [message], orderId);
}

public sealed class ContinueAuthenticatedRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant,
    ICurrentUserService currentUser)
    : IRequestHandler<ContinueAuthenticatedRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public Task<RegistrationOrderLifecycleResponseDto> Handle(
        ContinueAuthenticatedRegistrationOrderCommand request,
        CancellationToken cancellationToken) => RegistrationOrderAccessGuard.ExecuteCurrentAccountAsync(
        request,
        inventory,
        tenant,
        currentUser,
        (orderId, tenantId, token) => lifecycle.SubmitAsync(
            orderId,
            tenantId,
            request.PlatformContributionBasisPoints,
            token),
        cancellationToken);
}

public sealed class FinalizeAuthenticatedRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant,
    ICurrentUserService currentUser)
    : IRequestHandler<FinalizeAuthenticatedRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(
        FinalizeAuthenticatedRegistrationOrderCommand request,
        CancellationToken cancellationToken) => await RegistrationOrderAccessGuard.ExecuteCurrentAccountAsync(
        request, inventory, tenant, currentUser, lifecycle.FinalizeFreeAsync, cancellationToken);
}

public sealed class CancelAuthenticatedRegistrationOrderCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationOrderLifecycleService lifecycle,
    ITenantContext tenant,
    ICurrentUserService currentUser)
    : IRequestHandler<CancelAuthenticatedRegistrationOrderCommand, RegistrationOrderLifecycleResponseDto>
{
    public async Task<RegistrationOrderLifecycleResponseDto> Handle(
        CancelAuthenticatedRegistrationOrderCommand request,
        CancellationToken cancellationToken) => await RegistrationOrderAccessGuard.ExecuteCurrentAccountAsync(
        request, inventory, tenant, currentUser, lifecycle.CancelAsync, cancellationToken);
}

public sealed class LaunchAuthenticatedNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<LaunchAuthenticatedNativeRegistrationAttemptCommand, NativeRegistrationAttemptResult>
{
    public async Task<NativeRegistrationAttemptResult> Handle(
        LaunchAuthenticatedNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
                inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken) is null)
        {
            return Missing(request.RequirementId, request.ChannelId, request.FormId, request.FormVersionId);
        }

        return await sender.Send(new LaunchNativeRegistrationAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId,
            request.ChannelId, request.FormId, request.FormVersionId, request.BindingId,
            request.SupersededAttemptId), cancellationToken);
    }

    private static NativeRegistrationAttemptResult Missing(
        Guid requirementId, Guid channelId, Guid formId, Guid versionId) =>
        new(false, Guid.Empty, requirementId, channelId, formId, versionId,
            default, null, [], null, false, null, "registration_order_not_found");
}

public sealed class SubmitAuthenticatedNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<SubmitAuthenticatedNativeRegistrationAttemptCommand, NativeRegistrationSubmissionResult>
{
    public async Task<NativeRegistrationSubmissionResult> Handle(
        SubmitAuthenticatedNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
                inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken) is null)
        {
            return new(false, Guid.Empty, [], "registration_order_not_found");
        }

        return await sender.Send(new SubmitNativeRegistrationAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId, request.AttemptId,
            request.AttemptCapabilityToken, request.IdempotencyKey, request.Answers), cancellationToken);
    }
}

public sealed class LaunchAuthenticatedRegistrationProviderAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<LaunchAuthenticatedRegistrationProviderAttemptCommand, RegistrationProviderAttemptResult>
{
    public async Task<RegistrationProviderAttemptResult> Handle(
        LaunchAuthenticatedRegistrationProviderAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
                inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken) is null)
        {
            return new(false, Guid.Empty, null, "registration_order_not_found");
        }

        return await sender.Send(new LaunchRegistrationProviderAttemptCommand(
            tenant.TenantId, request.EventId, request.OrderId, request.RequirementId,
            request.ChannelId, request.BindingId, request.FormId, request.FormVersionId,
            request.SupersededAttemptId), cancellationToken);
    }
}

public sealed class SkipAuthenticatedNativeRegistrationRequirementCommandHandler(
    IRegistrationInventoryRepository inventory,
    ITenantContext tenant,
    ICurrentUserService currentUser,
    ISender sender)
    : IRequestHandler<SkipAuthenticatedNativeRegistrationRequirementCommand, NativeRegistrationSkipResult>
{
    public async Task<NativeRegistrationSkipResult> Handle(
        SkipAuthenticatedNativeRegistrationRequirementCommand request,
        CancellationToken cancellationToken)
    {
        if (await RegistrationOrderAccessGuard.GetCurrentAccountOrderAsync(
                inventory, currentUser, tenant.TenantId, request.EventId, request.OrderId, cancellationToken) is null)
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
