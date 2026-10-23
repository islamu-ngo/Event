using Explore.Application.Contracts.Notifications;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeReadTests
{
    [Test]
    public async Task Content_status_cannot_cross_original_custody_expiry_during_final_identity_reads()
    {
        var until = Now.AddMinutes(1);
        await using var fixture = await NoticeDatabase.CreateAsync(content: true, representativeUntil: until);
        var expiry = new ExpireStatusAfterProof(fixture.Clock, until);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, expiry);
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync((dto, _) =>
        {
            expiry.Armed = true;
            return Task.FromResult(dto);
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(expiry.Expired).IsTrue();
        await Assert.That(delivered).IsFalse();
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Content_status_holds_only_its_original_representative_sources_before_tenant(int change)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var (eventId, originalActor, replacement) = await StatusActorsAsync(fixture);
        var checkpoint = new NoticeSourcesBeforeTenant(eventId, replacement, change);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, checkpoint);
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync((dto, _) => Task.FromResult(dto),
            (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(change == 0
            ? ModerationNoticeReadOutcome.Allowed : ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsEqualTo(change == 0);
        await Assert.That(checkpoint.ObservedTenant).IsTrue();
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(originalActor)).IsTrue();
        await Assert.That(checkpoint.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(replacement)).IsFalse();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await AssertOriginalStatusSourcesAsync(fixture, eventId, originalActor);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Prepared_content_status_cannot_expand_its_sources_before_delivery(bool changeRole)
    {
        await using var fixture = await NoticeDatabase.CreateAsync(content: true);
        var (eventId, originalActor, replacement) = await StatusActorsAsync(fixture);
        var checkpoint = new NoticeSourcesBeforeTenant(eventId, replacement, 0);
        await using var reader = fixture.Reader(fixture.Database.SubjectId, null, checkpoint);
        var pending = await reader.Status(fixture.DecisionId, fixture.EffectId);
        var delivered = false;
        var outcome = await pending.Projection!.DiscloseAsync(async (_, token) =>
        {
            if (changeRole)
                await reader.Context.EventRoleAssignments.Where(row => row.EventId == eventId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.RoleId, (int)RoleEnum.RegistrationManager), token);
            else
                await reader.Context.Events.Where(row => row.Id == eventId).ExecuteUpdateAsync(
                    setters => setters.SetProperty(row => row.ActorId, replacement), token);
            return ReadOnlyMemory<byte>.Empty;
        }, (_, _) => { delivered = true; return Task.CompletedTask; });
        await Assert.That(outcome).IsEqualTo(ModerationNoticeReadOutcome.NotFound);
        await Assert.That(delivered).IsFalse();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await AssertOriginalStatusSourcesAsync(fixture, eventId, originalActor);
    }

    private static async Task<(Guid EventId, Guid ActorId, Guid Replacement)> StatusActorsAsync(NoticeDatabase fixture)
    {
        await using var setup = fixture.Database.Context();
        var eventId = await setup.EventRoleAssignments.Select(row => row.EventId).SingleAsync();
        var actor = await setup.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
        var replacement = Guid.CreateVersion7();
        setup.Actors.Add(new Actor
        {
            Id = replacement, ActorTypeId = (int)ActorTypeEnum.User,
            UserId = fixture.Database.ReviewerId, Pii = new ActorPii()
        });
        await setup.SaveChangesAsync();
        return (eventId, actor, replacement);
    }

    private static async Task AssertOriginalStatusSourcesAsync(NoticeDatabase fixture, Guid eventId, Guid actor)
    {
        await using var inspect = fixture.Database.Context();
        await Assert.That(await inspect.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync())
            .IsEqualTo(actor);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        inspect.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }

    private sealed class ExpireStatusAfterProof(TestClock clock, DateTimeOffset until) : DbCommandInterceptor
    {
        private bool _proofRead;
        public bool Armed { get; set; }
        public bool Expired { get; private set; }
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (!Armed || Expired || eventData.Context is not ExploreDbContext context) return ValueTask.FromResult(result);
            var sql = context.GetService<ISqlGenerationHelper>();
            bool Reads(Type type)
            {
                var entity = context.Model.FindEntityType(type)!;
                return command.CommandText.Contains($"FROM {sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema())}",
                    StringComparison.Ordinal);
            }
            if (_proofRead && Reads(typeof(User)))
            {
                Expired = true;
                clock.UtcNow = until;
            }
            if (Reads(typeof(ModerationNoticeNotificationReceipt))) _proofRead = true;
            return ValueTask.FromResult(result);
        }
    }
}
