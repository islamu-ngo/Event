using Explore.Application.Services.Moderation;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Expected coordinates of an original native observation, never a binding, identity claim
/// or currentness verdict. Native owners reread the original subject and accepted parents.
/// </summary>
public sealed record ModerationProfileCollectionAssessmentOriginal(
    Guid ObservationId,
    long ExpectedObservationRevision,
    long ExpectedObservationGeneration,
    AuthorityScope Jurisdiction,
    Guid PolicyId,
    long PolicyRevision,
    Guid PublicationId,
    Guid ProfileId,
    ModerationNativeMode Mode,
    Guid ProcedureSourceId,
    long ExpectedProcedureSourceRevision,
    long ExpectedProcedureSourceWriterGeneration,
    Guid ProcedureAdoptionId,
    long ExpectedProcedureAdoptionRevision,
    long ExpectedProcedureAdoptionWriterGeneration);

/// <summary>
/// Nominated owner's complete proposal, including positive empty. Required input is copied
/// and bounded for native validation; omitted input is never interpreted as an empty set.
/// ExpectedGeneration is the previous subject/procedure assessment-slot generation (zero initially).
/// </summary>
public sealed record RecordModerationProfileCollectionAssessment
{
    private readonly IReadOnlyList<ModerationCollectionPrerequisiteKind> required = null!;
    public required ModerationProfileCollectionAssessmentOriginal Original { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required IReadOnlyList<ModerationCollectionPrerequisiteKind> Required
    {
        get => required;
        init => required = value is null ? null! : Array.AsReadOnly(value.Take(4).ToArray());
    }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>
/// Exact-source independent acceptance nomination. It cannot replace the original subject,
/// required set, observation time, binding or accepted parent window.
/// </summary>
public sealed record AdoptModerationProfileCollectionAssessment
{
    public required ModerationProfileCollectionAssessmentOriginal Original { get; init; }
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceGeneration { get; init; }
    public required long ExpectedGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

/// <summary>
/// Exact retained assessment coordinates, not an authenticated binding or applicability verdict.
/// Withdrawal is corrective and does not require a live procedure or extend any original window.
/// </summary>
public sealed record WithdrawModerationProfileCollectionAssessment(
    AuthorityScope Jurisdiction,
    Guid SubjectHumanId,
    Guid PolicyId,
    long PolicyRevision,
    Guid PublicationId,
    Guid ProfileId,
    ModerationNativeMode Mode,
    Guid SourceId,
    long ExpectedSourceGeneration,
    Guid AdoptionId,
    long ExpectedAdoptionGeneration,
    long ExpectedAdoptionRevision);

/// <summary>
/// Native original-observation proposal, independent acceptance and terminal correction.
/// These entities are private finite metadata, not consent or collection receipts.
/// </summary>
public interface IModerationProfileCollectionAssessmentRepository
{
    /// <summary>Records only the exact nominated qualified owner's explicit complete proposal.</summary>
    Task<ModerationProfileCollectionAssessmentSource?> RecordAsync(
        RecordModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default);

    /// <summary>Independently accepts one latest exact source without renewing its original window.</summary>
    Task<ModerationProfileCollectionAssessmentAdoption?> AdoptAsync(
        AdoptModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the exact withdrawn entity after the real identity-owned primary commit,
    /// or null when original coordinates, nominated identity or final currentness do not qualify.
    /// Cancellation propagates; callers must not supply an existing unit of work.
    /// </summary>
    Task<ModerationProfileCollectionAssessmentAdoption?> WithdrawAsync(
        WithdrawModerationProfileCollectionAssessment request, CancellationToken cancellationToken = default);
}
