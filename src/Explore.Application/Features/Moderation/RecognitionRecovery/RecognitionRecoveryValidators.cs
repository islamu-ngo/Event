using FluentValidation;

namespace Explore.Application.Features.Moderation.RecognitionRecovery;

internal static class RecognitionRecoveryValidation
{
    public static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
}

public sealed class AssessRecognitionRecoveryCommandValidator : AbstractValidator<AssessRecognitionRecoveryCommand>
{
    public AssessRecognitionRecoveryCommandValidator()
    {
        RuleFor(row => row.OperationId).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.IntentId).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.SourceId).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.AdoptionId).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.ExpectedPendingRevision).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(row => row.ExpectedSourceRevision).Equal(1);
        RuleFor(row => row.ExpectedAdoptionRevision).Equal(1);
        RuleFor(row => row.ExpectedSourceGeneration).InclusiveBetween(1, long.MaxValue - 1);
        RuleFor(row => row.ExpectedAdoptionGeneration).GreaterThan(row => row.ExpectedSourceGeneration);
        RuleFor(row => row.Grounds).IsInEnum();
        RuleFor(row => row.ValidUntil).Must(value => value.Offset == TimeSpan.Zero
            && value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue);
    }
}

public sealed class AuthorizeRecognitionRecoveryCommandValidator : AbstractValidator<AuthorizeRecognitionRecoveryCommand>
{
    public AuthorizeRecognitionRecoveryCommandValidator()
    {
        RuleFor(row => row.Id).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.ExpectedRevision).Equal(1);
    }
}

public sealed class AcknowledgeRecognitionRecoveryCommandValidator : AbstractValidator<AcknowledgeRecognitionRecoveryCommand>
{
    public AcknowledgeRecognitionRecoveryCommandValidator() =>
        RuleFor(row => row.Id).Must(RecognitionRecoveryValidation.IsUuidV7);
}

public sealed class GetRecognitionRecoveryEscalationsQueryValidator : AbstractValidator<GetRecognitionRecoveryEscalationsQuery>
{
    public GetRecognitionRecoveryEscalationsQueryValidator()
    {
        RuleFor(row => row.Limit).InclusiveBetween(1, 100);
        RuleFor(row => row.AfterId).Must(value => value is null || RecognitionRecoveryValidation.IsUuidV7(value.Value));
    }
}

public sealed class GetRecognitionRecoveryEscalationQueryValidator : AbstractValidator<GetRecognitionRecoveryEscalationQuery>
{
    public GetRecognitionRecoveryEscalationQueryValidator() =>
        RuleFor(row => row.Id).Must(RecognitionRecoveryValidation.IsUuidV7);
}

public sealed class GetRecognitionRecoveryContextQueryValidator : AbstractValidator<GetRecognitionRecoveryContextQuery>
{
    public GetRecognitionRecoveryContextQueryValidator() =>
        RuleFor(row => row.EscalationId).Must(RecognitionRecoveryValidation.IsUuidV7);
}

public sealed class GetRecognitionCorrectionProofsQueryValidator : AbstractValidator<GetRecognitionCorrectionProofsQuery>
{
    public GetRecognitionCorrectionProofsQueryValidator()
    {
        RuleFor(row => row.AuthenticatedIntentId).Must(RecognitionRecoveryValidation.IsUuidV7);
        RuleFor(row => row.Receipt).NotEmpty().MaximumLength(128);
    }
}
