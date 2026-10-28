using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationEventScopeQualificationTests
{
    [Test]
    [Arguments(ModerationScopeKind.Event, 0, true)]
    [Arguments(ModerationScopeKind.Resource, 0, false)]
    [Arguments(ModerationScopeKind.Event, 1, false)]
    [Arguments(ModerationScopeKind.Event, 2, false)]
    [Arguments(ModerationScopeKind.Event, 3, false)]
    [Arguments(ModerationScopeKind.Event, 4, false)]
    public async Task ContentCaseRequiresTheActualEventAndExactNativeScope(
        ModerationScopeKind kind, int substitution, bool accepted)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var resourceId = await ModerationResourceGenerationWriterTests.SeedAsync(database);
        Guid eventId;
        await using (var reader = database.Context())
            eventId = await reader.EventResources.Where(row => row.Id == resourceId)
                .Select(row => row.EventId).SingleAsync();
        if (substitution == 4)
        {
            await using var writer = database.Context();
            await writer.Events.Where(row => row.Id == eventId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.IsDeleted, true));
        }
        var tenant = substitution == 3
            ? AuthorityScope.Tenant(database.Scope.Parent!, Guid.CreateVersion7())
            : database.Scope;
        var targetId = substitution == 1 ? resourceId : eventId;
        var target = kind == ModerationScopeKind.Event
            ? AuthorityScope.Event(tenant, targetId)
            : AuthorityScope.Resource(tenant, targetId);
        var request = database.OpenCase(target) with
        {
            SubjectKind = ModerationSubjectKind.Content,
            SubjectId = substitution == 2 ? resourceId : eventId
        };

        var result = await database.ExecuteAsync(database.AdministratorId, request);
        await Assert.That(result.IsSuccess).IsEqualTo(accepted);
        await using var persisted = database.Context();
        persisted.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await persisted.ModerationCases.CountAsync()).IsEqualTo(accepted ? 1 : 0);
        await Assert.That(await persisted.ModerationQualificationMutationReceipts
            .AnyAsync(row => row.Id == request.OperationId)).IsEqualTo(accepted);
        await Assert.That((await persisted.ModerationQualificationEpochs.SingleAsync()).Revision)
            .IsEqualTo(accepted ? 2L : 1L);
        if (accepted)
        {
            var moderationCase = await persisted.ModerationCases.SingleAsync();
            await Assert.That(moderationCase.Scope).IsEqualTo(target);
            await Assert.That(moderationCase.ScopeEventId).IsEqualTo(eventId);
            await Assert.That(moderationCase.ScopeResourceId).IsNull();
            await Assert.That(moderationCase.Subject!.Origin).IsEqualTo(database.Scope);
        }
    }
}
