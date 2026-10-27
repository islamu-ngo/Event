using Explore.Application.Contracts.Persistence;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task RequireOriginalMeasurementAtCommitAsync(
        ModerationQualificationMutation request, ModerationSourceAuthority original, CancellationToken token)
    {
        if (context.Database.CurrentTransaction is null
            || !await new ModerationQualificationAuthority(context, admin, machine)
                .ProviderTenantMatchesAsync(original.Tenant.Id, token)
            || await admin.ResolveUserIdAsync(token) != original.UserId)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Unavailable);
        // Reread on the same retained/primary acquisition, including adopted outer commits.
        // Never acquire a newly discovered human after the original sorted fence pass.
        RequireConsumerRuntime(request);
        if (request is PrepareModerationConsumerMeasurementSource preparation)
            await ReadMeasurementPreparationAsync(preparation, original, clock.GetUtcNow(), token);
        else if (request is AcceptModerationConsumerMeasurementSource acceptance)
            await ReadMeasurementAcceptanceAsync(acceptance, original, clock.GetUtcNow(), token);
        else
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
    }
}
