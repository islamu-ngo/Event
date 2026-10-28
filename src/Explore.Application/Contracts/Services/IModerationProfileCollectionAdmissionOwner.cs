using Explore.Application.Contracts.Persistence;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Services;

/// <summary>Private admission display metadata, not transferable collection authority.</summary>
public sealed record ModerationProfileCollectionAdmission(Guid SubjectId, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationProfileCollectionAdmission);
}

/// <summary>Editor nomination only; every consuming request must reacquire its native originals.</summary>
public sealed record ModerationProfileCollectionOption(
    ModerationProfileSubjectConsentAssessment Assessment, Guid? ConsentId, DateTimeOffset ValidUntil)
{
    public ModerationDeclaredAgeDisplay? DeclaredAge { get; init; }
    public override string ToString() => nameof(ModerationProfileCollectionOption);
}

/// <summary>Private declared-only calendar display. Never assurance or an audience/admission permit.</summary>
public sealed record ModerationDeclaredAgeDisplay(
    ModerationAgeEvidenceKind Kind, ModerationDeclaredAgeOutcome Outcome, int MinimumAge,
    long BirthDateRevision, DateTimeOffset EvaluatedAt, DateTimeOffset ValidUntil)
{
    public override string ToString() => nameof(ModerationDeclaredAgeDisplay);
}

/// <summary>Private correction metadata. The declared date is deliberately absent.</summary>
public sealed record PrivateBirthDateStatus(long Revision, bool HasDeclaration);

/// <summary>
/// Authenticates existing-Human ProfileCompletion before invoking a body consumer and
/// retains native authority through the actual privacy owner's commit. Nominations are
/// coordinates only. HTTP owners must buffer output until this operation succeeds.
/// </summary>
public interface IModerationProfileCollectionAdmissionOwner
{
    Task<IReadOnlyList<ModerationProfileCollectionOption>> ReadOptionsAsync(Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<PrivateBirthDateStatus> ReadBirthDateStatusAsync(Guid subjectId,
        CancellationToken cancellationToken = default);

    Task<bool> ExecuteAsync(ModerationProfileSubjectConsentAssessment assessment, Guid? consentId,
        Func<ModerationProfileCollectionAdmission, CancellationToken, Task> consume,
        CancellationToken cancellationToken = default);

    /// <summary>Rejects direct handler calls outside this owner's live exact-self admission.</summary>
    Task RequireCurrentAsync(Guid subjectId, CancellationToken cancellationToken = default);

    Task RecordBirthDateAsync(Guid subjectId, DateOnly birthDate, long expectedRevision,
        CancellationToken cancellationToken = default);

    Task ClearBirthDateAsync(Guid subjectId, long expectedRevision,
        CancellationToken cancellationToken = default);
}
