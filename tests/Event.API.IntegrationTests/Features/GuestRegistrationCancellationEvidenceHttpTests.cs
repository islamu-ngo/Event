
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.API.Hateoas;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Hateoas;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Services.Registration;
using Explore.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using static Event.Api.IntegrationTests.Features.AnonymousRegistrationChallengeHttpTests;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class GuestRegistrationStatusHttpTests
{
    [Test]
    public async Task Cancellation_ExactPaidEvidenceOverridesZeroTotalAndReturnsSafeConflict()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        using HttpResponseMessage initial = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        using JsonDocument before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        string staleHref = before.RootElement.GetProperty("_links").GetProperty("cancel-registration").GetProperty("href").GetString()!;
        string providerCanary = "PAYMENT-EVIDENCE-NOT-FOR-DISCLOSURE";
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            Guid actorId = (await database.Events.SingleAsync(row => row.Id == host.EventId)).ActorId;
            var recipient = OrganizerPaymentRecipientSnapshot.Create(PlatformDefaults.DefaultTenantId,
                actorId, Guid.CreateVersion7(), "stripe", "platform", providerCanary,
                "US", "USD", Guid.CreateVersion7(), null, host.Clock.GetUtcNow().UtcDateTime);
            database.PaymentAttempts.Add(PaymentAttempt.Create(Guid.CreateVersion7(), PlatformDefaults.DefaultTenantId,
                orderId, recipient, "OrganizerDirect", "test-v1", "test-v1", Money.Create(100, "USD"),
                Money.Create(0, "USD"), Money.Create(0, "USD"), Guid.CreateVersion7().ToString("N"),
                host.Clock.GetUtcNow().UtcDateTime, null));
            await database.SaveChangesAsync();
        }
        await AssertCancellationAbsentAsync(host, orderId, capability);
        using HttpResponseMessage denied = await CancelAsync(host.Client, staleHref, capability);
        await AssertCancellationConflictAsync(denied, capability, providerCanary);
        using HttpResponseMessage privateDenied = await CancelAsync(host.Client, staleHref, null);
        await AssertPrivateNotFound(privateDenied);
    }

    [Test]
    public async Task Cancellation_StaleLinkRejectsNativeCheckedInAdmissionWithoutLeakingEvidence()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        AdmissionScenario admission = await IssueAdmissionAsync(host, orderId);
        using HttpResponseMessage initial = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        using JsonDocument before = JsonDocument.Parse(await initial.Content.ReadAsStringAsync());
        string staleHref = before.RootElement.GetProperty("_links").GetProperty("cancel-registration").GetProperty("href").GetString()!;
        await CheckInAsync(host, admission);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(PlatformDefaults.DefaultTenantId);
            var cached = JsonSerializer.Deserialize<GuestRegistrationStatusDto>(before.RootElement,
                JsonSerializerOptions.Web)! with
            { CanCancelRegistration = true };
            var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
            context.Request.Scheme = host.Client.BaseAddress!.Scheme;
            context.Request.Host = new HostString(host.Client.BaseAddress.Authority);
            context.Request.Headers[CapabilityHeader] = capability;
            var assembler = scope.ServiceProvider.GetRequiredService<IResourceAssembler<GuestRegistrationStatusDto, GuestRegistrationStatusDto>>();
            HalResource<GuestRegistrationStatusDto> refreshed = await assembler.ToResource(cached, context);
            await Assert.That(refreshed.Links.ContainsKey(LinkRelations.CancelRegistration)).IsFalse();
        }
        await AssertCancellationAbsentAsync(host, orderId, capability);
        using HttpResponseMessage denied = await CancelAsync(host.Client, staleHref, capability);
        await AssertCancellationConflictAsync(denied, capability, admission.Credential.LookupDigest,
            admission.TicketId.ToString("D"), "CANCELLATION-PII-CANARY", "admission-delivery@example.test");
        using HttpResponseMessage current = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        using JsonDocument status = JsonDocument.Parse(await current.Content.ReadAsStringAsync());
        await Assert.That(status.RootElement.GetProperty("registrationOrderStatusId").GetInt32())
            .IsEqualTo((int)RegistrationOrderStatusEnum.Confirmed);
        await Assert.That((await host.HoldsAsync(orderId)).Single().RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Consumed);
    }

    [Test]
    public async Task Cancellation_NoGetSideEffectsAndSuccessfulPostRevokesNativeAdmission()
    {
        await using NativeHost host = await NativeHost.CreateAsync();
        (Guid orderId, string capability) = await ConfirmAsync(host);
        AdmissionScenario admission = await IssueAdmissionAsync(host, orderId);
        for (int read = 0; read < 2; read++)
        {
            using HttpResponseMessage status = await SendAsync(host.Client, HttpMethod.Get,
                StatusPath(host.EventId, orderId), capability);
            await Assert.That(status.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IParticipantAdmissionEligibilityRepository>();
            var active = await repository.GetIssuedTicketAsync(PlatformDefaults.DefaultTenantId, admission.AssignmentId, CancellationToken.None);
            await Assert.That(active!.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Active);
        }
        using HttpResponseMessage cancelled = await CancelAsync(host.Client,
            CancellationPath(host.EventId, orderId), capability);
        await AssertNoContentAsync(cancelled);
        await using (AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IParticipantAdmissionEligibilityRepository>();
            var revoked = await repository.GetIssuedTicketAsync(PlatformDefaults.DefaultTenantId, admission.AssignmentId, CancellationToken.None);
            await Assert.That(revoked!.AdmissionTicketStatusId).IsEqualTo((int)AdmissionTicketStatusEnum.Cancelled);
        }
        await Assert.That((await host.HoldsAsync(orderId)).Single().RegistrationInventoryHoldStatusId)
            .IsEqualTo((int)RegistrationInventoryHoldStatusEnum.Released);
    }

    private static async Task AssertCancellationAbsentAsync(NativeHost host, Guid orderId, string capability)
    {
        using HttpResponseMessage response = await SendAsync(host.Client, HttpMethod.Get,
            StatusPath(host.EventId, orderId), capability);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(document.RootElement.GetProperty("_links").TryGetProperty("cancel-registration", out _)).IsFalse();
    }

    private static async Task AssertCancellationConflictAsync(HttpResponseMessage response, params string[] excluded)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Conflict)
            .Because(await response.Content.ReadAsStringAsync());
        await AssertPrivate(response);
        string body = await response.Content.ReadAsStringAsync();
        using JsonDocument problem = JsonDocument.Parse(body);
        await Assert.That(problem.RootElement.GetProperty("status").GetInt32()).IsEqualTo(409);
        await Assert.That(problem.RootElement.GetProperty("code").GetString())
            .IsEqualTo("guest_registration_cancellation_ineligible");
        await Assert.That(problem.RootElement.TryGetProperty("_links", out _)).IsFalse();
        string[] allowed = ["type", "title", "status", "detail", "instance", "code", "traceId", "timestamp", "correlationId", "id", "success", "message"];
        await Assert.That(problem.RootElement.EnumerateObject().All(property => allowed.Contains(property.Name))).IsTrue().Because(body);
        await Assert.That(problem.RootElement.GetProperty("success").GetBoolean()).IsFalse();
        foreach (string secret in excluded) await Assert.That(body).DoesNotContain(secret);
    }

    private sealed record AdmissionScenario(Guid TicketId, Guid AssignmentId, Guid TargetId,
        Guid StaffActorId, AdmissionCheckInCredentialDigestCandidate Credential);

    private static async Task<AdmissionScenario> IssueAdmissionAsync(NativeHost host, Guid orderId)
    {
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        Guid tenantId = PlatformDefaults.DefaultTenantId;
        DateTime now = host.Clock.GetUtcNow().UtcDateTime;
        var order = await database.RegistrationOrders.Include(row => row.Lines).Include(row => row.Participants)
            .SingleAsync(row => row.Id == orderId);
        // This native issuance path requires optional delivery contact, not checkout or cancellation authority.
        order.SetPii(RegistrationOrderPii.Create(orderId, tenantId, "CANCELLATION-PII-CANARY",
            "admission-delivery@example.test", null, null, (int)RegistrationRetentionPolicyEnum.StandardOperational, now));
        Guid subjectId = (await database.InstanceBootstrapStates.SingleAsync()).CompletedByUserId!.Value;
        Guid actorId = (await database.Events.SingleAsync(row => row.Id == host.EventId)).ActorId;
        RegistrationParticipant participant = order.Participants.Single();
        participant.ClaimBy(subjectId, Guid.CreateVersion7());
        var assignment = RegistrationTicketAssignment.CreateAssigned(Guid.CreateVersion7(), order.Lines.Single().Id, 1, participant, now);
        var eligibility = ParticipantAdmissionEligibility.Create(tenantId, host.EventId, assignment, participant, false, false, now);
        eligibility.RecordSubjectCompletion(participant, subjectId, null, now, Guid.CreateVersion7());
        var effect = RegistrationFinalizationEffect.Create(order, now);
        var target = AdmissionTarget.Create(Guid.CreateVersion7(), tenantId, host.EventId, AdmissionTargetTypeEnum.Event, null, null);
        var policy = AdmissionCheckInPolicy.Create(Guid.CreateVersion7(), target, now.AddHours(-1), now.AddHours(1), 1);
        database.AddRange(assignment, eligibility, effect, target, policy);
        await database.SaveChangesAsync();

        // Only the external secret authority is substituted. Issuance, envelopes, repository and dispatch are native.
        string key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secrets = Substitute.For<ISecretResolver>();
        secrets.ResolveQualifiedAsync(SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey,
            SecretScope.Instance, null, "v1", Arg.Any<CancellationToken>()).Returns(
            SecretResolutionResult.Resolved(new ResolvedSecret(SecretDefinitionRegistry.Keys.Admissions.CredentialLookupHmacKey,
                key, SecretSourceType.EnvironmentVariable, SecretScope.Instance, null, host.Clock.GetUtcNow())));
        var service = new AdmissionIssuanceService(scope.ServiceProvider.GetRequiredService<IAdmissionIssuanceRepository>(),
            new AdmissionCredentialDigestService(secrets, Options.Create(new AdmissionCredentialOptions())),
            scope.ServiceProvider.GetRequiredService<IAdmissionDeliveryEnvelopeProtector>(),
            scope.ServiceProvider.GetRequiredService<IAdmissionDeliveryDispatcher>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), host.Clock);
        AdmissionIssuanceResult issued = await service.IssueConfirmedAsync(new AdmissionIssuanceRequest(tenantId,
            orderId, effect.Id, AdmissionIssuanceAuthority.ConfirmedFreeOrder), CancellationToken.None);
        await Assert.That(issued.Outcome).IsEqualTo(AdmissionIssuanceOutcome.Issued);
        AdmissionTicket ticket = issued.Tickets.Single();
        AdmissionTicketCredential credential = ticket.Credentials.Single();
        return new(ticket.Id, assignment.Id, target.Id, actorId,
            new AdmissionCheckInCredentialDigestCandidate(credential.LookupDigest, credential.LookupKeyVersion));
    }

    private static async Task CheckInAsync(NativeHost host, AdmissionScenario admission)
    {
        await using AsyncServiceScope scope = host.Factory.Services.CreateAsyncScope();
        var transaction = scope.ServiceProvider.GetRequiredService<IAdmissionCheckInTransaction>();
        AdmissionCheckInDecision? decision = await scope.ServiceProvider.GetRequiredService<IUnitOfWork>()
            .ExecuteInTransactionAsync(token => transaction.ExecuteAsync(new AdmissionCheckInTransactionRequest(
                PlatformDefaults.DefaultTenantId, host.EventId, admission.TargetId, [admission.Credential],
                AdmissionCheckInAction.CheckIn, null, admission.StaffActorId, null, host.Clock.GetUtcNow()), token),
                CancellationToken.None);
        await Assert.That(decision?.ResultCode).IsEqualTo(AdmissionCheckInResultCodeEnum.CheckedIn);
    }
}
