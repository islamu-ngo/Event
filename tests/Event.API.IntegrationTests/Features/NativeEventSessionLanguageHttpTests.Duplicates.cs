using System.Net;
using System.Net.Http.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Domain.Constants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEventSessionLanguageHttpTests
{
    [Test]
    public async Task DuplicateCreate_ReturnsControlledValidationWithoutChangingTheAssignment()
    {
        await using var factory = await LanguageFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        var id = await CreateAsync(owner, factory.PublicSessionId, 1);
        var before = (await factory.DetailAsync(id))!;
        using var validation = await owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, int.MaxValue));
        using var duplicate = await owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, 1));
        await DuplicateProblemAsync(duplicate, validation);
        var after = (await factory.DetailAsync(id))!;
        await Assert.That(after).IsEqualTo(before);
        await Assert.That(Items(await CollectionAsync(owner, Managed(factory.PublicEventId, factory.PublicSessionId))).Length).IsEqualTo(1);
        var otherSessionId = await CreateAsync(owner, factory.DraftSessionId, 1);
        await Assert.That(otherSessionId).IsNotEqualTo(id);
        await Assert.That((await factory.DetailAsync(otherSessionId))!.EventSessionId).IsEqualTo(factory.DraftSessionId);
    }

    [Test]
    public async Task ConcurrentDuplicateCreates_KeepOneDurableWinnerAndOneControlledFailure()
    {
        await using var factory = await LanguageFactory.CreateAsync();
        using var owner = factory.Client(factory.OwnerId);
        using var validation = await owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, int.MaxValue));
        factory.SynchronizeAssignmentInserts();
        var first = owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, 1));
        var second = owner.PostAsJsonAsync("/api/eventsessionlanguage", Input(factory.PublicSessionId, 1));
        var responses = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await Assert.That(responses.Select(response => response.StatusCode))
                .IsEquivalentTo(new[] { HttpStatusCode.Created, HttpStatusCode.BadRequest });
            await DuplicateProblemAsync(responses.Single(response => response.StatusCode == HttpStatusCode.BadRequest), validation);
            var winner = await JsonAsync(responses.Single(response => response.StatusCode == HttpStatusCode.Created));
            var id = winner.GetProperty("id").GetInt32();
            var stored = (await factory.DetailAsync(id))!;
            await Assert.That(stored.LanguageId).IsEqualTo(1);
            await Assert.That(stored.EventSessionId).IsEqualTo(factory.PublicSessionId);
            var collection = await CollectionAsync(owner, Managed(factory.PublicEventId, factory.PublicSessionId));
            await Assert.That(Items(collection).Single().GetProperty("id").GetInt32()).IsEqualTo(id);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Test]
    public async Task AssignmentInsert_DoesNotMisclassifyForeignKeysOrPrimaryKeysAsDuplicateLanguages()
    {
        await using var factory = await LanguageFactory.CreateAsync(seedAssignments: true);
        foreach (var entity in new[]
        {
            Insert(101, factory.PublicSessionId, 3),
            Insert(108, factory.PublicSessionId, int.MaxValue),
            Insert(109, factory.ForeignSessionId, 1)
        })
        {
            using var scope = factory.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var repository = scope.ServiceProvider.GetRequiredService<IEventSessionLanguageRepository>();
            await Assert.That(() => repository.Create(entity)).Throws<DbUpdateException>();
        }
        await Assert.That(await factory.DetailAsync(108)).IsNull();
        await Assert.That(await factory.DetailAsync(109)).IsNull();
        await Assert.That((await factory.DetailAsync(101))!.LanguageId).IsEqualTo(1);
    }

    [Test]
    public async Task DuplicateInsert_DetachesOnlyTheRejectedAssignmentSoTheScopeCanContinue()
    {
        await using var factory = await LanguageFactory.CreateAsync(seedAssignments: true);
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
        var repository = scope.ServiceProvider.GetRequiredService<IEventSessionLanguageRepository>();
        await Assert.That(() => repository.Create(Insert(0, factory.PublicSessionId, 1)))
            .Throws<EventSessionLanguageAlreadyAssignedException>();
        var created = await repository.Create(Insert(0, factory.PublicSessionId, 3));
        await Assert.That((await factory.DetailAsync(created.Id))!.LanguageId).IsEqualTo(3);
        await Assert.That((await factory.DetailAsync(101))!.LanguageId).IsEqualTo(1);
    }

    private static EventSessionLanguage Insert(int id, Guid sessionId, int languageId) => new()
    {
        Id = id,
        TenantId = PlatformDefaults.DefaultTenantId,
        EventSessionId = sessionId,
        LanguageId = languageId,
        EventSession = null!,
        Language = null!,
        Tenant = null!
    };

    private static async Task DuplicateProblemAsync(HttpResponseMessage response, HttpResponseMessage validation)
    {
        await Assert.That(validation.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await Assert.That(response.Content.Headers.ContentType?.MediaType)
            .IsEqualTo(validation.Content.Headers.ContentType?.MediaType);
        var established = await JsonAsync(validation);
        var problem = await JsonAsync(response);
        await Assert.That(problem.GetProperty("status").GetInt32()).IsEqualTo(400);
        await Assert.That(problem.GetProperty("type").GetString()).IsEqualTo(established.GetProperty("type").GetString());
        await Assert.That(problem.GetProperty("title").GetString()).IsEqualTo(established.GetProperty("title").GetString());
        await Assert.That(problem.GetProperty("code").GetString()).IsEqualTo("validation_failed");
        await Assert.That(problem.GetProperty("errors").GetProperty("program").GetArrayLength()).IsEqualTo(1);
        await Assert.That(problem.TryGetProperty("traceId", out _)).IsTrue();
        await Assert.That(problem.TryGetProperty("timestamp", out _)).IsTrue();
    }

    // Synchronize after both real handlers have validated and staged their inserts,
    // before either database write. The database unique index chooses the winner.
    private sealed class AssignmentInsertBarrier : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<EventSessionLanguage>()
                .Any(entry => entry.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref _arrivals) == 2)
                    _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            }
            return result;
        }
    }
}
