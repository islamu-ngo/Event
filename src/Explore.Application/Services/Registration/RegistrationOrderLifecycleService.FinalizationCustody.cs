using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;

namespace Explore.Application.Services.Registration;

public sealed partial class RegistrationOrderLifecycleService
{
    public Task<RegistrationOrderLifecycleResponseDto> ReadyForCheckoutAsync(
        RegistrationFinalizationClaim claim,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            ReadyForCheckoutCoreAsync(claim.RegistrationOrderId, claim.TenantId, cancellationToken, claim),
            cancellationToken);

    public Task<RegistrationOrderLifecycleResponseDto> FinalizePaidAsync(
        RegistrationFinalizationClaim claim,
        CancellationToken cancellationToken) => WithdrawHoldDeadlineWhenTerminalAsync(
            FinalizeCoreAsync(claim.RegistrationOrderId, claim.TenantId, paid: true, cancellationToken, claim),
            cancellationToken);

    private async Task<RegistrationOrderLifecycleResponseDto> ExecuteFinalizationTransactionAsync(
        RegistrationFinalizationClaim? claim,
        Func<CancellationToken, Task<RegistrationOrderLifecycleResponseDto>> operation,
        CancellationToken cancellationToken)
    {
        try
        {
            return await unitOfWork.ExecuteSerializableAsync(async token =>
            {
                if (claim is not null && !await finalization.TryAcquireExecutionAsync(claim, token))
                    throw new RegistrationFinalizationCustodyLostException();

                RegistrationOrderLifecycleResponseDto result = await operation(token);
                // Expiry during an awaited mutation rolls back its entire owning
                // transaction, including inventory, promotion and outbox writes.
                if (claim is not null && !await finalization.TryAcquireExecutionAsync(claim, token))
                    throw new RegistrationFinalizationCustodyLostException();
                return result;
            }, cancellationToken);
        }
        catch (RegistrationFinalizationCustodyLostException) when (claim is not null)
        {
            return Failure(claim.RegistrationOrderId, null, "registration_finalization_custody_lost");
        }
    }

    private Task<RegistrationOrderLifecycleResponseDto> ObserveFinalizationResultAsync(
        RegistrationFinalizationClaim? claim,
        RegistrationOrderLifecycleResponseDto result,
        CancellationToken cancellationToken) =>
        claim is null
            ? Task.FromResult(result)
            : ExecuteFinalizationTransactionAsync(claim, _ => Task.FromResult(result), cancellationToken);
}
