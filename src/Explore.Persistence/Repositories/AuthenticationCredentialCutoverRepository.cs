using Explore.Application.Contracts.Persistence;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class AuthenticationCredentialCutoverRepository(ExploreDbContext db)
    : IAuthenticationCredentialCutoverRepository
{
    public Task<AuthenticationCredentialCutover?> GetAsync(Guid instanceId, Guid operationId,
        CancellationToken cancellationToken = default) =>
        db.AuthenticationCredentialCutovers.AsNoTracking().SingleOrDefaultAsync(
            operation => operation.InstanceId == instanceId && operation.Id == operationId, cancellationToken);

    public Task<AuthenticationCredentialCutoverHead?> GetHeadAsync(Guid instanceId,
        CancellationToken cancellationToken = default) =>
        db.AuthenticationCredentialCutoverHeads.AsNoTracking().SingleOrDefaultAsync(
            head => head.InstanceId == instanceId, cancellationToken);

    public async Task AddAsync(AuthenticationCredentialCutover operation,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        db.AuthenticationCredentialCutovers.Add(operation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AddHeadAsync(AuthenticationCredentialCutoverHead head,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        db.AuthenticationCredentialCutoverHeads.Add(head);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(AuthenticationCredentialCutover operation, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        var tracked = db.AuthenticationCredentialCutovers.Local.SingleOrDefault(candidate => candidate.Id == operation.Id);
        var entity = operation;
        if (tracked is not null && !ReferenceEquals(tracked, operation))
        {
            db.Entry(tracked).CurrentValues.SetValues(operation);
            entity = tracked;
        }
        else if (tracked is null)
            db.AuthenticationCredentialCutovers.Attach(operation);
        db.Entry(entity).State = EntityState.Modified;
        db.Entry(entity).Property(candidate => candidate.ConcurrencyStamp).OriginalValue = expectedConcurrencyStamp;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveHeadAsync(AuthenticationCredentialCutoverHead head, Guid expectedConcurrencyStamp,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();
        var tracked = db.AuthenticationCredentialCutoverHeads.Local.SingleOrDefault(candidate => candidate.InstanceId == head.InstanceId);
        var entity = head;
        if (tracked is not null && !ReferenceEquals(tracked, head))
        {
            db.Entry(tracked).CurrentValues.SetValues(head);
            entity = tracked;
        }
        else if (tracked is null)
            db.AuthenticationCredentialCutoverHeads.Attach(head);
        db.Entry(entity).State = EntityState.Modified;
        db.Entry(entity).Property(candidate => candidate.ConcurrencyStamp).OriginalValue = expectedConcurrencyStamp;
        await db.SaveChangesAsync(cancellationToken);
    }

    private void EnsureTransaction()
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("authentication_cutover_transaction_required");
    }
}
