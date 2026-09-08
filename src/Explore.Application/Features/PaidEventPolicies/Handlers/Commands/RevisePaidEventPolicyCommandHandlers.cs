using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Requests.Commands;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;

namespace Explore.Application.Features.PaidEventPolicies.Handlers.Commands;

public sealed class ReviseInstancePaidEventPolicyCommandHandler(
    IPaidEventPolicyMutationBoundary mutationBoundary)
    : IRequestHandler<ReviseInstancePaidEventPolicyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReviseInstancePaidEventPolicyCommand request,
        CancellationToken cancellationToken)
    {
        var validation = await new RevisePaidEventPolicyCommandValidator()
            .ValidateAsync(request.Policy, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation.Errors[0].ErrorMessage);
        }

        PaidEventPolicyMutationResult result =
            await mutationBoundary.ReviseInstanceAsync(
                request.Policy,
                cancellationToken);
        return ToResponse(result);
    }

    internal static BaseCommandResponse<Guid> ToResponse(
        PaidEventPolicyMutationResult result) => result.Success
            ? BaseCommandResponse.Success(
                result.PolicyVersionId
                    ?? throw new InvalidOperationException(
                        "A successful paid-event policy mutation requires a revision identity."),
                result.Message)
            : BaseCommandResponse.Failure<Guid>(
                result.FailureCode
                    ?? PaidEventPolicyMutationFailureCodes.ValidationFailed,
                result.Message,
                result.Errors);

    internal static BaseCommandResponse<Guid> ValidationFailure(string error) =>
        BaseCommandResponse.Failure<Guid>(
            PaidEventPolicyMutationFailureCodes.ValidationFailed,
            "Paid-event policy is invalid.",
            [error]);
}

public sealed class RevisePaidEventPolicyCommandValidator : AbstractValidator<RevisePaidEventPolicyDto>
{
    public RevisePaidEventPolicyCommandValidator()
    {
        RuleFor(policy => policy).NotNull();
        When(policy => policy is not null, () =>
        {
            RuleFor(policy => policy.AllowedOrganizerKindIds).NotEmpty();
            RuleForEach(policy => policy.AllowedOrganizerKindIds).Must(id => Enum.IsDefined(typeof(ActorTypeEnum), id));
            RuleFor(policy => policy.AllowedCurrencyCodes).NotEmpty();
            RuleForEach(policy => policy.AllowedCurrencyCodes).NotEmpty().MaximumLength(3);
            RuleFor(policy => policy.DefaultCurrencyCode).MaximumLength(3);
            RuleFor(policy => policy.RefundProtectionIds).NotEmpty();
            RuleForEach(policy => policy.RefundProtectionIds).Must(id => Enum.IsDefined(typeof(PaidEventRefundProtection), id));
            RuleForEach(policy => policy.CurrencyRiskLimits).SetValidator(new PaidEventPolicyCurrencyRiskLimitDtoValidator());
            RuleFor(policy => policy.FarFutureReviewThresholdDays).GreaterThan(0).When(policy => policy.FarFutureReviewThresholdDays.HasValue);
        });
    }
}

internal sealed class PaidEventPolicyCurrencyRiskLimitDtoValidator : AbstractValidator<PaidEventPolicyCurrencyRiskLimitDto>
{
    public PaidEventPolicyCurrencyRiskLimitDtoValidator()
    {
        RuleFor(limit => limit.CurrencyCode).NotEmpty().MaximumLength(3);
        RuleFor(limit => limit.PerEventSalesCeilingMinor).GreaterThan(0).When(limit => limit.PerEventSalesCeilingMinor.HasValue);
        RuleFor(limit => limit.PerEventSalesCountCeiling).GreaterThan(0).When(limit => limit.PerEventSalesCountCeiling.HasValue);
        RuleFor(limit => limit.RollingOrganizerSalesCeilingMinor).GreaterThan(0).When(limit => limit.RollingOrganizerSalesCeilingMinor.HasValue);
        RuleFor(limit => limit.RollingOrganizerSalesCountCeiling).GreaterThan(0).When(limit => limit.RollingOrganizerSalesCountCeiling.HasValue);
        RuleFor(limit => limit.RollingOrganizerWindowDays).GreaterThan(0).When(limit => limit.RollingOrganizerWindowDays.HasValue);
        RuleFor(limit => limit.HighValueReviewThresholdMinor).GreaterThan(0).When(limit => limit.HighValueReviewThresholdMinor.HasValue);
    }
}

public sealed class ReviseTenantPaidEventPolicyCommandHandler(
    IPaidEventPolicyMutationBoundary mutationBoundary)
    : IRequestHandler<ReviseTenantPaidEventPolicyCommand, BaseCommandResponse<Guid>>
{
    public async Task<BaseCommandResponse<Guid>> Handle(
        ReviseTenantPaidEventPolicyCommand request,
        CancellationToken cancellationToken)
    {
        if (request.TenantId == Guid.Empty)
        {
            return ReviseInstancePaidEventPolicyCommandHandler.ValidationFailure(
                "Tenant is required.");
        }

        var validation = await new RevisePaidEventPolicyCommandValidator()
            .ValidateAsync(request.Policy, cancellationToken);
        if (!validation.IsValid)
        {
            return ReviseInstancePaidEventPolicyCommandHandler.ValidationFailure(
                validation.Errors[0].ErrorMessage);
        }

        return ReviseInstancePaidEventPolicyCommandHandler.ToResponse(
            await mutationBoundary.ReviseTenantAsync(
                new TenantPaidEventPolicyMutationInput(
                    request.TenantId,
                    request.Policy),
                cancellationToken));
    }
}
