using Explore.Application.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationNoticeEmailTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Content_smtp_holds_original_representative_sources_before_tenant(int change)
    {
        await using var fixture = await EmailFixture.CreateAsync(content: true);
        var request = await fixture.AdmitAsync();
        Guid eventId;
        Guid originalActor;
        var replacement = Guid.CreateVersion7();
        await using (var setup = fixture.Notice.Database.Context())
        {
            eventId = await setup.EventRoleAssignments.Select(row => row.EventId).SingleAsync();
            originalActor = await setup.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync();
            setup.Actors.Add(new Actor
            {
                Id = replacement,
                ActorTypeId = (int)ActorTypeEnum.User,
                UserId = fixture.Notice.Database.ReviewerId,
                Pii = new ActorPii()
            });
            await setup.SaveChangesAsync();
        }
        var checkpoint = new ModerationNoticeReadTests.NoticeSourcesBeforeTenant(eventId, replacement, change);
        await using var context = fixture.Notice.Database.Context(checkpoint);
        var delivered = false;
        var result = await fixture.CreateAuthority(context).SendAsync(request, (_, _) =>
        {
            delivered = true;
            return Task.FromResult(EmailResult.Ok());
        }, CancellationToken.None);
        await Assert.That(result.Success).IsEqualTo(change == 0);
        await Assert.That(delivered).IsEqualTo(change == 0);
        await Assert.That(checkpoint.ObservedTenant).IsTrue();
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(originalActor)).IsTrue();
        await Assert.That(checkpoint.RolesBeforeTenant.Contains((int)RoleEnum.EventOwner)).IsTrue();
        await Assert.That(checkpoint.ActorsBeforeTenant.Contains(replacement)).IsFalse();
        await Assert.That(checkpoint.LateEarlyAnchors).IsEqualTo(0);
        await using var inspect = fixture.Notice.Database.Context();
        await Assert.That(await inspect.Events.Where(row => row.Id == eventId).Select(row => row.ActorId).SingleAsync())
            .IsEqualTo(originalActor);
        await Assert.That((await inspect.EventRoleAssignments.SingleAsync()).RoleId).IsEqualTo((int)RoleEnum.EventOwner);
        inspect.ModerationTrustDomainId = fixture.Notice.Database.Scope.TrustDomainId;
        await Assert.That(await inspect.Set<ModerationNoticeNotificationReceipt>().CountAsync()).IsEqualTo(0);
    }
}
