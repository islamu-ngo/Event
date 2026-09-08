// ABOUTME: Shared authority port for current and complete proposed visitor-access policy states.
// ABOUTME: Callers own the ordered settings lease and transaction before any authoritative read.

using Explore.Application.Models;

namespace Explore.Application.Contracts.Services;

public interface IVisitorAccessCapabilityResolver
{
    /// <summary>
    /// Uses uncached reads. For write authority, acquire
    /// the FULL VisitorAccessCapabilityResolver.AuthoritySettingKeys group through
    /// ISettingMutationLock.ExecuteOrderedGroupsAsync BEFORE ExecuteSerializableAsync
    /// starts the transaction/snapshot, and retain that outer lease through commit.
    /// ExecuteManyAsync opens a transaction and is not the outer-lease API.
    /// This resolver does not acquire leases, begin transactions, or use cached settings.
    /// </summary>
    Task<VisitorAccessCapability> ResolveAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pure evaluation of the complete FINAL effective state, including every provider.
    /// Caller composes overrides, removals and lock inheritance without temporary writes.
    /// </summary>
    VisitorAccessCapability Evaluate(VisitorAccessPolicyState proposedState);
}
