using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Nominates one executable profile procedure beneath the exact accepted child-age source.
/// Native owners authenticate the requester, parent adoption and independent qualification;
/// no individual applicability, consent, sensitive input or collection grant belongs here.
/// </summary>
public sealed record RecordModerationProfileCollectionProcedureSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required Guid ChildAgeSourceId { get; init; }
    public required long ExpectedChildAgeSourceRevision { get; init; }
    public required long ExpectedChildAgeSourceWriterGeneration { get; init; }
    public required Guid ChildAgeAdoptionId { get; init; }
    public required long ExpectedChildAgeAdoptionRevision { get; init; }
    public required long ExpectedChildAgeAdoptionWriterGeneration { get; init; }
    public required ModerationProfileCollectionProcedure Procedure { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record AdoptModerationProfileCollectionProcedureSource : ModerationQualificationMutation
{
    public required Guid SourceId { get; init; }
    public required long ExpectedSourceRevision { get; init; }
    public required long ExpectedSourceWriterGeneration { get; init; }
    public required Guid AdoptionId { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
}

public sealed record WithdrawModerationProfileCollectionProcedureAdoption : ModerationQualificationMutation
{
    public required Guid AdoptionId { get; init; }
    public required long ExpectedAdoptionRevision { get; init; }
}

/// <summary>
/// Current procedure provenance on one original native transaction, not permission to
/// collect a subject's profile. Individual assessment and prerequisite owners remain required.
/// </summary>
public sealed record ModerationProfileCollectionProcedureObservation(
    Guid TransactionId, ModerationProfileCollectionProcedureSource Source,
    ModerationProfileCollectionProcedureAdoption Adoption,
    DateTimeOffset ObservedAt, DateTimeOffset ValidUntil);
