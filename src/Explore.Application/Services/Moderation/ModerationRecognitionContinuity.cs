using System.Collections.Immutable;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Services.Moderation;

public sealed class ModerationRecognitionContinuity(
    IModerationRecognitionContinuityRepository repository, ISettingMutationLock settings)
    : IModerationRecognitionContinuity, IModerationRecognitionContinuityReader
{
    private readonly AsyncLocal<bool> outerLockHeld = new();

    public Task<T> ExecuteWithRecognitionLockAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
        settings.ExecuteOrderedGroupsAsync([[ModerationRecognitionKeyRing.SecretSettingKey]], async ct =>
        {
            var previous = outerLockHeld.Value;
            outerLockHeld.Value = true;
            try { return await operation(ct); }
            finally { outerLockHeld.Value = previous; }
        }, token);

    public async Task<ModerationRecognitionContinuityResult> MatchAndBindAsync(
        ModerationRecognitionContinuityRequest request, CancellationToken token) =>
        Map(outerLockHeld.Value
            ? await repository.MatchAndBindAsync(request, token)
            : ModerationRecognitionContinuityEvaluation.Closed(ModerationRecognitionContinuityFailure.NativeBoundaryRequired));

    public Task<ModerationRecognitionContinuityFailure> GuardExistingSubjectAsync(Guid subjectId, CancellationToken token) =>
        repository.GuardExistingSubjectAsync(subjectId, token);

    public async Task<ModerationRecognitionContinuityResult> ReadCurrentInTransactionAsync(Guid freshSubjectId, CancellationToken token) =>
        Map(await repository.ReadCurrentInTransactionAsync(freshSubjectId, token));

    public async Task<ModerationRecognitionContinuityResult> ReadPublicationInTransactionAsync(
        Guid freshSubjectId, AuthorityScope target, CancellationToken token) =>
        Map(await repository.ReadPublicationInTransactionAsync(freshSubjectId, target, token));

    public async Task<ImmutableArray<ModerationRecognitionContinuityResult>> ReadResourcesInTransactionAsync(
        Guid freshSubjectId, ModerationCapability capability, ImmutableArray<AuthorityScope> targets,
        CancellationToken token) =>
        (await repository.ReadResourcesInTransactionAsync(freshSubjectId, capability, targets, token))
            .Select(Map).ToImmutableArray();

    private static ModerationRecognitionContinuityResult Map(ModerationRecognitionContinuityEvaluation result) => new()
    {
        ActionFailure = result.ActionFailure,
        AdmissionFailure = result.AdmissionFailure,
        AdmissionExcluded = result.AdmissionExcluded,
        ActionRecoveryStaged = result.ActionRecoveryStaged,
        CurrentUntil = result.CurrentUntil,
        Generation = result.Generation,
        Actions = result.ActionFailure != ModerationRecognitionContinuityFailure.None ? [] :
            result.Bindings.Select(binding =>
            {
                var item = result.CurrentActions.Single(row => row.Id == binding.CaptureItemId);
                return new ModerationRecognizedAction(binding.Id, item.SanctionId, item.DecisionId, binding.FreshSubjectId, item.Scope,
                    Enum.GetValues<ModerationCapability>().Where(capability =>
                        (item.PurposeCapabilityMask & (1 << (int)capability)) != 0).ToImmutableArray(),
                    item.StartsAt, item.EndsAt, item.ReviewAt, item.RetainUntil);
            }).ToImmutableArray()
    };
}
