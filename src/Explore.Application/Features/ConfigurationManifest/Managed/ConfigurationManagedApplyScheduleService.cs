namespace Explore.Application.Features.ConfigurationManifest.Managed;

using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Importing;
using Explore.Domain;

public interface IConfigurationManagedApplyScheduleRepository
{
    Task AddAsync(
        ConfigurationManagedApplySchedule schedule,
        CancellationToken cancellationToken);

    Task<ConfigurationManagedApplySchedule?> GetForUpdateAsync(
        Guid scheduleId,
        string targetAuthorityKey,
        CancellationToken cancellationToken);

    Task UpdateAsync(
        ConfigurationManagedApplySchedule schedule,
        CancellationToken cancellationToken);
}

public sealed record ConfigurationManagedApplyScheduleResult(
    Guid ScheduleId,
    string TargetAuthorityKey,
    DateTime ApplyNotBefore,
    DateTime ApplyBefore,
    ConfigurationManagedApplyScheduleStatus Status,
    DateTime CreatedAt,
    DateTime? CompletedAt)
{
    public override string ToString() =>
        nameof(ConfigurationManagedApplyScheduleResult);
}

public sealed class ConfigurationManagedApplyScheduleService(
    IConfigurationManagedApplyScheduleRepository schedules,
    IConfigurationImportSessionRepository sessions,
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
{
    public Task<ConfigurationManagedApplyScheduleResult> CreateAsync(
        Guid sessionId,
        ConfigurationImportTarget target,
        string accessToken,
        DateTime applyNotBefore,
        DateTime applyBefore,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                DateTime now = UtcNow();
                ConfigurationImportSession session =
                    await sessions.GetForUpdateAsync(sessionId, target, token)
                    ?? throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.ArtifactMissing);
                session.AuthorizePreview(
                    target,
                    ConfigurationImportSessionManager.DigestToken(accessToken),
                    now);
                ConfigurationImportPreviewBinding binding =
                    session.PreviewBinding
                    ?? throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.StalePreview);
                ConfigurationManagedApplySchedule schedule =
                    ConfigurationManagedApplySchedule.Create(
                        Guid.CreateVersion7(),
                        target.AuthorityKey,
                        session.ArtifactDigest,
                        binding.TargetRevisionDigest,
                        PlanDigest(binding),
                        Actor(),
                        applyNotBefore,
                        applyBefore,
                        now);
                await schedules.AddAsync(schedule, token);
                return Map(schedule);
            },
            cancellationToken);

    public Task<ConfigurationManagedApplyScheduleResult> ApproveAsync(
        Guid scheduleId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken) =>
        MutateAsync(
            scheduleId,
            target,
            schedule => schedule.Approve(Actor(), UtcNow()),
            cancellationToken);

    public Task<ConfigurationManagedApplyScheduleResult> CancelAsync(
        Guid scheduleId,
        ConfigurationImportTarget target,
        CancellationToken cancellationToken) =>
        MutateAsync(
            scheduleId,
            target,
            schedule => schedule.Cancel(UtcNow()),
            cancellationToken);

    public async Task AuthorizeApplyAsync(
        Guid scheduleId,
        ConfigurationImportTarget target,
        string artifactDigest,
        ConfigurationImportPreviewBinding binding,
        Guid actorUserId,
        DateTime occurredAt,
        CancellationToken cancellationToken)
    {
        ConfigurationManagedApplySchedule schedule =
            await schedules.GetForUpdateAsync(
                scheduleId,
                target.AuthorityKey,
                cancellationToken)
            ?? throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        if (!string.Equals(
                schedule.ArtifactDigest,
                artifactDigest,
                StringComparison.Ordinal)
            || !string.Equals(
                schedule.ManagedPlanDigest,
                PlanDigest(binding),
                StringComparison.Ordinal)
            || !string.Equals(
                schedule.TargetRevisionDigest,
                binding.TargetRevisionDigest,
                StringComparison.Ordinal))
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.StalePreview);
        }
        try
        {
            schedule.Apply(
                actorUserId,
                binding.TargetRevisionDigest,
                occurredAt);
        }
        catch (InvalidOperationException)
        {
            throw new ConfigurationImportSessionException(
                ConfigurationImportFailureCodes.ApplyBlocked);
        }
        await schedules.UpdateAsync(schedule, cancellationToken);
    }

    private Task<ConfigurationManagedApplyScheduleResult> MutateAsync(
        Guid scheduleId,
        ConfigurationImportTarget target,
        Action<ConfigurationManagedApplySchedule> mutate,
        CancellationToken cancellationToken) =>
        unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                ConfigurationManagedApplySchedule schedule =
                    await schedules.GetForUpdateAsync(
                        scheduleId,
                        target.AuthorityKey,
                        token)
                    ?? throw new ConfigurationImportSessionException(
                        ConfigurationImportFailureCodes.ArtifactMissing);
                mutate(schedule);
                await schedules.UpdateAsync(schedule, token);
                return Map(schedule);
            },
            cancellationToken);

    internal static string PlanDigest(ConfigurationImportPreviewBinding binding) =>
        ConfigurationImportDigest.Compute(
        [
            binding.ArtifactDigest,
            binding.Target.AuthorityKey,
            binding.TargetRevisionDigest,
            binding.SelectedSectionsDigest,
            binding.MappingDigest,
            binding.RequiredApprovalDigest,
            ((int)binding.ApplyMode).ToString(
                System.Globalization.CultureInfo.InvariantCulture)
        ]);

    private Guid Actor() =>
        currentUser.IsAuthenticated
        && currentUser.UserId is { } actorUserId
        && actorUserId != Guid.Empty
            ? actorUserId
            : throw new UnauthorizedAccessException();

    private DateTime UtcNow() => timeProvider.GetUtcNow().UtcDateTime;

    private static ConfigurationManagedApplyScheduleResult Map(
        ConfigurationManagedApplySchedule schedule) =>
        new(
            schedule.Id,
            schedule.TargetAuthorityKey,
            schedule.ApplyNotBefore,
            schedule.ApplyBefore,
            schedule.Status,
            schedule.CreatedAt,
            schedule.CompletedAt);
}
