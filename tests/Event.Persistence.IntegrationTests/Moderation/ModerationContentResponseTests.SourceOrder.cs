using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NoticeDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationNoticeReadTests.NoticeDatabase;
using SourceOrder = Event.Persistence.IntegrationTests.Moderation.ModerationContentNoticeCommitTests.DisclosureSourceOrder;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationContentResponseTests
{
    [Test]
    [Arguments(0, false, false)]
    [Arguments(1, false, false)]
    [Arguments(2, false, false)]
    [Arguments(3, false, false)]
    [Arguments(4, false, false)]
    [Arguments(1, true, false)]
    [Arguments(2, true, false)]
    [Arguments(0, false, true)]
    [Arguments(3, false, true)]
    [Arguments(4, false, true)]
    public async Task Content_response_rechecks_borrow_the_original_representative_sources(
        int change, bool replay, bool colocated)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var (actor, replacement) = await ResponseParentsAsync(fixture);
        var order = new SourceOrder(replacement, change);
        using var baseline = fixture.Database.Context();
        var options = (DbContextOptions<ExploreDbContext>)baseline.GetService<IDbContextOptions>();
        var primary = new ExploreDbContext(new DbContextOptionsBuilder<ExploreDbContext>(options)
            .AddInterceptors(order, new ChangeResponseSourcesAfterSave(change, replacement)).Options)
        { TenantContext = fixture.Database };
        await using var privacy = colocated ? await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary) : null;
        await using (var session = new ResponseSession(fixture, fixture.Database.SubjectId, primary,
            privacy?.Authority(primary, fixture.Clock)))
        {
            var command = Command(fixture);
            if (replay) await Assert.That((await session.Handler.ExecuteAsync(command)).IsSuccess).IsTrue();
            order.Armed = true;
            var result = await session.Handler.ExecuteAsync(command);
            await Assert.That(result.IsSuccess).IsEqualTo(change == 0);
            await Assert.That(order.ActorsBeforeTenant.Contains(actor)).IsTrue();
            await Assert.That(order.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
            await Assert.That(order.UnplannedSources).IsEqualTo(0);
            await Assert.That(primary.Database.CurrentTransaction).IsNull();
        }
        await using var inspect = Read(fixture);
        await Assert.That((await inspect.Events.SingleAsync()).ActorId).IsEqualTo(actor);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        await Assert.That(await inspect.Set<ModerationContentResponse>().CountAsync())
            .IsEqualTo(replay || change == 0 ? 1 : 0);
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().AnyAsync()).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Availability_preparation_cannot_replan_representative_authority(int change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var (actor, replacement) = await ResponseParentsAsync(fixture);
        var order = new SourceOrder(replacement, 0) { Armed = true };
        await using (var session = new ResponseSession(fixture, fixture.Database.SubjectId,
            fixture.Database.Context(order)))
        {
            var delivered = false;
            var outcome = await session.Repository.DiscloseContentResponseAvailabilityAsync(
                fixture.Database.TenantId, fixture.SubjectNoticeId, async (_, token) =>
                {
                    if (change == 1)
                        await session.Context.Events.ExecuteUpdateAsync(setters => setters.SetProperty(
                            row => row.ActorId, replacement), token);
                    if (change == 2)
                        await session.Context.EventRoleAssignments.ExecuteUpdateAsync(setters => setters.SetProperty(
                            row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
                    return ReadOnlyMemory<byte>.Empty;
                }, (_, _) => { delivered = true; return Task.CompletedTask; }, CancellationToken.None);
            await Assert.That(outcome == ModerationQualificationWriteOutcome.Known).IsEqualTo(change == 0);
            await Assert.That(delivered).IsEqualTo(change == 0);
            await Assert.That(order.ActorsBeforeTenant.Contains(actor)).IsTrue();
            await Assert.That(order.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
            await Assert.That(order.UnplannedSources).IsEqualTo(0);
        }
        await using var inspect = Read(fixture);
        await Assert.That((await inspect.Events.SingleAsync()).ActorId).IsEqualTo(actor);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsFalse();
    }

    [Test]
    public async Task Co_located_availability_expiry_rolls_back_prepared_native_changes()
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var primary = fixture.Database.Context();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateColocatedAsync(primary);
        await using var session = new ResponseSession(fixture, fixture.Database.SubjectId, primary,
            privacy.Authority(primary, fixture.Clock));
        var originalTitle = (await primary.Events.AsNoTracking().SingleAsync()).Title;
        var delivered = false;
        var prepared = false;
        var outcome = await session.Repository.DiscloseContentResponseAvailabilityAsync(
            fixture.Database.TenantId, fixture.SubjectNoticeId, async (availability, token) =>
            {
                prepared = true;
                await Assert.That(primary.IdentityFenceOwnsTransaction).IsTrue();
                (await primary.Events.SingleAsync(token)).Title = "Private preparation must roll back";
                await primary.SaveChangesAsync(token);
                fixture.Clock.UtcNow = availability.QualifiedUntil;
                return ReadOnlyMemory<byte>.Empty;
            }, (_, _) => { delivered = true; return Task.CompletedTask; }, CancellationToken.None);
        await Assert.That(prepared).IsTrue();
        await Assert.That(outcome == ModerationQualificationWriteOutcome.Known).IsFalse();
        await Assert.That(delivered).IsFalse();
        await Assert.That(primary.Database.CurrentTransaction).IsNull();
        await using var inspect = Read(fixture);
        await Assert.That((await inspect.Events.SingleAsync()).Title).IsEqualTo(originalTitle);
        await Assert.That(await inspect.Set<ModerationContentResponse>().AnyAsync()).IsFalse();
    }

    private static async Task<(Guid Actor, Guid Replacement)> ResponseParentsAsync(NoticeDatabase fixture)
    {
        await using var context = fixture.Database.Context();
        var actor = (await context.Events.SingleAsync()).ActorId;
        var replacement = Guid.CreateVersion7();
        context.Actors.Add(new Actor
        {
            Id = replacement,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = fixture.Database.ReviewerId,
            Pii = new ActorPii { ActorId = replacement, DisplayName = "Unplanned response parent" }
        });
        await context.SaveChangesAsync();
        return (actor, replacement);
    }

    private sealed class ChangeResponseSourcesAfterSave(int change, Guid replacement) : SaveChangesInterceptor
    {
        private bool _changed;
        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var context = (ExploreDbContext)eventData.Context!;
            if (_changed || change is not (3 or 4)
                || !context.ChangeTracker.Entries<ModerationContentResponse>().Any()) return result;
            _changed = true;
            if (change == 3)
                await context.Events.ExecuteUpdateAsync(setters => setters.SetProperty(
                    row => row.ActorId, replacement), cancellationToken);
            else
                await context.EventRoleAssignments.ExecuteUpdateAsync(setters => setters.SetProperty(
                    row => row.RoleId, (int)RoleEnum.RegistrationManager), cancellationToken);
            return result;
        }
    }
}
