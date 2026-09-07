namespace Explore.Application.Features.PaidEventPolicies;

using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.PaidEventPolicies;
using Explore.Application.Features.PaidEventPolicies.Handlers.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using FluentValidation.Results;

public static class PaidEventPolicyMutationFailureCodes
{
    public const string ValidationFailed = "paid_event_policy_validation_failed";
    public const string ConcurrencyConflict = "paid_event_policy_concurrency_conflict";
}

public static class PaidEventPolicyMutationLockKeys
{
    public const string Instance = "paid-event-policy:instance";

    public static string ForTenant(Guid tenantId)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        return $"paid-event-policy:tenant:{tenantId:D}";
    }
}

public sealed record TenantPaidEventPolicyMutationInput(
    Guid TenantId,
    RevisePaidEventPolicyDto Policy,
    int? ExpectedInstancePolicyVersion = null,
    bool RequireAbsentTenantPolicy = false);

public sealed record InstancePaidEventPolicyMutationInput(
    RevisePaidEventPolicyDto Policy,
    int ExpectedActivePolicyVersion);

public sealed record PaidEventPolicyMutationResult(
    bool Success,
    Guid? PolicyVersionId,
    string? FailureCode,
    string Message,
    IReadOnlyList<string> Errors);

public interface IPaidEventPolicyMutationBoundary
{
    Task<PaidEventPolicyMutationResult> ReviseInstanceAsync(
        RevisePaidEventPolicyDto policy,
        CancellationToken cancellationToken);

    Task<PaidEventPolicyMutationResult> ReviseTenantAsync(
        TenantPaidEventPolicyMutationInput request,
        CancellationToken cancellationToken);

    Task<PaidEventPolicyMutationResult> ReviseInstanceInCurrentTransactionAsync(
        InstancePaidEventPolicyMutationInput request,
        CancellationToken cancellationToken);

    Task<PaidEventPolicyMutationResult> ReviseTenantInCurrentTransactionAsync(
        TenantPaidEventPolicyMutationInput request,
        CancellationToken cancellationToken);
}

