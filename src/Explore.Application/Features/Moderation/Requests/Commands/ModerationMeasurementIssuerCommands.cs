using Explore.Application.Contracts.Persistence;

namespace Explore.Application.Features.Moderation.Requests.Commands;

public sealed record EnrollModerationMeasurementIssuerCommand : ModerationQualificationCommand
{
    public required Guid EnrollmentId { get; init; }
    public required Guid KeyId { get; init; }
    public required string Issuer { get; init; }
    public required string PublicKeySpkiBase64 { get; init; }
    public required string RunnerDigest { get; init; }
    public required string CatalogueDigest { get; init; }
    public required string TestBundleDigest { get; init; }
    public required DateTimeOffset ValidUntil { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new EnrollModerationMeasurementIssuerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        EnrollmentId = EnrollmentId,
        KeyId = KeyId,
        Issuer = Issuer,
        PublicKeySpkiBase64 = PublicKeySpkiBase64,
        RunnerDigest = RunnerDigest,
        CatalogueDigest = CatalogueDigest,
        TestBundleDigest = TestBundleDigest,
        ValidUntil = ValidUntil
    };
}

public sealed record RevokeModerationMeasurementIssuerCommand : ModerationQualificationCommand
{
    public required Guid EnrollmentId { get; init; }
    public required long ExpectedEnrollmentRevision { get; init; }
    internal override ModerationQualificationMutation ToMutation() => new RevokeModerationMeasurementIssuerSource
    {
        OperationId = OperationId,
        Target = Target,
        ExpectedQualificationRevision = ExpectedQualificationRevision,
        EnrollmentId = EnrollmentId,
        ExpectedEnrollmentRevision = ExpectedEnrollmentRevision
    };
}
