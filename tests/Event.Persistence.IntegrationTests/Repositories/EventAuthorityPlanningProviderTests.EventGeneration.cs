using System.Collections.Immutable;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.Moderation;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Services.Moderation;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class EventAuthorityPlanningProviderTests
{
    [Test]
    [Arguments(false, false, false)]
    [Arguments(true, false, false)]
    [Arguments(false, true, false)]
    [Arguments(true, true, false)]
    [Arguments(false, false, true)]
    public async Task An_actor_source_save_can_commit_before_generation_or_adoption_takes_its_actor_anchor(
        bool adopting, bool resourceObservation, bool disclosureApproval)
    {
        var fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        using var timeout = new CancellationTokenSource(Deadline);
        var token = timeout.Token;
        var tenant = new Tenant
        {
            Id = Guid.CreateVersion7(),
            Slug = $"generation-source-{Guid.CreateVersion7():N}",
            FullName = "Generation source ordering",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        var publisher = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"publisher-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Publisher",
                LastName = "Witness"
            }
        };
        var administrator = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"administrator-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Administrator",
                LastName = "Witness"
            }
        };
        var adopter = new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"adopter-{Guid.CreateVersion7():N}@example.test",
                FirstName = "Adopter",
                LastName = "Witness"
            }
        };
        var actor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = publisher.Id,
            User = publisher,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Before generation" }
        };
        var entity = new Explore.Domain.Event(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ActorId = actor.Id,
            Actor = actor,
            SubmittedByUserId = publisher.Id,
            Title = "Generation source ordering",
            PublicCode = Guid.CreateVersion7().ToString("N")[..12],
            Timezone = "UTC",
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            VisibilityTypeId = (int)VisibilityTypeEnum.Private,
            VisibilityType = null!,
            EventFormatId = (int)EventFormatEnum.Local,
            EventFormat = null!,
            EventStatus = null!
        };
        ModerationHostBinding? host;
        Guid maintenanceId;
        await using (var seed = fixture.CreateSystemContext())
        {
            host = await seed.ModerationHostBindings.AsNoTracking().SingleOrDefaultAsync(token);
            var role = await seed.Roles.SingleAsync(row => row.MasterCode == "platform.admin"
                && row.RoleScopeId == (int)RoleScopeEnum.Platform, token);
            seed.AddRange(actor, entity, administrator, adopter);
            foreach (var human in new[] { administrator, adopter })
                seed.PlatformUserRoles.Add(new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = human.Id,
                    User = human,
                    RoleId = role.Id,
                    Role = role
                });
            foreach (var human in new[] { administrator, adopter, publisher })
                seed.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenant.Id,
                    Tenant = tenant,
                    UserId = human.Id,
                    User = human,
                    StatusId = (int)TenantUserStatusEnum.Active
                });
            maintenanceId = host?.MaintenanceServicePrincipalId ?? Guid.CreateVersion7();
            if (host is null)
                seed.ServicePrincipals.Add(new ServicePrincipal
                {
                    Id = maintenanceId,
                    Code = $"generation-maintenance-{Guid.CreateVersion7():N}",
                    DisplayName = "Generation maintenance"
                });
            await seed.SaveChangesAsync(token);
        }
        var http = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, administrator.Id.ToString("D"))], "event-generation-witness"))
            }
        };
        AdminContext Identity(ExploreDbContext context) => new(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
            new OrganizationMemberRepository(context),
            new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
        var machine = new MachinePrincipalAccessor(http);
        var trustDomainId = host?.TrustDomainId ?? Guid.CreateVersion7();
        var solutionId = host?.SolutionId ?? Guid.CreateVersion7();
        var instanceId = host?.InstanceId ?? Guid.CreateVersion7();
        var epochId = Guid.CreateVersion7();
        await using (var setup = fixture.CreateTenantContext(tenant.Id))
        {
            var installed = await new EstablishModerationAuthoritySourcesCommandHandler(
                new ModerationAuthoritySourceRepository(setup, Identity(setup), machine)).ExecuteAsync(new()
                {
                    HostBindingId = host?.Id ?? Guid.CreateVersion7(),
                    TrustDomainId = trustDomainId,
                    SolutionId = solutionId,
                    InstanceId = instanceId,
                    MaintenanceServicePrincipalId = maintenanceId,
                    TenantId = tenant.Id,
                    EpochId = epochId
                }, token);
            await Assert.That(installed.IsSuccess).IsTrue();
        }
        var beforeWriterTenant = new BeforeFence(typeof(Tenant));
        var beforeGenerationActor = new BeforeFence(typeof(Actor));
        await using var writer = fixture.CreateTenantContext(tenant.Id, beforeWriterTenant);
        await using var owner = fixture.CreateTenantContext(tenant.Id, beforeGenerationActor);
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        var clock = TimeProvider.System;
        var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
        var identity = Identity(owner);
        var publications = new ModerationPolicyPublicationRepository(owner, identity, machine, inventory, null, clock);
        var activation = new ModerationNativeActivationQualification(owner, inventory, null, null, clock);
        var repository = new ModerationQualificationRepository(owner, identity, machine, publications, activation, clock,
            privacy.Authority(owner, clock), deploymentInventory: inventory);
        var source = new RecordModerationEventGenerationSource
        {
            OperationId = Guid.CreateVersion7(),
            SourceId = Guid.CreateVersion7(),
            EventId = entity.Id,
            Target = AuthorityScope.Tenant(AuthorityScope.Instance(
                AuthorityScope.Solution(AuthorityScope.TrustDomain(trustDomainId), solutionId), instanceId), tenant.Id),
            ExpectedQualificationRevision = 1,
            ExpectedEventRevision = 0,
            ValidUntil = clock.GetUtcNow().AddMinutes(10)
        };
        ModerationQualificationMutation mutation = source;
        if (resourceObservation)
        {
            var resourceId = Guid.CreateVersion7();
            var resource = EventResource.CreateDraft(resourceId, tenant.Id, entity.Id, null,
                new EventResourceMetadata
                {
                    Title = "Resource generation ordering",
                    Kind = EventResourceKindEnum.GeneralDocument,
                    DisclosureMode = EventResourceDisclosureModeEnum.EligibleOnly
                }, EventResourceDeliveryTypeEnum.ExternalLink, EventResourceAvailability.Create(),
                [EventResourceAudienceRule.Create(tenant.Id, entity.Id, resourceId, EventResourceAudienceKindEnum.Organizer)],
                administrator.Id, clock.GetUtcNow().UtcDateTime);
            resource.SetExternalDestination("encrypted-test-envelope", 1, "https://example.invalid",
                resource.ConcurrencyStamp, administrator.Id, clock.GetUtcNow().UtcDateTime);
            await using var seed = fixture.CreateTenantContext(tenant.Id);
            seed.Set<EventResource>().Add(resource);
            await seed.SaveChangesAsync(token);
            mutation = new RecordModerationResourceGenerationSource
            {
                OperationId = Guid.CreateVersion7(),
                SourceId = source.SourceId,
                ResourceId = resource.Id,
                Target = source.Target,
                ExpectedQualificationRevision = 1,
                ExpectedResourceRevision = 0,
                ValidUntil = source.ValidUntil
            };
        }
        if (adopting)
        {
            await using var setup = fixture.CreateTenantContext(tenant.Id);
            var setupIdentity = Identity(setup);
            var setupPublications = new ModerationPolicyPublicationRepository(setup, setupIdentity, machine, inventory, null, clock);
            var setupRepository = new ModerationQualificationRepository(setup, setupIdentity, machine, setupPublications,
                new ModerationNativeActivationQualification(setup, inventory, null, null, clock), clock,
                privacy.Authority(setup, clock), deploymentInventory: inventory);
            var handler = new ApplyModerationQualificationCommandHandler(setupRepository);
            var adoptedTargetId = resourceObservation ? ((RecordModerationResourceGenerationSource)mutation).ResourceId : entity.Id;
            await Assert.That((await handler.ExecuteAsync(new(mutation), token)).IsSuccess).IsTrue();
            var opened = new OpenModerationCaseSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 2,
                CaseId = Guid.CreateVersion7(),
                SubjectKind = ModerationSubjectKind.Human,
                SubjectId = publisher.Id,
                OccurrenceId = Guid.CreateVersion7()
            };
            await Assert.That((await handler.ExecuteAsync(new(opened), token)).IsSuccess).IsTrue();
            var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG06);
            var response = reference.Responses[0];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(tenant.Id, code, 1);
            var classification = Rule("adoption-source-class");
            var reason = Rule("adoption-source-reason");
            var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("adoption-source-witness"), reason,
                reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
                [ModerationSubjectKind.Human], reference.Effects.ToImmutableArray(), reference.Bundle,
                reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
                response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), source.Target,
                ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(classification, "Native adoption classification.", reference.Ground, reference.Basis)],
                [new(reason, "Native source authority and protected response.", classification)], [profile], [profile.Rule]);
            var published = await setupPublications.PublishAsync(new(Guid.CreateVersion7(), host?.Id
                ?? await setup.ModerationHostBindings.AsNoTracking().Select(row => row.Id).SingleAsync(token),
                epochId, 3, ModerationPolicyPublicationProjection.WriterGeneration, null, proposed), token);
            await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            var now = clock.GetUtcNow();
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
                SubjectReference.Create(ModerationSubjectKind.Human, publisher.Id, source.Target.Parent!),
                SubjectReference.Create(ModerationSubjectKind.Human, administrator.Id, source.Target.Parent!),
                source.Target, source.Target, profile.Rule, [ModerationCapability.Publish], now, now.AddHours(1), null, 1, now);
            setup.ModerationTrustDomainId = trustDomainId;
            setup.ModerationDecisions.Add(decision);
            await setup.SaveChangesAsync(token);
            setup.ModerationTrustDomainId = null;
            var target = new ModerationPolicyTarget(source.Target, decision.Subject.Kind, decision.Subject.Id,
                adoptedTargetId, 1, resourceObservation ? "resource.read" : "event.publish", null);
            var selection = new RecordModerationContributionSelectionSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 4,
                SelectionId = Guid.CreateVersion7(),
                EffectId = Guid.CreateVersion7(),
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                DecisionId = decision.Id,
                ExpectedDecisionRevision = 2,
                PolicyId = proposed.PolicyId,
                ExpectedPolicyRevision = 1,
                PublicationId = published.Publication!.Id,
                ProfileId = profile.Id,
                Contribution = new(decision.Id, decision.Rule, decision.Scope, decision.Subject.Kind, decision.Subject.Id,
                    decision.StartsAt, null, profile.Effects.Select(effect => new ModerationPolicyEffectTarget(effect, target)).ToImmutableArray(),
                    [new(null, target)]),
                ValidUntil = source.ValidUntil
            };
            await Assert.That((await handler.ExecuteAsync(new(selection), token)).IsSuccess).IsTrue();
            mutation = resourceObservation ? new AdoptModerationResourceTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 5,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selection.SelectionId,
                BindingOrdinal = 0,
                GenerationId = source.SourceId,
                ExpectedGenerationRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = now.AddMinutes(5)
            } : new AdoptModerationEventTargetSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 5,
                AdoptionId = Guid.CreateVersion7(),
                SelectionId = selection.SelectionId,
                BindingOrdinal = 0,
                GenerationId = source.SourceId,
                ExpectedGenerationRevision = 1,
                CaseId = opened.CaseId,
                ExpectedCaseRevision = 1,
                ValidUntil = now.AddMinutes(5)
            };
            http.HttpContext!.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, adopter.Id.ToString("D"))], "event-adoption-witness"));
        }
        if (disclosureApproval)
        {
            await using var setup = fixture.CreateTenantContext(tenant.Id);
            var setupIdentity = Identity(setup);
            var setupPublications = new ModerationPolicyPublicationRepository(setup, setupIdentity, machine, inventory, null, clock);
            var setupRepository = new ModerationQualificationRepository(setup, setupIdentity, machine, setupPublications,
                new ModerationNativeActivationQualification(setup, inventory, null, null, clock), clock,
                privacy.Authority(setup, clock), deploymentInventory: inventory);
            var opened = new OpenModerationCaseSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 1,
                CaseId = Guid.CreateVersion7(),
                SubjectKind = ModerationSubjectKind.Content,
                SubjectId = entity.Id,
                OccurrenceId = Guid.CreateVersion7()
            };
            await Assert.That((await new ApplyModerationQualificationCommandHandler(setupRepository)
                .ExecuteAsync(new(opened), token)).IsSuccess).IsTrue();
            var reference = ModerationReferenceCatalogue.GetProfile(ModerationReferenceProfileId.CFG09);
            var response = reference.Responses[0];
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(tenant.Id, code, 1);
            var classification = Rule("disclosure-source-class");
            var reason = Rule("disclosure-source-reason");
            var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("disclosure-source-witness"), reason,
                reference.Id, reference.Revision, response.Grade, [ModerationScopeKind.Tenant],
                [ModerationSubjectKind.Content], reference.Effects.ToImmutableArray(), reference.Bundle,
                reference.ProtectionPurpose, [], new(response.Timing.Kind, TimeSpan.FromHours(1)),
                response.RequiredApprovals, 6, ModerationReferenceRecognition.Never);
            const string classMeaning = "Native disclosure classification.";
            const string reasonMeaning = "Original Content representative custody.";
            var proposed = new ModerationPolicyProposedState(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), source.Target,
                ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(classification, classMeaning, reference.Ground, reference.Basis)],
                [new(reason, reasonMeaning, classification)], [profile], [profile.Rule]);
            var installedHostId = await setup.ModerationHostBindings.AsNoTracking().Select(row => row.Id).SingleAsync(token);
            var published = await setupPublications.PublishAsync(new(Guid.CreateVersion7(), installedHostId,
                epochId, 2, ModerationPolicyPublicationProjection.WriterGeneration, null, proposed), token);
            await Assert.That(published.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            var now = clock.GetUtcNow();
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), opened.CaseId,
                SubjectReference.Create(ModerationSubjectKind.Content, entity.Id, source.Target),
                SubjectReference.Create(ModerationSubjectKind.Human, administrator.Id, source.Target.Parent!),
                source.Target, source.Target, profile.Rule, [ModerationCapability.Publish], now, now.AddHours(1), null, 1, now);
            var representative = EventRoleAssignment.Create(tenant.Id, entity.Id, publisher.Id,
                (int)RoleEnum.EventOwner, EventRoleAssignmentStatus.Active,
                now.AddHours(-1).UtcDateTime, now.AddMinutes(20).UtcDateTime, administrator.Id);
            setup.ModerationTrustDomainId = trustDomainId;
            setup.AddRange(decision, representative);
            await setup.SaveChangesAsync(token);
            mutation = new ApproveModerationNoticeDisclosureSource
            {
                OperationId = Guid.CreateVersion7(),
                Target = source.Target,
                ExpectedQualificationRevision = 3,
                Intent = new ModerationNoticeDisclosureIntent(Guid.CreateVersion7(), Guid.CreateVersion7(), decision.Id, 2,
                    opened.CaseId, 1, proposed.PolicyId, 1, published.Publication!.Id, profile.Id,
                    Guid.CreateVersion7(), new string('a', 64), new(classMeaning), new(reasonMeaning),
                    [new("Original Content explanation.")], new("Protected response."), new("Independent review."),
                    new("No automated finding."), new("Purpose-bound retention."), now.AddMinutes(10), now.AddYears(1))
                { ContentRepresentatives = [new(representative.Id, representative.Version, publisher.Id)] }
            };
        }
        Task write = new EfCoreUnitOfWork(writer).ExecuteSerializableAsync(async ct =>
        {
            var current = await writer.Actors.Include(row => row.Pii).SingleAsync(row => row.Id == actor.Id, ct);
            current.Pii!.DisplayName = "After generation source update";
            await writer.SaveChangesAsync(ct);
            return true;
        }, token);
        Task<Explore.Application.Features.Moderation.Requests.Commands.ModerationQualificationResponse>? generation = null;
        try
        {
            await beforeWriterTenant.Reached.WaitAsync(Deadline, token);
            generation = new ApplyModerationQualificationCommandHandler(repository).ExecuteAsync(new(mutation), token);
            await beforeGenerationActor.Reached.WaitAsync(Deadline, token);
            beforeWriterTenant.Release();
            // The ordinary Actor save must commit before qualification is allowed to acquire Actor.
            await write.WaitAsync(Deadline, token);
        }
        finally
        {
            beforeWriterTenant.Release();
            beforeGenerationActor.Release();
            await Task.WhenAll(write, (Task?)generation ?? Task.CompletedTask).WaitAsync(Deadline);
        }
        await Assert.That((await generation!).IsSuccess).IsEqualTo(!adopting);
        await using var reader = fixture.CreateTenantContext(tenant.Id);
        reader.ModerationTrustDomainId = trustDomainId;
        var updatedActor = await reader.Actors.Include(row => row.Pii).SingleAsync(row => row.Id == actor.Id, token);
        await Assert.That(updatedActor.Pii!.DisplayName).IsEqualTo("After generation source update");
        if (disclosureApproval)
        {
            await Assert.That(await reader.ModerationNoticeDisclosureApprovals.CountAsync(token)).IsEqualTo(1);
            await Assert.That(await reader.ModerationQualificationMutationReceipts
                .CountAsync(row => row.Id == mutation.OperationId, token)).IsEqualTo(1);
        }
        else if (resourceObservation)
        {
            var recorded = await reader.ModerationResourceGenerations.SingleAsync(row => row.Id == source.SourceId, token);
            if (adopting)
            {
                await Assert.That(recorded.ActorStamp).IsNotEqualTo(updatedActor.ConcurrencyStamp);
                await Assert.That(await reader.ModerationResourceTargetAdoptions.AnyAsync(token)).IsFalse();
                await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == mutation.OperationId, token)).IsFalse();
            }
            else await Assert.That(recorded.ActorStamp).IsEqualTo(updatedActor.ConcurrencyStamp);
        }
        else
        {
            var recorded = await reader.ModerationEventGenerations.SingleAsync(row => row.Id == source.SourceId, token);
            if (adopting)
            {
                await Assert.That(recorded.ActorStamp).IsNotEqualTo(updatedActor.ConcurrencyStamp);
                await Assert.That(await reader.ModerationEventTargetAdoptions.AnyAsync(token)).IsFalse();
                await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == mutation.OperationId, token)).IsFalse();
            }
            else await Assert.That(recorded.ActorStamp).IsEqualTo(updatedActor.ConcurrencyStamp);
        }
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync(row => row.Id == epochId, token)).Revision)
            .IsEqualTo(disclosureApproval ? 4L : adopting ? 5L : 2L);
    }
}