public sealed class PaidEventPolicyMutationBoundary(
    IPaidEventPolicyRepository policies,
    IUnitOfWork unitOfWork,
    ISettingMutationLock mutationLock) : IPaidEventPolicyMutationBoundary
{
    public Task<PaidEventPolicyMutationResult> ReviseInstanceAsync(
        RevisePaidEventPolicyDto policy,
        CancellationToken cancellationToken) =>
        ExecuteLockedAsync(
            [PaidEventPolicyMutationLockKeys.Instance],
            async token =>
            {
                PaidEventPolicyVersion? current =
                    await policies.GetActiveInstanceAsync(token);
                if (current is null || !current.IsActive)
                {
                    return Failure(
                        PaidEventPolicyMutationFailureCodes.ValidationFailed,
                        "Active instance paid-event policy is required.");
                }

                return await ReviseInstanceInCurrentTransactionAsync(
                    new InstancePaidEventPolicyMutationInput(
                        policy,
                        current.VersionNumber),
                    token);
            },
            cancellationToken);

    public Task<PaidEventPolicyMutationResult> ReviseTenantAsync(
        TenantPaidEventPolicyMutationInput request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteLockedAsync(
            [
                PaidEventPolicyMutationLockKeys.Instance,
                PaidEventPolicyMutationLockKeys.ForTenant(request.TenantId)
            ],
            token => ReviseTenantInCurrentTransactionAsync(request, token),
            cancellationToken);
    }

    public async Task<PaidEventPolicyMutationResult> ReviseTenantInCurrentTransactionAsync(
        TenantPaidEventPolicyMutationInput request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TenantId == Guid.Empty)
        {
            return Failure(
                PaidEventPolicyMutationFailureCodes.ValidationFailed,
                "Tenant is required.");
        }

        PaidEventPolicyMutationResult? invalid =
            await ValidateAsync(request.Policy, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        try
        {
            PaidEventPolicyVersion? instancePolicy =
                await policies.GetActiveInstanceAsync(cancellationToken);
            if (instancePolicy is null || !instancePolicy.IsActive)
            {
                return request.ExpectedInstancePolicyVersion.HasValue
                    ? Conflict()
                    : Failure(
                        PaidEventPolicyMutationFailureCodes.ValidationFailed,
                        "Active instance paid-event policy is required.");
            }

            if (request.ExpectedInstancePolicyVersion is { } expectedVersion
                && instancePolicy.VersionNumber != expectedVersion)
            {
                return Conflict();
            }

            PaidEventPolicyVersion? currentTenantPolicy =
                await policies.GetActiveTenantAsync(
                    request.TenantId,
                    cancellationToken);
            if (request.RequireAbsentTenantPolicy && currentTenantPolicy is not null)
            {
                return Conflict();
            }

            PaidEventPolicyVersion candidate =
                CreateTenant(request.TenantId, request.Policy);
            PaidEventPolicyRules.ValidateTenantPolicy(instancePolicy, candidate);
            PaidEventPolicyVersion revision = currentTenantPolicy is null
                ? candidate
                : CreateRevision(currentTenantPolicy, request.Policy);
            await policies.AddAsync(revision, cancellationToken);
            await policies.SaveChangesAsync(cancellationToken);
            return Succeeded(revision.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return Failure(
                PaidEventPolicyMutationFailureCodes.ValidationFailed,
                exception.Message);
        }
    }

    private Task<PaidEventPolicyMutationResult> ExecuteLockedAsync(
        IReadOnlyCollection<string> lockKeys,
        Func<CancellationToken, Task<PaidEventPolicyMutationResult>> operation,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteSerializableAsync(
            token => mutationLock.ExecuteManyAsync(lockKeys, operation, token),
            cancellationToken);

    public async Task<PaidEventPolicyMutationResult> ReviseInstanceInCurrentTransactionAsync(
        InstancePaidEventPolicyMutationInput request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        PaidEventPolicyMutationResult? invalid =
            await ValidateAsync(request.Policy, cancellationToken);
        if (invalid is not null)
        {
            return invalid;
        }

        try
        {
            PaidEventPolicyVersion? current =
                await policies.GetActiveInstanceAsync(cancellationToken);
            if (current is null || !current.IsActive)
            {
                return Conflict();
            }

            if (current.VersionNumber != request.ExpectedActivePolicyVersion)
            {
                return Conflict();
            }

            PaidEventPolicyVersion proposedRevision =
                CreateProposedInstance(request.Policy);
            await ValidateActiveTenantPoliciesAsync(
                proposedRevision,
                cancellationToken);
            PaidEventPolicyVersion revision = CreateRevision(current, request.Policy);
            await policies.AddAsync(revision, cancellationToken);
            await policies.SaveChangesAsync(cancellationToken);
            return Succeeded(revision.Id);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            return Failure(
                PaidEventPolicyMutationFailureCodes.ValidationFailed,
                exception.Message);
        }
    }

    private async Task ValidateActiveTenantPoliciesAsync(
        PaidEventPolicyVersion proposedInstancePolicy,
        CancellationToken cancellationToken)
    {
        const int pageSize =
            IPaidEventPolicyRepository.MaximumActiveTenantPolicyPageSize;
        int offset = 0;
        PaidEventPolicyVersion[] page;
        do
        {
            page = await policies.ListActiveTenantsAsync(
                offset,
                pageSize,
                cancellationToken);
            foreach (PaidEventPolicyVersion tenantPolicy in page)
            {
                PaidEventPolicyRules.ValidateTenantPolicy(
                    proposedInstancePolicy,
                    tenantPolicy);
            }

            offset = checked(offset + page.Length);
        }
        while (page.Length == pageSize);
    }

    private static async Task<PaidEventPolicyMutationResult?> ValidateAsync(
        RevisePaidEventPolicyDto policy,
        CancellationToken cancellationToken)
    {
        ValidationResult validation =
            await new RevisePaidEventPolicyCommandValidator()
                .ValidateAsync(policy, cancellationToken);
        return validation.IsValid
            ? null
            : Failure(
                PaidEventPolicyMutationFailureCodes.ValidationFailed,
                validation.Errors[0].ErrorMessage);
    }

    private static PaidEventPolicyVersion CreateTenant(
        Guid tenantId,
        RevisePaidEventPolicyDto policy) =>
        PaidEventPolicyVersion.CreateTenant(
            tenantId,
            policy.IsPaymentsEnabled,
            OrganizerKinds(policy),
            policy.RequiresLocalVerification,
            policy.AllowedCurrencyCodes,
            policy.DefaultCurrencyCode,
            RefundProtections(policy),
            RiskLimits(policy),
            policy.RequiresFirstPaidEventReview,
            policy.FarFutureReviewThresholdDays);

    private static PaidEventPolicyVersion CreateRevision(
        PaidEventPolicyVersion current,
        RevisePaidEventPolicyDto policy) =>
        current.CreateRevision(
            policy.IsPaymentsEnabled,
            OrganizerKinds(policy),
            policy.RequiresLocalVerification,
            policy.AllowedCurrencyCodes,
            policy.DefaultCurrencyCode,
            RefundProtections(policy),
            RiskLimits(policy),
            policy.RequiresFirstPaidEventReview,
            policy.FarFutureReviewThresholdDays);

    private static PaidEventPolicyVersion CreateProposedInstance(
        RevisePaidEventPolicyDto policy) =>
        CreateRevision(PaidEventPolicyVersion.CreateDefaultInstance(), policy);

    private static IEnumerable<ActorTypeEnum> OrganizerKinds(
        RevisePaidEventPolicyDto policy) =>
        policy.AllowedOrganizerKindIds.Select(id => (ActorTypeEnum)id);

    private static IEnumerable<PaidEventRefundProtection> RefundProtections(
        RevisePaidEventPolicyDto policy) =>
        policy.RefundProtectionIds.Select(id => (PaidEventRefundProtection)id);

    private static IEnumerable<PaidEventPolicyCurrencyRiskLimit> RiskLimits(
        RevisePaidEventPolicyDto policy) =>
        policy.CurrencyRiskLimits.Select(limit => PaidEventPolicyCurrencyRiskLimit.Create(
            limit.CurrencyCode,
            limit.PerEventSalesCeilingMinor,
            limit.PerEventSalesCountCeiling,
            limit.RollingOrganizerSalesCeilingMinor,
            limit.RollingOrganizerSalesCountCeiling,
            limit.RollingOrganizerWindowDays,
            limit.HighValueReviewThresholdMinor));

    private static PaidEventPolicyMutationResult Succeeded(Guid id) =>
        new(
            Success: true,
            id,
            FailureCode: null,
            "Paid-event policy revised.",
            []);

    private static PaidEventPolicyMutationResult Conflict() =>
        new(
            Success: false,
            PolicyVersionId: null,
            PaidEventPolicyMutationFailureCodes.ConcurrencyConflict,
            "Paid-event policy changed concurrently.",
            ["Retry against the active instance paid-event policy revision."]);

    private static PaidEventPolicyMutationResult Failure(
        string code,
        string error) =>
        new(
            Success: false,
            PolicyVersionId: null,
            code,
            "Paid-event policy is invalid.",
            [error]);
}
