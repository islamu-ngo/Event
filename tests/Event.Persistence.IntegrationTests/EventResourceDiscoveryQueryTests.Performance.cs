using System.Diagnostics;
using System.Runtime.InteropServices;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class EventResourceDiscoveryQueryTests
{
    [Test]
    public async Task MixedAudienceFirstPageStaysBoundedWithTenThousandOtherParticipants()
    {
        var (scope, subject) = await SeedAsync();
        var other = await MemberAsync(scope.TenantAId);
        EventResourceAudienceKindEnum[] audiences =
        [
            EventResourceAudienceKindEnum.Public,
            EventResourceAudienceKindEnum.AuthenticatedTenantMember,
            EventResourceAudienceKindEnum.TicketHolder
        ];
        var resources = Enumerable.Range(0, 500).Select(index =>
        {
            var audience = audiences[index % audiences.Length];
            var resource = Resource(scope, subject, index, audience == EventResourceAudienceKindEnum.TicketHolder
                ? EventResourceAudienceKindEnum.Public : audience);
            if (audience == EventResourceAudienceKindEnum.TicketHolder)
                resource.ReplacePolicy(resource.Availability,
                    [EventResourceAudienceRule.Create(scope.TenantAId, scope.EventAId, resource.Id,
                        audience, sessionId: scope.SessionAId)], resource.ConcurrencyStamp, subject, Now);
            return resource;
        }).ToArray();
        await SaveAsync(resources);
        await using (var seed = database.CreateContext())
        {
            for (int orderIndex = 0; orderIndex < 100; orderIndex++)
            {
                var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, other, scope.ActorId,
                    BookingPartyTypeEnum.Individual, scope.CatalogAId,
                    RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 1, 1, 1, null),
                    null, null, "USD", Now, null);
                for (int participantIndex = 0; participantIndex < 100; participantIndex++)
                    order.AddParticipant(RegistrationParticipant.Create(scope.TenantAId, order.Id, other, ParticipantTypeEnum.Adult, null));
                seed.RegistrationOrders.Add(order);
            }
            await seed.SaveChangesAsync();
            await Assert.That(await seed.RegistrationParticipants
                .CountAsync(row => row.RegistrationOrder!.EventId == scope.EventAId)).IsEqualTo(10_000);
        }
        var milliseconds = new List<double>();
        var selects = new List<int>();
        for (int iteration = 0; iteration < 20; iteration++)
        {
            var recorder = new SqlRecorder();
            var provider = new Provider
            {
                Decide = input => input.Resource.Id == scope.EventAId || input.Resource.Id == resources[^1].Id
                    ? EventResourceProviderDecision.Allow : EventResourceProviderDecision.Deny
            };
            await using var context = database.CreateContext(recorder);
            var workflow = Workflow(context, scope.TenantAId, subject, provider);
            long started = Stopwatch.GetTimestamp();
            var result = await workflow.ListAsync(scope.EventAId, 20, null, default);
            milliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            selects.Add(recorder.Commands.Count);
            await Assert.That(result.Failure).IsEqualTo(EventResourceAudienceFailure.None);
            await Assert.That(result.Value!.Items.Select(item => item.Id).SequenceEqual([resources[^1].Id])).IsTrue();
            await Assert.That(recorder.Commands.Count).IsLessThanOrEqualTo(150);
            await Assert.That(provider.Sizes.Count).IsLessThanOrEqualTo(4);
            await Assert.That(context.ChangeTracker.Entries<RegistrationParticipant>().Any()).IsFalse();
        }
        double p95 = milliseconds.Order().ElementAt(18);
        Console.WriteLine($"AUDIENCE_PROFILE provider=SQLite resources=500 participants=10000 samples=20 " +
            $"native_first_page_p95_ms={p95:F2} select_min={selects.Min()} select_max={selects.Max()} " +
            $"logical_processors={Environment.ProcessorCount} runtime={RuntimeInformation.FrameworkDescription}; " +
            "diagnostic only: excludes HTTP/HAL and remote PDP latency; no timing assertion.");
    }
}
