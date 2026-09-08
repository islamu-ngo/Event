using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record RecordRegistrationRequirementFulfillmentCommand(
    Guid TenantId,
    Guid RegistrationOrderId,
    Guid RegistrationRequirementId,
    Guid? RegistrationSubmissionId,
    RegistrationAnswerSubjectTypeEnum SubjectType,
    Guid SubjectId,
    bool IsSkipped) : IRequest<bool>;

public sealed class RecordRegistrationRequirementFulfillmentCommandValidator
    : AbstractValidator<RecordRegistrationRequirementFulfillmentCommand>
{
    public RecordRegistrationRequirementFulfillmentCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.RegistrationOrderId).NotEmpty();
        RuleFor(command => command.RegistrationRequirementId).NotEmpty();
        RuleFor(command => command.SubjectId).NotEmpty();
        RuleFor(command => command.SubjectType).IsInEnum();
        RuleFor(command => command.RegistrationSubmissionId)
            .NotEmpty().When(command => !command.IsSkipped)
            .Null().When(command => command.IsSkipped);
    }
}

public sealed class RecordRegistrationRequirementFulfillmentCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationSubmissionRepository submissions,
    IRegistrationFinalizationRepository finalization,
    TimeProvider timeProvider)
    : IRequestHandler<RecordRegistrationRequirementFulfillmentCommand, bool>
{
    public async Task<bool> Handle(
        RecordRegistrationRequirementFulfillmentCommand request,
        CancellationToken cancellationToken)
    {
        await new RecordRegistrationRequirementFulfillmentCommandValidator()
            .ValidateAndThrowAsync(request, cancellationToken);
        RegistrationOrder order = await inventory.GetOrderByIdAsync(
            request.RegistrationOrderId, request.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("Registration order was not found.");
        RegistrationRequirement requirement = await submissions.GetRequirementAsync(
            request.TenantId, request.RegistrationRequirementId, cancellationToken)
            ?? throw new InvalidOperationException("Registration requirement was not found.");
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        RegistrationRequirementFulfillment fulfillment;
        if (request.IsSkipped)
        {
            fulfillment = RegistrationRequirementFulfillment.CreateSkipped(
                order, requirement, request.SubjectType, request.SubjectId, now);
        }
        else
        {
            RegistrationSubmission submission = await submissions.GetSubmissionAsync(
                request.TenantId, request.RegistrationSubmissionId!.Value, cancellationToken)
                ?? throw new InvalidOperationException("Registration submission was not found.");
            fulfillment = RegistrationRequirementFulfillment.CreateFulfilled(
                order, requirement, submission, request.SubjectType, request.SubjectId, now);
        }

        return await finalization.RecordFulfillmentAsync(fulfillment, now, cancellationToken);
    }
}

public sealed record DrainRegistrationFinalizationEffectsCommand(
    string LeaseOwner,
    int BatchSize = 100,
    int LeaseSeconds = 60) : IRequest<int>;

public sealed class DrainRegistrationFinalizationEffectsCommandValidator
    : AbstractValidator<DrainRegistrationFinalizationEffectsCommand>
{
    public DrainRegistrationFinalizationEffectsCommandValidator()
    {
        RuleFor(command => command.LeaseOwner).NotEmpty().MaximumLength(RegistrationFinalizationEffect.MaxLeaseOwnerLength);
        RuleFor(command => command.BatchSize).InclusiveBetween(1, 1000);
        RuleFor(command => command.LeaseSeconds).InclusiveBetween(1, 3600);
    }
}

public sealed class DrainRegistrationFinalizationEffectsCommandHandler(
    IRegistrationFinalizationRepository finalization,
    IRegistrationOrderLifecycleService lifecycle,
    ITenantContextAccessor tenantContextAccessor,
    TimeProvider timeProvider,
    IAdmissionIssuanceService? admissionIssuance = null)
    : IRequestHandler<DrainRegistrationFinalizationEffectsCommand, int>
{
    public async Task<int> Handle(
        DrainRegistrationFinalizationEffectsCommand request,
        CancellationToken cancellationToken)
    {
        await new DrainRegistrationFinalizationEffectsCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<RegistrationFinalizationClaim> claims = await finalization.ClaimDueAsync(
            request.LeaseOwner, request.BatchSize, now, TimeSpan.FromSeconds(request.LeaseSeconds), cancellationToken);
        int completed = 0;
        foreach (RegistrationFinalizationClaim claim in claims)
        {
            tenantContextAccessor.SetTenant(claim.TenantId);
            try
            {
                var result = await lifecycle.ReadyForCheckoutAsync(
                    claim.RegistrationOrderId, claim.TenantId, cancellationToken);
                if (result.IsSuccess && result.Order?.StatusId is
                    (int)RegistrationOrderStatusEnum.AwaitingPayment or
                    (int)RegistrationOrderStatusEnum.NeedsReconciliation)
                {
                    result = await lifecycle.FinalizePaidAsync(
                        claim.RegistrationOrderId, claim.TenantId, cancellationToken);
                }

                DateTime settledAt = timeProvider.GetUtcNow().UtcDateTime;
                bool issuanceAllowsCompletion = true;
                if (result.IsSuccess && result.Order?.StatusId == (int)RegistrationOrderStatusEnum.Confirmed &&
                    admissionIssuance is not null)
                {
                    AdmissionIssuanceResult issuance = await admissionIssuance.IssueConfirmedAsync(
                        new AdmissionIssuanceRequest(
                            claim.TenantId,
                            claim.RegistrationOrderId,
                            claim.EffectId,
                            AdmissionIssuanceAuthority.ForOrderTotal(result.Order.TotalDueMinor)),
                        cancellationToken);
                    issuanceAllowsCompletion =
                        issuance.Outcome is (
                            AdmissionIssuanceOutcome.Issued or
                            AdmissionIssuanceOutcome.AlreadyIssued or
                            AdmissionIssuanceOutcome.NoAssignments) &&
                        issuance.DeliveryOutcome is
                            AdmissionDeliveryOutcome.NotRequired or
                            AdmissionDeliveryOutcome.Delivered;
                }
                if (result.IsSuccess && result.Order is not null && IsSettled(result.Order.StatusId) && issuanceAllowsCompletion)
                {
                    completed += await finalization.CompleteAsync(claim, settledAt, cancellationToken) ? 1 : 0;
                }
                else
                {
                    await finalization.RetryAsync(claim, settledAt.AddMinutes(1), settledAt, cancellationToken);
                }
            }
            finally
            {
                tenantContextAccessor.Clear();
            }
        }

        return completed;
    }

    private static bool IsSettled(int statusId) => (RegistrationOrderStatusEnum)statusId is
        RegistrationOrderStatusEnum.ReadyForCheckout or
        RegistrationOrderStatusEnum.Confirmed or
        RegistrationOrderStatusEnum.Rejected or
        RegistrationOrderStatusEnum.Expired or
        RegistrationOrderStatusEnum.Cancelled or
        RegistrationOrderStatusEnum.NeedsReconciliation;
}
