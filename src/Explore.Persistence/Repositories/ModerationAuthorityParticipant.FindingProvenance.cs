using Explore.Application.Contracts.Moderation;
using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationAuthorityParticipant
{
    private FindingProvenanceSession? _findingProvenance;

    /// <summary>The actual local parent acquisition, never a caller-constructed grant or copied metadata.</summary>
    internal sealed class FindingProvenanceSession(
        ModerationAuthorityParticipant owner, Acquisition acquired, ExploreDbContext acquiredContext)
        : IModerationFindingProvenanceSession
    {
        internal Guid CaseId => acquired.Case!.Id;

        internal async Task<ModerationSourceAuthority?> AcquireAsync(
            ExploreDbContext receivingContext, Guid caseId, CancellationToken token)
        {
            if (!ReferenceEquals(owner._findingProvenance, this) || !ReferenceEquals(acquiredContext, receivingContext)
                || acquired.Case?.Id != caseId
                || !ReferenceEquals(receivingContext.Database.CurrentTransaction, acquired.Transaction))
                return null;
            var current = owner._acquisition ?? acquired;
            if (!ReferenceEquals(current.Session, acquired.Session)
                || !ReferenceEquals(current.Operation, acquired.Operation)
                || current.Case?.Id != caseId || current.Source != acquired.Source
                || current.Grant.Principal != acquired.Grant.Principal || current.Grant.Subject != acquired.Grant.Subject
                || current.Machine != acquired.Machine
                || current.Grant.Principal.Kind == ModerationSubjectKind.Service
                    && (current.Machine is null || current.Operation.Kind != ModerationOperationKind.Reconcile
                        || current.Grant.Principal.Id != current.Source.Binding.MaintenanceServicePrincipalId)
                || !await owner.CurrentAsync(current, token))
                return null;
            return current.Source;
        }
    }
}
