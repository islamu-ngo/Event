using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Security;

/// <summary>Retains the originally selected identity/application pair and its source custody during projections.</summary>
internal static class IdentityWriterHumanSourceQueries
{
    internal static Task<HumanPrincipalSourceFacts> ReadPrincipalAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        LocalSessionAuthority? local, CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, local is not null);
        return HumanPrincipalSourceProjection.ReadAsync(owner.Application, owner.Identity,
            subject, local, owner.RequireCurrent, cancellationToken);
    }

    internal static Task<HumanAuthorizationRouteFacts> ReadRouteAsync(
        LocalIdentityExecutionLease execution, HumanCommitSubject subject,
        CancellationToken cancellationToken = default)
    {
        var owner = new OriginalRead(execution, includeIdentity: false);
        return HumanAuthorizationRouteProjection.ReadAsync(owner.Application, subject,
            owner.Application.TenantFilterTenantId, owner.RequireCurrent, cancellationToken);
    }

    private sealed class OriginalRead
    {
        private readonly LocalIdentityExecutionLease _execution;
        private readonly IDbContextTransaction? _applicationTransaction;
        private readonly IDbContextTransaction? _identityTransaction;
        private readonly bool _includeIdentity;
        internal ExploreDbContext Application { get; }
        internal DbContext Identity { get; }

        internal OriginalRead(LocalIdentityExecutionLease execution, bool includeIdentity)
        {
            _execution = execution;
            Application = execution.Application ?? throw new UnauthorizedAccessException("The selected application owner is required.");
            Identity = execution.Identity ?? throw new UnauthorizedAccessException("The selected identity owner is required.");
            _applicationTransaction = Application.Database.CurrentTransaction;
            _identityTransaction = Identity.Database.CurrentTransaction;
            _includeIdentity = includeIdentity;
            RequireCurrent();
        }

        internal void RequireCurrent()
        {
            _execution.RequireCurrent(Application, Identity);
            HumanSecuritySourceGate.RequireCurrent(Application);
            if (_includeIdentity)
                LocalIdentityLifecycleGateCustody.RequireHeld(Identity);
            if (!ReferenceEquals(Application.Database.CurrentTransaction, _applicationTransaction)
                || !ReferenceEquals(Identity.Database.CurrentTransaction, _identityTransaction))
                throw new UnauthorizedAccessException("The original human source transaction changed.");
        }

    }
}
