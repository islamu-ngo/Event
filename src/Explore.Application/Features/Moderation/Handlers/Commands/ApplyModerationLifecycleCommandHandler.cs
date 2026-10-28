using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using FluentValidation;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Application.Features.Moderation.Handlers.Commands;

public sealed class ApplyModerationLifecycleCommandHandler(ModerationOperationService operations)
    : ICommandHandler<ApplyModerationLifecycleCommand, ModerationLifecycleResponse>
{
    public async Task<ModerationLifecycleResponse> ExecuteAsync(
        ApplyModerationLifecycleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = await new ModerationLifecycleValidator().ValidateAsync(command, cancellationToken);
        if (!validation.IsValid)
            return ModerationLifecycleResponse.Invalid(validation.Errors.Select(error => error.ErrorMessage));

        var result = await operations.ExecuteAsync(command.Operation, cancellationToken);
        return ModerationLifecycleResponse.FromOutcome(command.Operation.OperationId, result);
    }
}

/// <summary>Validates mutation structure, never permission, adopted policy or activation qualification.</summary>
public sealed class ModerationLifecycleValidator : AbstractValidator<ApplyModerationLifecycleCommand>
{
    public ModerationLifecycleValidator()
    {
        RuleFor(command => command.Operation).NotNull().Custom((operation, context) =>
        {
            if (operation is null) return;
            if (!IsUuidV7(operation.OperationId) || !IsUuidV7(operation.EffectId)
                || !IsUuidV7(operation.DecisionId) || operation.ExpectedRevision < 1
                || operation.ExpectedPolicyRevision < 1 || operation.Subject is null || operation.Target is null
                || operation.Subject.TrustDomainId != operation.Target.TrustDomainId || operation.Capability is not null)
            {
                context.AddFailure("Lifecycle mutation requires stable UUIDv7 identities, qualified coordinates and positive revisions.");
                return;
            }

            var valid = operation.Kind switch
            {
                ModerationOperationKind.Propose => operation.ReferenceId == Guid.Empty
                    && operation.ExpectedSanctionRevision == 0 && operation.SupersededSanctionId == Guid.Empty
                    && operation.SupersededScope is null
                    && operation.Proposal is { } proposal && IsUuidV7(proposal.CaseId)
                    && proposal.Rule is { } rule && IsUuidV7(rule.OwnerId) && rule.Revision > 0
                    && !string.IsNullOrWhiteSpace(rule.Code) && rule.Code.Length <= 100
                    && rule.Code.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.')
                    && proposal.Capabilities is { Count: > 0 }
                    && proposal.Capabilities.All(capability => Enum.IsDefined(capability)
                        && !EffectiveRestrictions.IsProtectedRemedy(capability))
                    && proposal.RequiredApprovals is 1 or 2
                    && (operation.Target.Kind is not (ModerationScopeKind.TrustDomain or ModerationScopeKind.Solution)
                        || proposal.RequiredApprovals == 2)
                    && Instant(proposal.StartsAt)
                    && (proposal.EndsAt is { } endsAt
                        ? Instant(endsAt) && endsAt > proposal.StartsAt
                        : proposal.ReviewAt is not null && proposal.RequiredApprovals == 2)
                    && (proposal.ReviewAt is not { } reviewAt
                        || Instant(reviewAt) && reviewAt > proposal.StartsAt
                            && (proposal.EndsAt is null || reviewAt <= proposal.EndsAt)),
                ModerationOperationKind.Reconcile => IsUuidV7(operation.ReferenceId)
                    && operation.ExpectedSanctionRevision > 0 && operation.Proposal is null
                    && operation.SupersededSanctionId == Guid.Empty && operation.SupersededScope is null,
                ModerationOperationKind.Supersede => IsUuidV7(operation.ReferenceId)
                    && IsUuidV7(operation.SupersededSanctionId)
                    && operation.ReferenceId != operation.SupersededSanctionId
                    && operation.ExpectedSanctionRevision > 0 && operation.Proposal is null
                    && operation.SupersededScope is { } oldScope
                    && oldScope.TrustDomainId == operation.Subject.TrustDomainId,
                ModerationOperationKind.AcceptFinding or ModerationOperationKind.Activate or ModerationOperationKind.Revoke =>
                    IsUuidV7(operation.ReferenceId) && operation.Proposal is null
                    && operation.ExpectedSanctionRevision == 0 && operation.SupersededSanctionId == Guid.Empty
                    && operation.SupersededScope is null,
                ModerationOperationKind.RecordResponse or ModerationOperationKind.Approve
                    or ModerationOperationKind.Reject or ModerationOperationKind.Withdraw =>
                    operation.ReferenceId == Guid.Empty && operation.Proposal is null
                    && operation.ExpectedSanctionRevision == 0 && operation.SupersededSanctionId == Guid.Empty
                    && operation.SupersededScope is null,
                _ => false
            };
            if (!valid)
                context.AddFailure("Lifecycle intent must be a supported mutation with matching references and proposal terms.");
        });
    }

    private static bool IsUuidV7(Guid value) => value != Guid.Empty && value.Version == 7 && value.Variant is >= 8 and <= 11;
    private static bool Instant(DateTimeOffset value) => value != DateTimeOffset.MinValue && value != DateTimeOffset.MaxValue;
}
