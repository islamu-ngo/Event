using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Payments;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Services.Registration;
using MediatR;

namespace Explore.Application.Features.PaidCheckoutGovernance.Commands;

public sealed class GetPaidCheckoutSaleControlQueryHandler(IPaidCheckoutActivationRepository repository)
    : IRequestHandler<GetPaidCheckoutSaleControlQuery, PaidCheckoutSaleControlDto?>
{
    public async Task<PaidCheckoutSaleControlDto?> Handle(GetPaidCheckoutSaleControlQuery request, CancellationToken cancellationToken)
    {
        PaidCheckoutSaleControl? control = await repository.GetSaleControlAsync(
            request.TenantId, request.EventId, false, cancellationToken);
        return control is null ? null : Map(control);
    }

    internal static PaidCheckoutSaleControlDto Map(PaidCheckoutSaleControl control) => new()
    {
        TenantId = control.TenantId,
        EventId = control.EventId,
        IsStopped = control.IsStopped,
        ResumeReviewPending = control.ResumeRequestedBy.HasValue,
        Version = control.Version,
        AuditTrail = control.AuditTrail.Select(entry => new PaidCheckoutSaleControlAuditDto
        {
            Sequence = entry.Sequence,
            ActionCode = entry.ActionCode,
            ReasonCode = entry.ReasonCode,
            OccurredAt = entry.OccurredAt
        }).ToArray()
    };
}

public sealed class StopPaidCheckoutSalesCommandHandler(
    IPaidCheckoutActivationRepository repository,
    IEventRepository events,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<StopPaidCheckoutSalesCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(StopPaidCheckoutSalesCommand request, CancellationToken cancellationToken) =>
        PaidCheckoutSaleControlMutation.StopAsync(request.TenantId, request.EventId, request.ReasonCode,
            repository, events, currentUser, unitOfWork, timeProvider, cancellationToken);
}

public sealed class RequestPaidCheckoutResumeCommandHandler(
    IPaidCheckoutActivationRepository repository,
    IEventRepository events,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<RequestPaidCheckoutResumeCommand, BaseCommandResponse<Guid>>
{
    public Task<BaseCommandResponse<Guid>> Handle(RequestPaidCheckoutResumeCommand request, CancellationToken cancellationToken) =>
        PaidCheckoutSaleControlMutation.RequestResumeAsync(request.TenantId, request.EventId, request.ReasonCode,
            repository, events, currentUser, unitOfWork, timeProvider, cancellationToken);
}

public sealed class ReviewPaidCheckoutResumeCommandHandler(
    IPaidCheckoutActivationRepository repository,
    IEventRepository events,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<ReviewPaidCheckoutResumeCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(ReviewPaidCheckoutResumeCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid reviewer || !await PaidCheckoutSaleControlMutation.ValidScopeAsync(
                request.TenantId, request.EventId, events, cancellationToken))
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_governance_invalid");
        }
        try
        {
            Guid id = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                PaidCheckoutSaleControl control = await repository.GetSaleControlAsync(
                    request.TenantId, request.EventId, true, token)
                    ?? throw new InvalidOperationException("Sale control was not found.");
                control.ReviewResume(reviewer, request.Approved, request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
                await repository.SaveChangesAsync(token);
                return control.Id;
            }, cancellationToken);
            return PaidCheckoutSaleControlMutation.Success(id, request.Approved ? "Paid sales resumed." : "Paid-sales resume rejected.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_resume_review_invalid");
        }
    }
}

public sealed class RequestPaidCheckoutReviewCommandHandler(
    IPaidCheckoutActivationRepository repository,
    IPaidEventPolicyRepository policies,
    IEventRepository events,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<RequestPaidCheckoutReviewCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(RequestPaidCheckoutReviewCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid requester || !Enum.IsDefined(typeof(PaidCheckoutReviewTrigger), request.TriggerId))
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_review_invalid");
        }
        try
        {
            Guid id = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                Event? eventTarget = await events.GetEventWithDetails(request.EventId);
                if (eventTarget?.TenantId != request.TenantId || eventTarget.OrganizerActorId is not Guid organizerActorId)
                {
                    throw new InvalidOperationException("Event organizer was not found.");
                }
                PaidEventPolicyVersion instance = await policies.GetActiveInstanceAsync(token)
                    ?? throw new InvalidOperationException("Active paid-event policy was not found.");
                PaidEventPolicyVersion? tenant = await policies.GetActiveTenantAsync(request.TenantId, token);
                if (tenant is not null)
                {
                    PaidEventPolicyRules.ValidateTenantPolicy(instance, tenant);
                }
                PaidEventPolicyVersion effective = tenant ?? instance;
                var trigger = (PaidCheckoutReviewTrigger)request.TriggerId;
                PaidEventPolicyCurrencyRiskLimit? limit = effective.CurrencyRiskLimits.SingleOrDefault(value =>
                    value.CurrencyCode == request.CurrencyCode);
                if (trigger == PaidCheckoutReviewTrigger.FirstPaidEvent && !effective.RequiresFirstPaidEventReview ||
                    trigger == PaidCheckoutReviewTrigger.HighValue &&
                    (limit?.HighValueReviewThresholdMinor is not { } threshold || request.MaximumOrderAmountMinor < threshold))
                {
                    throw new InvalidOperationException("The requested review is not required by the effective policy.");
                }
                PaidCheckoutReviewApproval review = PaidCheckoutReviewApproval.Request(
                    request.TenantId, request.EventId, organizerActorId, effective.Id, request.CurrencyCode,
                    trigger, request.MaximumOrderAmountMinor, requester, request.ReasonCode,
                    timeProvider.GetUtcNow().UtcDateTime);
                await repository.AddReviewAsync(review, token);
                await repository.SaveChangesAsync(token);
                return review.Id;
            }, cancellationToken);
            return PaidCheckoutSaleControlMutation.Success(id, "Paid Checkout review requested.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_review_invalid");
        }
    }
}

