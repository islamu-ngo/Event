using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Persistence;
using Explore.Tests.Shared.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Tests.Infrastructure;

[NotInParallel]
public sealed class EventResourceProviderOriginalCustodyTests
{
    [Test]
    public async Task OriginalCommandAndCanonicalStampSurviveExactPersistenceWithoutTrackedFlush()
    {
        await using var fixture = await Fixture.CreateAsync();
        var command = fixture.Bind();
        Guid stamp = await fixture.Authentication.RunAsync(null, async session =>
        {
            var preparation = session.Preparation;
            var store = session.Services.GetRequiredService<IEventResourceProviderOriginalStore>();
            var prepared = await preparation.PrepareAsync(command);
            // This unrelated tracked change must neither escape with the pair nor change
            // its canonical concurrency stamp through SaveChanges interception.
            session.Context.SystemSettings.Add(new SystemSetting { Id = Guid.CreateVersion7(), SettingKey = "original-custody-unrelated", Value = "not-written" });
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => preparation.ExecutePreparedAsync(prepared, async (_, selectedToken) =>
                {
                    var original = preparation.CurrentEventResourceProviderOriginal!;
                    await Assert.That(original.Command).IsSameReferenceAs(command);
                    var result = await store.ApplyBindAsync(original, selectedToken);
                    await Assert.That(result.Epoch).IsEqualTo(1L);
                    return original.ConcurrencyStamp;
                }, token));
        });
        await using var observer = fixture.Context();
        var persisted = await observer.Set<EventResourceProviderActivation>().AsNoTracking().SingleAsync(row => row.Id == fixture.Deployment);
        await Assert.That(persisted.ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(persisted.CreatedBy).IsEqualTo(fixture.Authentication.ActorId);
        await Assert.That(persisted.UpdatedBy).IsEqualTo(fixture.Authentication.ActorId);
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == "original-custody-unrelated")).IsFalse();
    }

    [Test]
    public async Task SkippedActionCannotClaimEvenAnUnchangedResult()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(fixture.Bind());
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => session.Preparation.ExecutePreparedAsync(prepared, (_, _) => Task.FromResult(false), token));
        }));
        await fixture.RequireAbsentAsync();
    }

    [Test]
    public async Task ClosedOriginalCannotReplayOrMoveToAnotherStore()
    {
        await using var fixture = await Fixture.CreateAsync();
        EventResourceProviderOriginal? captured = null;
        IPreparedHumanSettingsMutation? expired = null;
        await fixture.Authentication.RunAsync(null, async session =>
        {
            expired = await session.Preparation.PrepareAsync(fixture.Bind());
            var result = await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => session.Preparation.ExecutePreparedAsync(expired, async (_, selectedToken) =>
                {
                    captured = session.Preparation.CurrentEventResourceProviderOriginal!;
                    return await session.Services.GetRequiredService<IEventResourceProviderOriginalStore>().ApplyBindAsync(captured, selectedToken);
                }, token));
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.Preparation.ExecutePreparedAsync(expired,
                (_, _) => Task.FromResult(false)));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Services.GetRequiredService<IEventResourceProviderOriginalStore>()
                .ApplyBindAsync(captured!, CancellationToken.None));
            return result;
        });
        await fixture.Authentication.RunAsync(null, async session =>
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.Preparation.ExecutePreparedAsync(expired!,
                (_, _) => Task.FromResult(false)));
            var prepared = await session.Preparation.PrepareAsync(new BeginEventResourceProviderOperationCommand(fixture.Deployment));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => session.MutationLock.ExecuteOrderedGroupsAsync(
                [[EventResourceProviderBindingDocument.SettingKey]], token => session.Preparation.ExecutePreparedAsync(prepared,
                    (_, selectedToken) => session.Services.GetRequiredService<IEventResourceProviderOriginalStore>()
                        .ApplyBindAsync(captured!, selectedToken), token)));
            return true;
        });
    }

    [Test]
    public async Task ActualSignedCredentialExpiryRollsBackThePair()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Authentication.RunAsync(null, async session =>
        {
            var clock = new MutableClock(DateTimeOffset.UtcNow);
            var selected = new ResourceProviderTestServices(session.Services, clock);
            var request = session.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            var lifetime = (await session.Services.GetRequiredService<IHumanAuthenticationLifetimeObserver>()
                .ObserveAsync(request.User, CancellationToken.None))!;
            var prepared = await selected.Preparation.PrepareAsync(fixture.Bind());
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => selected.Preparation.ExecutePreparedAsync(prepared, async (_, selectedToken) =>
                {
                    var result = await selected.Store.ApplyBindAsync(
                        selected.Preparation.CurrentEventResourceProviderOriginal!, selectedToken);
                    clock.Now = lifetime.ExpiresAt.Add(lifetime.ClockSkew).AddTicks(1);
                    return result;
                }, token));
        }));
        await fixture.RequireAbsentAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OriginalBindingAbsenceOrActivationStampCannotBeReobserved(bool existing)
    {
        await using var fixture = await Fixture.CreateAsync();
        Guid revision = Guid.Empty;
        if (existing)
            revision = (await fixture.Authentication.RunAsync(null, session =>
                session.Services.GetRequiredService<EventResourceProviderControlPlane>().BindAsync(fixture.Bind(), default))).BindingRevision;
        await Assert.ThrowsAsync<Explore.Application.Exceptions.ConcurrencyConflictException>(() =>
            fixture.Authentication.RunAsync(null, async session =>
            {
                var prepared = await session.Preparation.PrepareAsync(fixture.Bind(revision));
                await using (var writer = fixture.Context())
                {
                    if (existing)
                    {
                        var row = await writer.Set<EventResourceProviderActivation>().SingleAsync(row => row.Id == fixture.Deployment);
                        row.ConcurrencyStamp = Guid.CreateVersion7();
                    }
                    else writer.SystemSettings.Add(new SystemSetting
                    {
                        Id = Guid.CreateVersion7(),
                        SettingKey = EventResourceProviderBindingDocument.SettingKey,
                        Value = System.Text.Json.JsonSerializer.Serialize(new EventResourceProviderBindingDocument(Guid.CreateVersion7(), []),
                            EventResourceProviderBindingDocument.JsonOptions),
                        ValueType = SettingValueType.Json
                    });
                    await writer.SaveChangesAsync();
                }
                return await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                    token => session.Preparation.ExecutePreparedAsync(prepared, (_, selectedToken) =>
                        session.Services.GetRequiredService<IEventResourceProviderOriginalStore>().ApplyBindAsync(
                            session.Preparation.CurrentEventResourceProviderOriginal!, selectedToken), token));
            }));
    }

    [Test]
    public async Task ReplacedRequestPrincipalCannotAcquireTheOriginal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(fixture.Bind());
            var request = session.Services.GetRequiredService<IHttpContextAccessor>().HttpContext!;
            request.User = new System.Security.Claims.ClaimsPrincipal(request.User);
            return await session.Preparation.ExecutePreparedAsync(prepared, (_, _) => Task.FromResult(false));
        }));
        await fixture.RequireAbsentAsync();
    }

    [Test]
    public async Task ReplacedPhysicalConnectionCannotAcquireTheOriginal()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(fixture.Bind());
            var previous = session.Context.Database.GetDbConnection();
            await session.Context.Database.CloseConnectionAsync();
            await using var replacement = new SqliteConnection(previous.ConnectionString);
            session.Context.Database.SetDbConnection(replacement, contextOwnsConnection: false);
            try
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => session.Preparation.ExecutePreparedAsync(prepared,
                    (_, _) => Task.FromResult(false)));
            }
            finally { session.Context.Database.SetDbConnection(previous, contextOwnsConnection: false); }
            return true;
        });
        await fixture.RequireAbsentAsync();
    }

    [Test]
    public async Task RuntimeTenantCannotBorrowGlobalInstancePurpose()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Authentication.RunAsync(fixture.Database.TenantId,
            session => session.Preparation.PrepareAsync(fixture.Bind())));
        await fixture.RequireAbsentAsync();
    }

    [Test]
    public async Task FinalHorizonFailureRollsBackTheAlreadyWrittenPair()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => fixture.Authentication.RunAsync(null, async session =>
        {
            var prepared = await session.Preparation.PrepareAsync(fixture.Bind());
            return await session.MutationLock.ExecuteOrderedGroupsAsync([[EventResourceProviderBindingDocument.SettingKey]],
                token => session.Preparation.ExecutePreparedAsync(prepared, async (_, selectedToken) =>
                {
                    var result = await session.Services.GetRequiredService<IEventResourceProviderOriginalStore>()
                        .ApplyBindAsync(session.Preparation.CurrentEventResourceProviderOriginal!, selectedToken);
                    session.Services.GetRequiredService<IHumanActionCommitFinalizer>().Register(
                        _ => Task.CompletedTask, _ => throw new UnauthorizedAccessException("Final horizon ended."));
                    return result;
                }, token));
        }));
        await fixture.RequireAbsentAsync();
    }

    private sealed class Fixture(SmtpSettingsDatabase database, AuthenticatedSettingsFixture authentication) : IAsyncDisposable
    {
        internal SmtpSettingsDatabase Database => database;
        internal AuthenticatedSettingsFixture Authentication => authentication;
        internal Guid Deployment { get; } = Guid.CreateVersion7();
        internal ExploreDbContext Context() => AuthenticatedPolicyPackageTestService.CreateContextFor(database);
        internal BindEventResourceProviderCommand Bind(Guid revision = default) =>
            new(new(Deployment, ["https://original.example.test"], "resource", "default"), revision);
        internal static async Task<Fixture> CreateAsync()
        {
            var database = await SmtpSettingsDatabase.CreateAsync();
            var authentication = await AuthenticatedSettingsFixture.CreateAsync(
                () => AuthenticatedPolicyPackageTestService.CreateContextFor(database));
            return new(database, authentication);
        }
        internal async Task RequireAbsentAsync()
        {
            await using var observer = Context();
            await Assert.That(await observer.Set<EventResourceProviderActivation>().AnyAsync(row => row.Id == Deployment)).IsFalse();
            await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == EventResourceProviderBindingDocument.SettingKey)).IsFalse();
        }
        public async ValueTask DisposeAsync()
        {
            await authentication.DisposeAsync();
            await database.DisposeAsync();
        }
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
