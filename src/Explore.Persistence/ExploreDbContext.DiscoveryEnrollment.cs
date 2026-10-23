using System.Data.Common;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence;

public partial class ExploreDbContext
{
    private readonly Dictionary<Guid, DiscoveryEnrollmentCreation> _discoveryEnrollmentCreations = [];

    internal IEnumerable<Guid> DiscoveryEnrollmentCreationIds => _discoveryEnrollmentCreations.Keys;

    private DiscoveryEnrollmentCreation[] PrepareDiscoveryEnrollmentSave()
    {
        Tenant[] added = ChangeTracker.Entries<Tenant>()
            .Where(entry => entry.State == EntityState.Added).Select(entry => entry.Entity).ToArray();
        if (added.Length == 0 && _discoveryEnrollmentCreations.Count == 0)
            return [];

        DisclosureMutations.RequireEnrollmentCreation();
        RequireDiscoveryEnrollmentCreations();
        List<DiscoveryEnrollmentCreation> pending = [];
        foreach (Tenant tenant in added)
        {
            if (_discoveryEnrollmentCreations.ContainsKey(tenant.Id))
                continue;
            var supplied = ChangeTracker.Entries<EventDiscoveryRevision>()
                .Where(entry => entry.Entity.TenantId == tenant.Id).ToArray();
            if (supplied.Length != 0)
            {
                // Explicit seed/native images already have their own identity and
                // epochs. They are not synthesized creation receipts or repairs.
                if (supplied.Length != 1 || supplied[0].State != EntityState.Added
                    || supplied[0].Entity.Id == Guid.Empty || supplied[0].Entity.Id.Version != 7
                    || supplied[0].Entity.IdentityEpoch < 0 || supplied[0].Entity.DisclosureEpoch < 0)
                    throw new InvalidOperationException("discovery_enrollment_creation_image_changed");
                continue;
            }

            if (tenant.Id == Guid.Empty || tenant.Id.Version != 7)
                throw new InvalidOperationException("discovery_enrollment_creation_image_changed");
            var revision = new EventDiscoveryRevision
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenant.Id,
                IdentityEpoch = 0,
                DisclosureEpoch = 1
            };
            Set<EventDiscoveryRevision>().Add(revision);
            var creation = new DiscoveryEnrollmentCreation(tenant, revision,
                RelationalConnectionIdentity.Capture(this), Database.CurrentTransaction!.GetDbTransaction());
            _discoveryEnrollmentCreations.Add(tenant.Id, creation);
            pending.Add(creation);
        }
        DisclosureMutations.RequireEnrollmentCapacity();
        return pending.ToArray();
    }

    private void CompleteDiscoveryEnrollmentSave(DiscoveryEnrollmentCreation[] pending, int saved)
    {
        if (saved < checked(pending.Length * 2))
            throw new InvalidOperationException("discovery_enrollment_creation_save_incomplete");
        foreach (var creation in pending)
        {
            RequireDiscoveryEnrollmentCreation(creation, requireCompleted: false);
            creation.Completed = true;
        }
    }

    private void FailDiscoveryEnrollmentSave()
    {
        foreach (var creation in _discoveryEnrollmentCreations.Values)
            creation.Failed = true;
    }

    internal void InvalidateDiscoveryEnrollmentSavepoint(DbTransaction transaction)
    {
        bool invalidated = false;
        foreach (var creation in _discoveryEnrollmentCreations.Values)
        {
            if (!ReferenceEquals(creation.Transaction, transaction))
                continue;
            creation.Failed = true;
            invalidated = true;
        }
        if (invalidated)
            DisclosureMutations.InvalidateEnrollmentCreation();
    }

    internal Guid[] RequireDiscoveryEnrollmentCreations()
    {
        foreach (var creation in _discoveryEnrollmentCreations.Values)
            RequireDiscoveryEnrollmentCreation(creation, requireCompleted: true);
        return _discoveryEnrollmentCreations.Keys.ToArray();
    }

    private void RequireDiscoveryEnrollmentCreation(DiscoveryEnrollmentCreation creation, bool requireCompleted)
    {
        RequireBoundTenantLease();
        EventDiscoveryEnrollmentGate.RequireCurrent(this);
        if (creation.Failed || requireCompleted && !creation.Completed
            || !creation.Connection.Matches(this)
            || !ReferenceEquals(Database.CurrentTransaction?.GetDbTransaction(), creation.Transaction)
            || !creation.Connection.Owns(creation.Transaction)
            || creation.Tenant.Id != creation.TenantId || creation.Revision.Id != creation.RevisionId
            || creation.Revision.TenantId != creation.TenantId || creation.Revision.IdentityEpoch != 0
            || creation.Revision.DisclosureEpoch != 1
            || Entry(creation.Tenant).State is EntityState.Detached or EntityState.Deleted
            || Entry(creation.Revision).State is not (EntityState.Added or EntityState.Unchanged))
            throw new InvalidOperationException("discovery_enrollment_creation_image_changed");
    }

    internal void ForgetDiscoveryEnrollmentCreation(Guid tenantId) => _discoveryEnrollmentCreations.Remove(tenantId);

    internal void ResetDiscoveryEnrollmentCreations()
    {
        foreach (var creation in _discoveryEnrollmentCreations.Values)
        {
            // Failed/unaccepted inserts must not leak a synthesized Added entry
            // into a fresh attempt. Never detach unrelated caller-supplied seeds.
            if (Entry(creation.Revision).State == EntityState.Added)
                Entry(creation.Revision).State = EntityState.Detached;
        }
        _discoveryEnrollmentCreations.Clear();
    }

    private sealed class DiscoveryEnrollmentCreation(Tenant tenant, EventDiscoveryRevision revision,
        RelationalConnectionIdentity connection, DbTransaction transaction)
    {
        internal Tenant Tenant { get; } = tenant;
        internal EventDiscoveryRevision Revision { get; } = revision;
        internal Guid TenantId { get; } = tenant.Id;
        internal Guid RevisionId { get; } = revision.Id;
        internal RelationalConnectionIdentity Connection { get; } = connection;
        internal DbTransaction Transaction { get; } = transaction;
        internal bool Completed { get; set; }
        internal bool Failed { get; set; }
    }
}