public sealed class DecidePaidCheckoutReviewCommandHandler(
    IPaidCheckoutActivationRepository repository,
    ICurrentUserService currentUser,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IRequestHandler<DecidePaidCheckoutReviewCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(DecidePaidCheckoutReviewCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid reviewer)
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_review_invalid");
        }
        try
        {
            Guid id = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                PaidCheckoutReviewApproval review = await repository.GetReviewAsync(request.TenantId, request.ReviewId, true, token)
                    ?? throw new InvalidOperationException("Review was not found.");
                if (request.Approved) review.Approve(reviewer, request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
                else review.Reject(reviewer, request.ReasonCode, timeProvider.GetUtcNow().UtcDateTime);
                await repository.SaveChangesAsync(token);
                return review.Id;
            }, cancellationToken);
            return PaidCheckoutSaleControlMutation.Success(id, request.Approved ? "Paid Checkout review approved." : "Paid Checkout review rejected.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return PaidCheckoutSaleControlMutation.Failure("paid_checkout_review_invalid");
        }
    }
}

file static class PaidCheckoutSaleControlMutation
{
    internal static async Task<BaseCommandResponse<Guid>> StopAsync(
        Guid tenantId, Guid? eventId, string reasonCode, IPaidCheckoutActivationRepository repository,
        IEventRepository events, ICurrentUserService currentUser, IUnitOfWork unitOfWork,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actor || !await ValidScopeAsync(tenantId, eventId, events, cancellationToken))
            return Failure("paid_checkout_governance_invalid");
        try
        {
            Guid id = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                PaidCheckoutSaleControl? control = await repository.GetSaleControlAsync(tenantId, eventId, true, token);
                if (control is null)
                {
                    control = PaidCheckoutSaleControl.CreateStopped(tenantId, eventId, actor, reasonCode, timeProvider.GetUtcNow().UtcDateTime);
                    await repository.AddSaleControlAsync(control, token);
                }
                else
                {
                    _ = control.Stop(actor, reasonCode, timeProvider.GetUtcNow().UtcDateTime);
                }
                await repository.SaveChangesAsync(token);
                return control.Id;
            }, cancellationToken);
            return Success(id, "Paid sales stopped.");
        }
        catch (ArgumentException)
        {
            return Failure("paid_checkout_governance_invalid");
        }
    }

    internal static async Task<BaseCommandResponse<Guid>> RequestResumeAsync(
        Guid tenantId, Guid? eventId, string reasonCode, IPaidCheckoutActivationRepository repository,
        IEventRepository events, ICurrentUserService currentUser, IUnitOfWork unitOfWork,
        TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actor || !await ValidScopeAsync(tenantId, eventId, events, cancellationToken))
            return Failure("paid_checkout_governance_invalid");
        try
        {
            Guid id = await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                PaidCheckoutSaleControl? control = await repository.GetSaleControlAsync(tenantId, eventId, true, token);
                if (control is null)
                {
                    control = PaidCheckoutSaleControl.CreateStopped(tenantId, eventId, actor, "initial_activation_required", now);
                    await repository.AddSaleControlAsync(control, token);
                }
                control.RequestResume(actor, reasonCode, now);
                await repository.SaveChangesAsync(token);
                return control.Id;
            }, cancellationToken);
            return Success(id, "Paid-sales resume review requested.");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return Failure("paid_checkout_resume_request_invalid");
        }
    }

    internal static async Task<bool> ValidScopeAsync(
        Guid tenantId, Guid? eventId, IEventRepository events, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || eventId == Guid.Empty) return false;
        if (eventId is null) return true;
        Event? eventTarget = await events.GetAuthorizationTargetByIdAsync(eventId.Value, cancellationToken);
        return eventTarget?.TenantId == tenantId;
    }

    internal static BaseCommandResponse<Guid> Success(Guid id, string message) => BaseCommandResponse.Success(id, message);

    internal static BaseCommandResponse<Guid> Failure(string code) =>
        BaseCommandResponse.Failure<Guid>(code, "Paid Checkout governance action was not applied.");
}
