using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    internal ITenantEnrollmentAdmissionWriter CreateVisitorAdmissionWriter(Guid enrollmentOperationId)
    {
        RequireVisitorPreparationOwner();
        return new LocalVisitorTenantEnrollmentAdmissionWriter(this,
            executionLease ?? throw new UnauthorizedAccessException("The original Local identity owner is absent."),
            enrollmentOperationId);
    }

    private sealed class LocalVisitorTenantEnrollmentAdmissionWriter : ITenantEnrollmentAdmissionWriter
    {
        private readonly LocalIdentityCredentialStateStore store;
        private readonly LocalIdentityExecutionLease lease;
        private readonly Guid enrollmentOperationId;
        private readonly Guid tenantId;

        internal LocalVisitorTenantEnrollmentAdmissionWriter(
            LocalIdentityCredentialStateStore store, LocalIdentityExecutionLease lease, Guid enrollmentOperationId)
        {
            this.store = store;
            this.lease = lease;
            this.enrollmentOperationId = enrollmentOperationId;
            tenantId = store.ApplicationOwner.TenantFilterTenantId
                ?? throw new UnauthorizedAccessException("Visitor admission requires its original tenant.");
            RequireCurrent();
        }

        public Guid TenantId
        {
            get { RequireCurrent(); return tenantId; }
        }

        public AuthenticationProviderKind Provider => AuthenticationProviderKind.Local;

        public async Task<TenantUser> AdmitAsync(
            Func<CancellationToken, Task> authorizeNewMembership, CancellationToken cancellationToken)
        {
            RequireCurrent();
            var receipt = await store.AdmitVisitorAsync(enrollmentOperationId, authorizeNewMembership, cancellationToken)
                .ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original visitor admission is unavailable.");
            RequireCurrent();
            var member = await store.ApplicationOwner.Set<TenantUser>().AsNoTracking().SingleOrDefaultAsync(
                row => row.Id == receipt.TenantUserId && row.TenantId == tenantId
                    && row.UserId == receipt.LocalSubjectId && row.ActorId == receipt.PersonalActorId
                    && row.StatusId == (int)TenantUserStatusEnum.Active && !row.IsDeleted,
                cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original visitor membership changed.");
            RequireCurrent();
            return member;
        }

        private void RequireCurrent()
        {
            lease.RequireCurrent(store.ApplicationOwner, store.IdentityOwner);
            store.RequireSelectedLifecycleCurrent();
            EventDiscoveryEnrollmentGate.RequireCurrent(store.ApplicationOwner);
            if (tenantId == Guid.Empty || enrollmentOperationId == Guid.Empty
                || store.ApplicationOwner.TenantFilterTenantId != tenantId)
                throw new UnauthorizedAccessException("The original visitor admission owner changed.");
        }
    }
}
