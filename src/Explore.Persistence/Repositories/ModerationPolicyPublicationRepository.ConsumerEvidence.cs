using Explore.Application.Features.Moderation.Policy;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationPolicyPublicationRepository
{
    internal ModerationConsumerEvidenceSession BorrowConsumerEvidence(
        ModerationSourceAuthority source, ModerationPolicyCompiledDraft draft)
    {
        var transaction = context.Database.CurrentTransaction
            ?? throw new AbortPublication(Application.Contracts.Persistence.ModerationPolicyPublicationOutcome.Unavailable);
        var revision = source.Epoch.Revision;
        return new(context, transaction, source, draft, async token =>
        {
            var current = await new ModerationQualificationAuthority(context, admin, machine)
                .AcquireAsync(draft.Publication.Jurisdiction, draft.Publication.PublicationId, token);
            return current is not null && ReferenceEquals(context.Database.CurrentTransaction, transaction)
                && current.UserId == source.UserId && current.Binding.Id == source.Binding.Id
                && current.Binding.TrustDomainId == source.Binding.TrustDomainId
                && current.Tenant == source.Tenant && current.Instance == source.Instance
                && current.Epoch.Id == source.Epoch.Id && current.Epoch.Revision == revision
                && current.IsInstanceAdmin == source.IsInstanceAdmin
                && current.FencedHumans.Order().SequenceEqual(source.FencedHumans.Order())
                && current.Administrators.Order().SequenceEqual(source.Administrators.Order());
        }, this, inventory, consumerSources, measurementReceipts, clock, readProfileConfiguration: null);
    }
}
