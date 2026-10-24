using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed partial class ModerationProfileCollectionAdmissionOwner
{
    public Task<PrivateBirthDateStatus> ReadBirthDateStatusAsync(Guid subjectId,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Private correction requires its own clean native owner.");
        return privacy.ExecuteEnrollmentSerializedAsync(async token =>
        {
            await RequireSelfAsync(subjectId, token);
            var pii = await context.Set<UserPii>().AsNoTracking().SingleAsync(row => row.UserId == subjectId, token);
            return new PrivateBirthDateStatus(pii.ReadBirthDateRevision(), pii.ReadDeclaredBirthDate() is not null);
        }, token => RequireSelfAsync(subjectId, token), cancellationToken);
    }

    public async Task RecordBirthDateAsync(Guid subjectId, DateOnly birthDate, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        await RequireCurrentAsync(subjectId, cancellationToken);
        if (birthDate == default || birthDate > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
            throw new ArgumentException("A declaration must be a real date no later than today.", nameof(birthDate));
        var retained = current!;
        var metadata = await ReadAsync(retained.Acquisition, retained.Expected, retained.ConsentId, cancellationToken)
            ?? throw new AuthorizationException("Current native profile collection admission is required.");
        var pii = await context.Set<UserPii>().SingleAsync(row => row.UserId == subjectId, cancellationToken);
        RequireBirthRevision(pii, expectedRevision);
        var original = retained.Expected.Original;
        var receipt = UserBirthDateCollectionReceipt.Record(subjectId, retained.Receipt, original.ObservationId,
            retained.Expected.SourceId, retained.Expected.AdoptionId, retained.ConsentId, original.PolicyId,
            original.PolicyRevision, original.PublicationId, original.ProfileId, clock.GetUtcNow());
        var previous = context.ChangeTracker.Entries<UserBirthDateCollectionReceipt>()
            .Where(entry => entry.Entity.UserId == subjectId).ToArray();
        if (previous.Any(entry => entry.State != EntityState.Unchanged))
            throw new InvalidOperationException("A private receipt correction requires saved native state.");
        // One current receipt only; clearing or correction does not retain a private DOB history.
        await context.Set<UserBirthDateCollectionReceipt>().Where(row => row.UserId == subjectId)
            .ExecuteDeleteAsync(cancellationToken);
        foreach (var entry in previous) entry.State = EntityState.Detached;
        pii.RecordDeclaredBirthDate(birthDate, expectedRevision, retained.Receipt, metadata.Binding,
            metadata.Assessment, metadata.Prerequisites, clock.GetUtcNow());
        context.Add(receipt);
        await context.SaveChangesAsync(cancellationToken);
        await RequireCurrentAsync(subjectId, cancellationToken);
    }

    public async Task ClearBirthDateAsync(Guid subjectId, long expectedRevision,
        CancellationToken cancellationToken = default)
    {
        if (context.Database.CurrentTransaction is not null || context.ChangeTracker.HasChanges())
            throw new InvalidOperationException("Private correction requires its own clean native owner.");
        async Task EnsureAsync(CancellationToken token)
        {
            await RequireSelfAsync(subjectId, token);
        }
        await privacy.ExecuteEnrollmentSerializedAsync(token => new EfCoreUnitOfWork(context)
            .ExecuteRecognitionEnrollmentAsync(async inner =>
            {
                await EnsureAsync(inner);
                await RelationalEntityRowFence.AcquireGlobalAsync<User>(context, subjectId, inner);
                await RelationalEntityRowFence.AcquireGlobalAsync<UserPii>(context, subjectId, inner);
                var pii = await context.Set<UserPii>().SingleAsync(row => row.UserId == subjectId, inner);
                await context.Entry(pii).ReloadAsync(inner);
                RequireBirthRevision(pii, expectedRevision);
                pii.ClearDeclaredBirthDate(expectedRevision);
                await context.Set<UserBirthDateCollectionReceipt>().Where(row => row.UserId == subjectId).ExecuteDeleteAsync(inner);
                await context.SaveChangesAsync(inner);
                return true;
            }, EnsureAsync, token), EnsureAsync, cancellationToken);
    }

    private static void RequireBirthRevision(UserPii pii, long expected)
    {
        if (expected < 0 || expected != pii.ReadBirthDateRevision() || expected == long.MaxValue)
            throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                "The private declaration changed. Reload its revision and retry.");
    }
}
