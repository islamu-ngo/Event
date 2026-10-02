using System.Security.Cryptography;
using Event.Api.IntegrationTests.Builders;
using Event.Api.IntegrationTests.Fixtures;
using Event.Api.IntegrationTests.Seeds;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Handlers.Commands;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class UserProfileSnapshotHttpTests
{
    [Test]
    public async Task PersistedProfileNameEditPreservesOrderContactAndPinnedConsentEvidence()
    {
        await using var factory = await LocalAdmissionWebApplicationFactory.CreateAsync();
        DateTime grantedAt = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
        Guid orderId = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        TenantScenarioSeed.TenantScenarioResult owner;
        Guid profileStamp;
        Guid consentId;
        Guid submissionId;
        Guid versionId;
        Guid fieldId;

        await using (ExploreDbContext db = factory.CreateDatabase())
        {
            owner = await TenantScenarioSeed.SeedActiveTenantWithUserAsync(db);
            User user = await db.Users.Include(value => value.Pii).SingleAsync(value => value.Id == owner.UserId);
            user.FirstName = "Original";
            user.LastName = "Booker";
            db.Events.Add(new EventBuilder().WithId(eventId).WithActorId(owner.ActorId)
                .WithTenantId(owner.TenantId).WithTitle("Profile snapshot event").Build());

            RegistrationWorkflow workflow = RegistrationWorkflow.Create(owner.TenantId, eventId, "registration", grantedAt);
            RegistrationRequirement requirement = RegistrationRequirement.Create(
                workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
                RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_SYNC,
                RegistrationRequirementSubjectTypeEnum.AllOrders, null, grantedAt);
            RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, grantedAt);
            requirement.AddChannel(channel);
            workflow.AddRequirement(requirement);
            RegistrationForm form = RegistrationForm.Create(
                owner.TenantId, eventId, "platform.registration", "contact_permissions", "Contact permissions", grantedAt);
            RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, grantedAt);
            RegistrationFormSection section = RegistrationFormSection.Create(
                Guid.CreateVersion7(), version, 1, "Permissions", grantedAt);
            RegistrationFormField field = RegistrationFormField.Create(
                Guid.CreateVersion7(), section, 1, "registration", "event_updates", "Send event updates",
                RegistrationFieldTypeEnum.Consent, 1, RegistrationOrganizerVisibilityEnum.Hidden,
                true, false, grantedAt, "EVENT_UPDATES", "2026-09",
                "I agree to receive event updates by email.");
            version.AddSection(section);
            version.AddField(section, field);
            form.AddVersion(version);
            versionId = version.Id;
            fieldId = field.Id;

            EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(owner.TenantId, eventId, "EUR", 1);
            RegistrationOrder order = RegistrationOrder.Create(
                orderId, owner.TenantId, eventId, owner.UserId, null, BookingPartyTypeEnum.Individual,
                catalog.Id, RegistrationParticipationSnapshot.Create(
                    Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                    (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.AccountRequired,
                    GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                workflow.Id, null, "EUR", grantedAt, grantedAt.AddHours(2));
            order.SetPii(RegistrationOrderPii.CreateFromVerifiedContact(
                order.Id, owner.TenantId, "Original Booker", "booker@example.test", "+32470000000",
                "Original Organization", "BOOKER@EXAMPLE.TEST",
                (int)RegistrationRetentionPolicyEnum.StandardOperational, grantedAt));
            RegistrationAttempt attempt = RegistrationAttempt.Create(
                owner.TenantId, eventId, orderId, workflow.Id, requirement.Id, channel.Id, form.Id, version.Id,
                CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
                null, null, grantedAt, grantedAt.AddMinutes(10));
            RegistrationSubmission submission = attempt.SubmitNative(
                RegistrationEvidenceHash.Create(Convert.ToBase64String(SHA256.HashData([1, 2, 3]))),
                grantedAt.AddMinutes(1), null);
            RegistrationConsentRecord consent = RegistrationConsentRecord.Grant(
                submission, requirement, version, field, RegistrationAnswerSubjectTypeEnum.Purchaser,
                orderId, null, grantedAt.AddMinutes(1));
            consentId = consent.Id;
            submissionId = submission.Id;
            db.RegistrationWorkflows.Add(workflow);
            db.RegistrationForms.Add(form);
            // Match the native registration fixture's persisted published-version setup.
            db.Entry(version).Property(value => value.StatusId).CurrentValue = (int)RegistrationFormStatusEnum.Published;
            db.Entry(version).Property(value => value.SchemaHash).CurrentValue = "pinned-profile-snapshot-schema";
            db.Entry(version).Property(value => value.DataSchemaArtifact).CurrentValue = "{}";
            db.Entry(version).Property(value => value.UiSchemaArtifact).CurrentValue = "{}";
            db.Entry(version).Property(value => value.LogicSchemaArtifact).CurrentValue = "[]";
            db.Entry(version).Property(value => value.MappingArtifact).CurrentValue = "{}";
            db.Entry(version).Property(value => value.PublishedAt).CurrentValue = grantedAt;
            db.EventTicketCatalogVersions.Add(catalog);
            db.RegistrationOrders.Add(order);
            db.RegistrationAttempts.Add(attempt);
            db.RegistrationSubmissions.Add(submission);
            db.RegistrationConsentRecords.Add(consent);
            await db.SaveChangesAsync();
            profileStamp = user.ConcurrencyStamp;
        }

        await using (AsyncServiceScope edit = factory.Services.CreateAsyncScope())
        {
            edit.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
            // Exercise the real transactional handler and its real repositories, not the authorization decorator.
            var handler = ActivatorUtilities.CreateInstance<UpdateUserCommandHandler>(edit.ServiceProvider);
            var response = await handler.ExecuteAsync(new UpdateUserCommand
            {
                UserId = owner.UserId,
                ExpectedConcurrencyStamp = profileStamp,
                UpdateUserDto = new UpdateUserDto
                {
                    Names = new UpdateUserNamesDto { FirstName = "Edited", LastName = "Profile" }
                }
            });
            await Assert.That(response.IsSuccess).IsTrue();
        }

        await using AsyncServiceScope read = factory.Services.CreateAsyncScope();
        read.ServiceProvider.GetRequiredService<ITenantContextAccessor>().SetTenant(owner.TenantId);
        User? editedUser = await read.ServiceProvider.GetRequiredService<IUserRepository>().GetById(owner.UserId);
        await Assert.That(editedUser!.FirstName).IsEqualTo("Edited");
        await Assert.That(editedUser.LastName).IsEqualTo("Profile");
        RegistrationOrder? historicalOrder = await read.ServiceProvider
            .GetRequiredService<IRegistrationInventoryRepository>()
            .GetOrderWithPiiAsync(orderId, owner.TenantId, CancellationToken.None);
        await Assert.That(historicalOrder!.AccountUserId).IsEqualTo(owner.UserId);
        await Assert.That(historicalOrder.Pii!.ContactName).IsEqualTo("Original Booker");
        await Assert.That(historicalOrder.Pii.Email).IsEqualTo("booker@example.test");
        await Assert.That(historicalOrder.Pii.NormalizedEmail).IsEqualTo("BOOKER@EXAMPLE.TEST");
        await Assert.That(historicalOrder.Pii.IsEmailVerified).IsTrue();
        await Assert.That(historicalOrder.Pii.Phone).IsEqualTo("+32470000000");
        await Assert.That(historicalOrder.Pii.OrganizationName).IsEqualTo("Original Organization");

        ExploreDbContext stored = read.ServiceProvider.GetRequiredService<ExploreDbContext>();
        RegistrationConsentRecord evidence = await stored.RegistrationConsentRecords.AsNoTracking()
            .SingleAsync(value => value.Id == consentId && value.RegistrationOrderId == historicalOrder.Id);
        await Assert.That(evidence.RegistrationSubmissionId).IsEqualTo(submissionId);
        await Assert.That(evidence.RegistrationFormVersionId).IsEqualTo(versionId);
        await Assert.That(evidence.RegistrationFormVersion).IsEqualTo(1);
        await Assert.That(evidence.RegistrationFormFieldId).IsEqualTo(fieldId);
        await Assert.That(evidence.PurchaserSubjectId).IsEqualTo(orderId);
        await Assert.That(evidence.PurposeCode).IsEqualTo("EVENT_UPDATES");
        await Assert.That(evidence.ConsentTextSnapshot).IsEqualTo("I agree to receive event updates by email.");
        await Assert.That(evidence.ConsentTextVersion).IsEqualTo("2026-09");
        await Assert.That(evidence.LanguageTag).IsEqualTo("en");
        await Assert.That(evidence.GrantedAt).IsEqualTo(grantedAt.AddMinutes(1));
        await Assert.That(evidence.WithdrawnAt).IsNull();
    }
}
