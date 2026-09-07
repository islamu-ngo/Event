using DotNet.Testcontainers.Containers;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.PrivacyErasure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Services;
using Explore.Infrastructure.Services.Privacy;
using Explore.Persistence;
using Explore.Persistence.Privacy.ErasureAuthority;
using Explore.Persistence.Privacy.ErasureAuthority.Repositories;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Explore.Persistence.Schema;
using Explore.Persistence.Seed;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Testcontainers.PostgreSql;
using TUnit.Core;
using TUnit.Core.Interfaces;

namespace Event.Persistence.IntegrationTests.Privacy;

[Category("EventLocationPrivacy")]
[ClassDataSource<ExternalDatabasePrivacyErasurePostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class GlobalLocationPrivacyErasureTests(ExternalDatabasePrivacyErasurePostgreSqlFixture fixture)
{
    [Test]
    public async Task OwnerPrivateHomeQuery_ReturnsExactCrossTenantSetWithoutEnumeratingOtherRows()
    {
        await using var seedContext = fixture.CreateDbContext();

        var tenantA = CreateTenant("global-erasure-a");
        var tenantB = CreateTenant("global-erasure-b");
        var owner = CreateUser("global-erasure-owner");
        var unrelatedOwner = CreateUser("global-erasure-unrelated");
        seedContext.AddRange(tenantA, tenantB, owner, unrelatedOwner);
        await seedContext.SaveChangesAsync();

        var tenantAHome = CreatePrivateHome(tenantA.Id, owner.Id, "Owner home A");
        var tenantBHome = CreatePrivateHome(tenantB.Id, owner.Id, "Owner home B");
        var unrelatedHome = CreatePrivateHome(tenantA.Id, unrelatedOwner.Id, "Unrelated home");
        var nonHome = CreateNonHome(tenantB.Id, "Commercial venue");
        seedContext.Locations.AddRange(tenantAHome, tenantBHome, unrelatedHome, nonHome);
        await seedContext.SaveChangesAsync();

        await using var tenantAContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var repository = new LocationRepository(tenantAContext);
        await using (var transaction = await tenantAContext.Database.BeginTransactionAsync())
        {
            try
            {
                await tenantAContext.Database.ExecuteSqlRawAsync(
                    $"ALTER TABLE {RelationalModelNamespace.Name}.locations DROP CONSTRAINT ck_locations_owner_private_home");
                await tenantAContext.Database.ExecuteSqlRawAsync(
                    $"UPDATE {RelationalModelNamespace.Name}.locations SET owner_user_id = @p0 WHERE id = @p1",
                    owner.Id,
                    nonHome.Id);

                Guid[] ownerRowsWithoutKindBoundary = await tenantAContext.Locations
                    .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
                    .Where(location => location.OwnerUserId == owner.Id)
                    .Select(location => location.Id)
                    .ToArrayAsync();
                List<Location> result =
                    await repository.GetOwnedPrivateHomesForGlobalErasureAsync(owner.Id);

                await Assert.That(ownerRowsWithoutKindBoundary)
                    .Contains(nonHome.Id);
                await Assert.That(result.Select(location => location.Id))
                    .IsEquivalentTo([tenantAHome.Id, tenantBHome.Id]);
                await Assert.That(result.Select(location => location.Id))
                    .DoesNotContain(unrelatedHome.Id);
                await Assert.That(result.Select(location => location.Id))
                    .DoesNotContain(nonHome.Id);
                await Assert.That(result.All(location => location.OwnerUserId == owner.Id))
                    .IsTrue();
                await Assert.That(result.All(location =>
                    location.LocationKindId == (int)LocationKindEnum.PrivateHome))
                    .IsTrue();
                await Assert.That(result.Select(location => location.TenantId))
                    .IsEquivalentTo([tenantA.Id, tenantB.Id]);
                await Assert.That(result.All(location => location.Pii is not null))
                    .IsTrue();
                await Assert.That(result.All(location =>
                    tenantAContext.Entry(location).State == EntityState.Unchanged))
                    .IsTrue();
                await Assert.That(result.All(location =>
                    tenantAContext.Entry(location.Pii!).State == EntityState.Unchanged))
                    .IsTrue();
            }
            finally
            {
                await transaction.RollbackAsync();
            }
        }

        tenantAContext.ChangeTracker.Clear();
        Guid? restoredOwner = await tenantAContext.Locations
            .IgnoreTenantFilter(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(location => location.Id == nonHome.Id)
            .Select(location => location.OwnerUserId)
            .SingleAsync();
        await Assert.That(restoredOwner).IsNull();
    }

    [Test]
    public async Task OwnerPrivateHomeQuery_RejectsEmptyOwnerIdBeforeDatabaseAccess()
    {
        await using var context = fixture.CreateTenantFilteredDbContext();
        var repository = new LocationRepository(context);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.GetOwnedPrivateHomesForGlobalErasureAsync(Guid.Empty));

        await Assert.That(exception!.ParamName).IsEqualTo("ownerUserId");
    }

    [Test]
    public async Task MembershipAndPreferenceErasure_RemovesExactSubjectAcrossTenantsOnly()
    {
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("preference-erasure-a");
        var tenantB = CreateTenant("preference-erasure-b");
        var subject = CreateUser("preference-erasure-subject");
        var unrelated = CreateUser("preference-erasure-unrelated");
        seedContext.AddRange(tenantA, tenantB, subject, unrelated);
        seedContext.UserPreferences.AddRange(
            CreatePreference(tenantA, subject.Id, "subject-a"),
            CreatePreference(tenantB, subject.Id, "subject-b"),
            CreatePreference(tenantA, unrelated.Id, "unrelated"));
        await seedContext.SaveChangesAsync();

        await using var erasureContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var repository = new UserLocationPrivacyErasureRepository(erasureContext);

        await repository.EraseMembershipsAndPreferencesAsync(subject.Id, CancellationToken.None);

        Guid[] remainingUsers = await erasureContext.UserPreferences
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(value => value.UserId == subject.Id || value.UserId == unrelated.Id)
            .Select(value => value.UserId)
            .ToArrayAsync();
        await Assert.That(remainingUsers).IsEquivalentTo([unrelated.Id]);
    }

    [Test]
    public async Task RetainedAuditErasure_AnonymizesExactSubjectAndPreservesUnrelatedEvidence()
    {
        await using var seedContext = fixture.CreateDbContext();
        var tenantA = CreateTenant("audit-erasure-a");
        var tenantB = CreateTenant("audit-erasure-b");
        var subject = CreateUser("audit-erasure-subject");
        var unrelated = CreateUser("audit-erasure-unrelated");
        seedContext.AddRange(tenantA, tenantB, subject, unrelated);
        var subjectActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = subject.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Subject reviewer" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        var unrelatedActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = unrelated.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Unrelated reviewer" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        Explore.Domain.Event subjectEvent = CreateEvent(tenantA.Id, subjectActor.Id, "Subject claim event");
        Explore.Domain.Event unrelatedEvent = CreateEvent(tenantB.Id, unrelatedActor.Id, "Unrelated claim event");
        subjectEvent.SubmittedByUserId = subject.Id;
        subjectEvent.SourcePublisherName = "Subject event publisher";
        unrelatedEvent.SubmittedByUserId = unrelated.Id;
        unrelatedEvent.SourcePublisherName = "Unrelated event publisher";
        EventOrganizerClaim subjectClaim = EventOrganizerClaim.CreatePending(
            tenantA.Id, subjectEvent.Id, subjectActor.Id, "test", "subject", DateTime.UtcNow);
        EventOrganizerClaim unrelatedClaim = EventOrganizerClaim.CreatePending(
            tenantB.Id, unrelatedEvent.Id, unrelatedActor.Id, "test", "unrelated", DateTime.UtcNow);
        subjectClaim.Reject(subject.Id, "reviewed", DateTime.UtcNow);
        unrelatedClaim.Reject(unrelated.Id, "reviewed", DateTime.UtcNow);
        seedContext.AddRange(subjectActor, unrelatedActor, subjectEvent, unrelatedEvent, subjectClaim, unrelatedClaim);
        AuditLog subjectA = CreateAudit(tenantA, subject.Id, "subject-a");
        AuditLog subjectB = CreateAudit(tenantB, subject.Id, "subject-b");
        AuditLog unrelatedA = CreateAudit(tenantA, unrelated.Id, "unrelated");
        seedContext.AuditLogs.AddRange(subjectA, subjectB, unrelatedA);
        await seedContext.SaveChangesAsync();

        await using var erasureContext = fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantA.Id));
        var repository = new UserLocationPrivacyErasureRepository(erasureContext);

        await repository.AnonymizeRetainedAuditEvidenceAsync(subject.Id, CancellationToken.None);

        AuditLog[] subjectRows = await erasureContext.AuditLogs
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .Where(value => value.Id == subjectA.Id || value.Id == subjectB.Id)
            .ToArrayAsync();
        AuditLog unrelatedRow = await erasureContext.AuditLogs
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == unrelatedA.Id);
        await Assert.That(subjectRows.All(value =>
            value.ActorId is null && value.OldValues is null && value.NewValues is null)).IsTrue();
        await Assert.That(unrelatedRow.ActorId).IsEqualTo(unrelated.Id);
        await Assert.That(unrelatedRow.OldValues).IsNotNull();
        await Assert.That(unrelatedRow.NewValues).IsNotNull();
        EventOrganizerClaim subjectClaimRow = await erasureContext.EventOrganizerClaims
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == subjectClaim.Id);
        EventOrganizerClaim unrelatedClaimRow = await erasureContext.EventOrganizerClaims
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == unrelatedClaim.Id);
        await Assert.That(subjectClaimRow.ReviewerUserId).IsNull();
        await Assert.That(unrelatedClaimRow.ReviewerUserId).IsEqualTo(unrelated.Id);
        Explore.Domain.Event subjectEventRow = await erasureContext.Events
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == subjectEvent.Id);
        Explore.Domain.Event unrelatedEventRow = await erasureContext.Events
            .IgnoreAllFilters(TenantFilterBypassReasons.UserPrivacyErasure)
            .SingleAsync(value => value.Id == unrelatedEvent.Id);
        await Assert.That(subjectEventRow.SubmittedByUserId).IsNull();
        await Assert.That(subjectEventRow.SourcePublisherName).IsEqualTo("Subject event publisher");
        await Assert.That(unrelatedEventRow.SubmittedByUserId).IsEqualTo(unrelated.Id);
        await Assert.That(unrelatedEventRow.SourcePublisherName).IsEqualTo("Unrelated event publisher");
    }

    [Test]
    [Timeout(240_000)]
    public async Task AuthorityFirstRollback_PersistsFenceAndRestoredBehindReplay_AreIdempotent()
    {
        ErasureGraph graph;
        await using (var seedContext = fixture.CreateDbContext())
        {
            graph = await SeedErasureGraphAsync(seedContext);
            await InstallOutboxFailureTriggerAsync(seedContext);
        }

        await using PrivacyErasureAuthorityDbContext authorityContext = fixture.CreateAuthorityDbContext();
        var authority = new EfCorePrivacyErasureAuthorityRepository(
            authorityContext,
            Options.Create(new PrivacyErasureOptions()));
        try
        {
            await using var failingContext = fixture.CreateDbContext();
            await using ErasureRuntime failingRuntime = CreateRuntime(failingContext, authority);

            DbUpdateException? failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                failingRuntime.Service.EraseUserAsync(
                    graph.OwnerUserId,
                    Guid.CreateVersion7(),
                    CancellationToken.None));

            PostgresException providerFailure = (PostgresException)failure!.InnerException!;
            await Assert.That(providerFailure.SqlState).IsEqualTo("P0001");
        }
        finally
        {
            await using var triggerContext = fixture.CreateDbContext();
            await RemoveOutboxFailureTriggerAsync(triggerContext);
        }

        PrivacyErasureIntent retained;
        await using (var rollbackContext = fixture.CreateDbContext())
        {
            Location[] homes = await rollbackContext.Locations
                .IgnoreQueryFilters()
                .Include(location => location.Pii)
                .Where(location => graph.LocationIds.Contains(location.Id))
                .OrderBy(location => location.Id)
                .ToArrayAsync();
            LocationRoom[] rooms = await rollbackContext.LocationRooms
                .IgnoreQueryFilters()
                .Where(room => graph.RoomIds.Contains(room.Id))
                .ToArrayAsync();

            await Assert.That(homes.All(home =>
                home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Active
                && home.OwnerUserId == graph.OwnerUserId
                && home.Pii is not null)).IsTrue();
            await Assert.That(homes.Select(home => home.FullName))
                .IsEquivalentTo(graph.HomeNames);
            await Assert.That(rooms.Single(room => room.Id == graph.RoomIds[0]).IsDeleted)
                .IsFalse();
            await Assert.That(rooms.Single(room => room.Id == graph.RoomIds[1]).IsDeleted)
                .IsTrue();
            await Assert.That(rooms.All(room => room.Description is not null)).IsTrue();
            await Assert.That(await rollbackContext.Users
                .IgnoreQueryFilters()
                .AnyAsync(user => user.Id == graph.OwnerUserId && !user.IsDeleted)).IsTrue();
            await Assert.That(await rollbackContext.UserPii
                .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsTrue();
            await Assert.That(await rollbackContext.UserPii
                .AnyAsync(pii => pii.UserId == graph.UnrelatedUserId)).IsTrue();
            await Assert.That(await rollbackContext.UserAuthenticationTokens
                .CountAsync(token => token.UserId == graph.OwnerUserId)).IsEqualTo(2);
            await Assert.That(await rollbackContext.UserAuthenticationTokens
                .CountAsync(token => token.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await rollbackContext.UserExternalLogins
                .CountAsync(login => login.UserId == graph.OwnerUserId)).IsEqualTo(2);
            await Assert.That(await rollbackContext.UserExternalLogins
                .CountAsync(login => login.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await rollbackContext.AtprotoIdentities
                .AnyAsync(identity => identity.ActorId == graph.OwnerActorId && !identity.IsDeleted)).IsTrue();
            await Assert.That(await rollbackContext.AtprotoIdentities
                .AnyAsync(identity => identity.ActorId == graph.UnrelatedActorId && !identity.IsDeleted)).IsTrue();
            await Assert.That(await rollbackContext.TenantUsers
                .CountAsync(membership => membership.UserId == graph.OwnerUserId)).IsEqualTo(2);
            await Assert.That(await rollbackContext.TenantUsers
                .CountAsync(membership => membership.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await rollbackContext.UserPreferences
                .CountAsync(preference => preference.UserId == graph.OwnerUserId)).IsEqualTo(2);
            await Assert.That(await rollbackContext.UserPreferences
                .CountAsync(preference => preference.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await rollbackContext.PrivacyErasureReplayCheckpoints.CountAsync())
                .IsEqualTo(0);
            await Assert.That(await rollbackContext.OutboxMessages.CountAsync(message =>
                message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType))
                .IsEqualTo(0);
        }

        IReadOnlyList<PrivacyErasureIntent> pending =
            await authority.ReadAfterAsync(0, 10);
        await Assert.That(pending.Count).IsEqualTo(1);
        retained = pending.Single();
        await Assert.That(retained.SubjectId).IsEqualTo(graph.OwnerUserId);
        await Assert.That(retained.SubjectKind).IsEqualTo(PrivacyErasureSubjectKind.User);

        PrivacyErasureIntent duplicate = await authority.AppendAsync(
            new PrivacyErasureRequest(
                retained.IntentId,
                retained.SubjectKind,
                retained.SubjectId,
                retained.ReasonCode,
                retained.PolicyVersion));
        await Assert.That(duplicate.AuthoritySequence).IsEqualTo(retained.AuthoritySequence);
        await Assert.That((await authority.ReadAfterAsync(0, 10)).Count).IsEqualTo(1);

        await using (var replayContext = fixture.CreateDbContext())
        await using (ErasureRuntime replayRuntime = CreateRuntime(replayContext, authority))
        {
            await replayRuntime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        int checkpointCount;
        int outboxCount;
        await using (var committedContext = fixture.CreateDbContext())
        {
            Location[] homes = await committedContext.Locations
                .IgnoreQueryFilters()
                .Include(location => location.Pii)
                .Where(location => graph.LocationIds.Contains(location.Id))
                .OrderBy(location => location.Id)
                .ToArrayAsync();
            LocationRoom[] rooms = await committedContext.LocationRooms
                .IgnoreQueryFilters()
                .Where(room => graph.RoomIds.Contains(room.Id))
                .ToArrayAsync();
            EventLocation[] eventLocations = await committedContext.EventLocations
                .IgnoreQueryFilters()
                .Where(eventLocation => graph.EventLocationIds.Contains(eventLocation.Id))
                .OrderBy(eventLocation => eventLocation.Id)
                .ToArrayAsync();
            EventLocationDisclosureAudit[] retainedDisclosureAudits = await committedContext
                .EventLocationDisclosureAudits
                .IgnoreQueryFilters()
                .Where(audit => audit.Id == graph.SubjectDisclosureAuditId
                    || audit.Id == graph.UnrelatedDisclosureAuditId)
                .ToArrayAsync();
            EventLocationExactReadAudit[] retainedExactReadAudits = await committedContext
                .EventLocationExactReadAudits
                .IgnoreQueryFilters()
                .Where(audit => audit.Id == graph.SubjectExactReadAuditId
                    || audit.Id == graph.UnrelatedExactReadAuditId)
                .ToArrayAsync();
            EventLocationDisclosureAudit[] remediationAudits = await committedContext
                .EventLocationDisclosureAudits
                .IgnoreQueryFilters()
                .Where(audit => graph.EventLocationIds.Contains(audit.EventLocationId)
                    && audit.Reason == EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation)
                .ToArrayAsync();
            OutboxMessage[] messages = await committedContext.OutboxMessages
                .Where(message =>
                    message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                    || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType)
                .OrderBy(message => message.Id)
                .ToArrayAsync();
            PrivacyErasureReplayCheckpoint checkpoint = await committedContext
                .PrivacyErasureReplayCheckpoints
                .SingleAsync();
            ActorPii ownerActorPii = await committedContext.ActorPii
                .IgnoreQueryFilters()
                .SingleAsync(pii => pii.ActorId == graph.OwnerActorId);
            AtprotoIdentity ownerIdentity = await committedContext.AtprotoIdentities
                .IgnoreQueryFilters()
                .SingleAsync(identity => identity.ActorId == graph.OwnerActorId);

            await Assert.That(homes.All(home =>
                home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased
                && home.OwnerUserId is null
                && home.Pii is null
                && home.FullName == Location.ErasedPrivateVenueLabel
                && home.City == string.Empty)).IsTrue();
            await Assert.That(rooms.All(room =>
                room.IsDeleted
                && room.Name.StartsWith("privacy-erased-", StringComparison.Ordinal)
                && room.Description is null
                && graph.LocationIds.Contains(room.LocationId))).IsTrue();
            await Assert.That(eventLocations.All(eventLocation =>
                eventLocation.LocationId.HasValue
                && graph.LocationIds.Contains(eventLocation.LocationId.Value)
                && eventLocation.NeedsPrivacyReview
                && !eventLocation.ShowVenueName
                && !eventLocation.ShowCity
                && !eventLocation.ShowCountry
                && !eventLocation.ShowRoomName
                && !eventLocation.ShowStreetAddress
                && !eventLocation.ShowPostcode
                && !eventLocation.ShowCoordinates
                && eventLocation.FullDetailsAudienceId == (int)LocationDisclosureAudienceEnum.Never
                && eventLocation.PolicyVersion == 3)).IsTrue();
            EventLocationDisclosureAudit subjectDisclosureAudit = retainedDisclosureAudits
                .Single(audit => audit.Id == graph.SubjectDisclosureAuditId);
            EventLocationDisclosureAudit unrelatedDisclosureAudit = retainedDisclosureAudits
                .Single(audit => audit.Id == graph.UnrelatedDisclosureAuditId);
            EventLocationExactReadAudit subjectExactReadAudit = retainedExactReadAudits
                .Single(audit => audit.Id == graph.SubjectExactReadAuditId);
            EventLocationExactReadAudit unrelatedExactReadAudit = retainedExactReadAudits
                .Single(audit => audit.Id == graph.UnrelatedExactReadAuditId);
            await Assert.That(subjectDisclosureAudit.ActorUserId).IsEqualTo(graph.OwnerUserId);
            await Assert.That(subjectDisclosureAudit.Reason)
                .IsEqualTo(EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange);
            await Assert.That(subjectDisclosureAudit.PreviousPolicyVersion).IsEqualTo(1);
            await Assert.That(subjectDisclosureAudit.NewPolicyVersion).IsEqualTo(2);
            await Assert.That(unrelatedDisclosureAudit.ActorUserId).IsEqualTo(graph.UnrelatedUserId);
            await Assert.That(unrelatedDisclosureAudit.Reason)
                .IsEqualTo(EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange);
            await Assert.That(unrelatedDisclosureAudit.PreviousPolicyVersion).IsEqualTo(1);
            await Assert.That(unrelatedDisclosureAudit.NewPolicyVersion).IsEqualTo(2);
            await Assert.That(subjectExactReadAudit.RequesterUserId).IsEqualTo(graph.OwnerUserId);
            await Assert.That(subjectExactReadAudit.Purpose)
                .IsEqualTo(EventLocationExactReadPurposeEnum.EventManagement);
            await Assert.That(subjectExactReadAudit.WasAuthorized).IsTrue();
            await Assert.That(unrelatedExactReadAudit.RequesterUserId).IsEqualTo(graph.UnrelatedUserId);
            await Assert.That(unrelatedExactReadAudit.Purpose)
                .IsEqualTo(EventLocationExactReadPurposeEnum.SupportCaseReview);
            await Assert.That(unrelatedExactReadAudit.WasAuthorized).IsFalse();
            await Assert.That(remediationAudits.Select(audit => audit.EventLocationId))
                .IsEquivalentTo(graph.EventLocationIds);
            await Assert.That(remediationAudits.All(audit =>
                audit.ActorUserId == graph.OwnerUserId
                && audit.PreviousFields != EventLocationDisclosureFields.None
                && audit.NewFields == EventLocationDisclosureFields.None
                && audit.PreviousAudienceId != (int)LocationDisclosureAudienceEnum.Never
                && audit.NewAudienceId == (int)LocationDisclosureAudienceEnum.Never
                && audit.PreviousPolicyVersion == 2
                && audit.NewPolicyVersion == 3)).IsTrue();
            await Assert.That(await committedContext.Users
                .IgnoreQueryFilters()
                .AnyAsync(user => user.Id == graph.OwnerUserId && user.IsDeleted)).IsTrue();
            await Assert.That(await committedContext.UserPii
                .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsFalse();
            await Assert.That(await committedContext.UserPii
                .AnyAsync(pii => pii.UserId == graph.UnrelatedUserId)).IsTrue();
            await Assert.That(await committedContext.UserAuthenticationTokens
                .CountAsync(token => token.UserId == graph.OwnerUserId)).IsEqualTo(0);
            await Assert.That(await committedContext.UserAuthenticationTokens
                .CountAsync(token => token.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await committedContext.UserExternalLogins
                .CountAsync(login => login.UserId == graph.OwnerUserId)).IsEqualTo(0);
            await Assert.That(await committedContext.UserExternalLogins
                .CountAsync(login => login.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await committedContext.TenantUsers
                .CountAsync(membership => membership.UserId == graph.OwnerUserId)).IsEqualTo(0);
            await Assert.That(await committedContext.TenantUsers
                .CountAsync(membership => membership.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await committedContext.UserPreferences
                .CountAsync(preference => preference.UserId == graph.OwnerUserId)).IsEqualTo(0);
            await Assert.That(await committedContext.UserPreferences
                .CountAsync(preference => preference.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(ownerActorPii.DisplayName).IsEqualTo("Deleted user");
            await Assert.That(ownerIdentity.Did).IsEqualTo($"did:deleted:{ownerIdentity.Id:N}");
            await Assert.That(ownerIdentity.Handle).IsNull();
            await Assert.That(ownerIdentity.PdsHost).IsEqualTo(string.Empty);
            await Assert.That(ownerIdentity.IsDeleted).IsTrue();
            await Assert.That(ownerActorPii.ProfilePictureUri).IsNull();
            await Assert.That(await committedContext.AtprotoIdentities
                .AnyAsync(identity => identity.ActorId == graph.UnrelatedActorId
                    && identity.Did == "did:plc:unrelated"
                    && !identity.IsDeleted)).IsTrue();
            await Assert.That(checkpoint.AuthoritySequence).IsEqualTo(retained.AuthoritySequence);
            await Assert.That(checkpoint.IntentId).IsEqualTo(retained.IntentId);
            await Assert.That(messages.Count(message =>
                message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType))
                .IsEqualTo(2);
            await Assert.That(messages.Count(message =>
                message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType))
                .IsEqualTo(2);
            await Assert.That(messages.All(message => message.Id.Version == 7)).IsTrue();
            string payloads = string.Join('|', messages.Select(message => message.Payload));
            foreach (string piiCanary in graph.PiiCanaries)
            {
                await Assert.That(payloads).DoesNotContain(piiCanary);
            }

            checkpointCount = await committedContext.PrivacyErasureReplayCheckpoints.CountAsync();
            outboxCount = messages.Length;
        }

        await using (var restartedContext = fixture.CreateDbContext())
        await using (ErasureRuntime restartedRuntime = CreateRuntime(restartedContext, authority))
        {
            await restartedRuntime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        await using (var finalContext = fixture.CreateDbContext())
        {
            await Assert.That(await finalContext.PrivacyErasureReplayCheckpoints.CountAsync())
                .IsEqualTo(checkpointCount);
            await Assert.That(await finalContext.OutboxMessages.CountAsync(message =>
                message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType))
                .IsEqualTo(outboxCount);
            await Assert.That(await finalContext.UserAuthenticationTokens
                .CountAsync(token => token.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
            await Assert.That(await finalContext.UserExternalLogins
                .CountAsync(login => login.UserId == graph.UnrelatedUserId)).IsEqualTo(1);
        }

        ErasureGraph restoredGraph;
        long checkpointBeforeRestore;
        await using (var seedContext = fixture.CreateDbContext())
        {
            restoredGraph = await SeedErasureGraphAsync(seedContext, "restored");
            checkpointBeforeRestore = await seedContext.PrivacyErasureReplayCheckpoints
                .MaxAsync(checkpoint => (long?)checkpoint.AuthoritySequence)
                ?? 0;
        }

        PrivacyErasureIntent retainedAfterRestore = await authority.AppendAsync(
            new PrivacyErasureRequest(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                restoredGraph.OwnerUserId,
                PrivacyErasureReasonCode.AccountDeletion,
                1));
        await Assert.That(retainedAfterRestore.AuthoritySequence).IsEqualTo(checkpointBeforeRestore + 1);

        await using (var restoredContext = fixture.CreateDbContext())
        await using (ErasureRuntime restored = CreateRuntime(restoredContext, authority))
        {
            await restored.ReplayService.ReplayAsync(CancellationToken.None);
        }

        int checkpointCountAfterRestore;
        int outboxCountAfterRestore;
        await using (var verifiedContext = fixture.CreateDbContext())
        {
            Location[] homes = await verifiedContext.Locations
                .IgnoreQueryFilters()
                .Include(location => location.Pii)
                .Where(location => restoredGraph.LocationIds.Contains(location.Id))
                .ToArrayAsync();
            checkpointCountAfterRestore = await verifiedContext.PrivacyErasureReplayCheckpoints.CountAsync();
            outboxCountAfterRestore = await verifiedContext.OutboxMessages.CountAsync(message =>
                message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
                || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType);

            await Assert.That(homes.All(home =>
                home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased
                && home.OwnerUserId is null
                && home.Pii == null)).IsTrue();
            await Assert.That(await verifiedContext.PrivacyErasureReplayCheckpoints
                .AnyAsync(checkpoint => checkpoint.AuthoritySequence == retainedAfterRestore.AuthoritySequence
                    && checkpoint.IntentId == retainedAfterRestore.IntentId)).IsTrue();
        }

        await using (var restartedContext = fixture.CreateDbContext())
        await using (ErasureRuntime restarted = CreateRuntime(restartedContext, authority))
        {
            await restarted.ReplayService.ReplayAsync(CancellationToken.None);
        }

        await using var postRestoreContext = fixture.CreateDbContext();
        await Assert.That(await postRestoreContext.PrivacyErasureReplayCheckpoints.CountAsync())
            .IsEqualTo(checkpointCountAfterRestore);
        await Assert.That(await postRestoreContext.OutboxMessages.CountAsync(message =>
            message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
            || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType))
            .IsEqualTo(outboxCountAfterRestore);
    }

    internal static ErasureRuntime CreateRuntime(
        ExploreDbContext context,
        IPrivacyErasureAuthority authority)
    {
        var services = new ServiceCollection();
        services.AddHybridCache();
        ServiceProvider cacheProvider = services.BuildIsolatedServiceProvider();
        var userRepository = new UserRepository(context);
        var userPiiRepository = new GenericRepository<UserPii, Guid>(context);
        var tokenRepository = new UserAuthenticationTokenRepository(context);
        var erasureRepository = new UserLocationPrivacyErasureRepository(context);
        var checkpointRepository = new PrivacyErasureReplayCheckpointRepository(context);
        var outboxRepository = new OutboxRepository(context);
        var stateRepository = new PrivacyErasureStateRepository(context);
        HybridCache cache = cacheProvider.GetRequiredService<HybridCache>();
        var applier = new PrivacyErasureApplier(
            userRepository,
            userPiiRepository,
            tokenRepository,
            erasureRepository,
            erasureRepository,
            new AiConversationRepository(context),
            new PrivacyErasureProviderWorkRepository(context),
            new PrivacyErasureProviderLocatorProtector(new EphemeralDataProtectionProvider()),
            checkpointRepository,
            stateRepository,
            outboxRepository,
            cache,
            TimeProvider.System,
            NullLogger<PrivacyErasureApplier>.Instance,
            Options.Create(new PrivacyErasureOptions()));
        var service = new RetainedAuthorityPrivacyErasureWorkflow(
            checkpointRepository,
            stateRepository,
            authority,
            new EfCoreUnitOfWork(context),
            applier,
            Options.Create(new PrivacyErasureOptions()),
            TimeProvider.System);
        return new ErasureRuntime(
            service,
            new PrivacyErasureReplayService(service),
            cacheProvider);
    }

    internal static async Task<ErasureGraph> SeedErasureGraphAsync(
        ExploreDbContext context,
        string identitySuffix = "")
    {
        if (!await context.Set<AuthenticationProvider>()
            .AnyAsync(provider => provider.Id == (int)AuthenticationProviderKind.Keycloak))
        {
            context.Add(new AuthenticationProvider
            {
                Id = (int)AuthenticationProviderKind.Keycloak,
                MasterCode = "KEYCLOAK",
                FullName = "Keycloak"
            });
        }

        var tenantA = CreateTenant("workflow-a");
        var tenantB = CreateTenant("workflow-b");
        var owner = CreateUser("workflow-owner");
        var unrelated = CreateUser("workflow-unrelated");
        context.AddRange(tenantA, tenantB, owner, unrelated);
        await context.SaveChangesAsync();

        var ownerActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = owner.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii
            {
                DisplayName = "ACTOR-NAME-CANARY",
                ProfilePictureUri = "https://example.com/actor-canary.jpg",
            },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        var ownerIdentity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse($"did:plc:actor-canary{identitySuffix}"))
        {
            Id = Guid.CreateVersion7(),
            ActorId = ownerActor.Id,
            Actor = ownerActor,

            Handle = $"actor-canary{identitySuffix}.example",
            PdsHost = "https://pds.example.invalid",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow,
        };
        var tenantBActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            ActorTypeId = (int)ActorTypeEnum.Group,
            ActorType = null!,
            GroupId = Guid.CreateVersion7(),
            Pii = new ActorPii { DisplayName = "Tenant B organizer" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        var tenantBGroup = new Group
        {
            Id = tenantBActor.GroupId.Value,
            FullName = "Tenant B organizers",
            Actor = tenantBActor,
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        tenantBActor.Group = tenantBGroup;
        var unrelatedActor = new Actor
        {
            Id = Guid.CreateVersion7(),
            UserId = unrelated.Id,
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            Pii = new ActorPii { DisplayName = "Unrelated user" },
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        var unrelatedIdentity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse($"did:plc:unrelated{identitySuffix}"))
        {
            Id = Guid.CreateVersion7(),
            ActorId = unrelatedActor.Id,
            Actor = unrelatedActor,

            Handle = $"unrelated{identitySuffix}.example",
            PdsHost = "https://pds.example.invalid",
            IsActive = true,
            LastResolvedAt = DateTime.UtcNow,
        };
        context.AddRange(
            ownerActor,
            ownerIdentity,
            tenantBGroup,
            tenantBActor,
            unrelatedActor,
            unrelatedIdentity);
        await context.SaveChangesAsync();

        context.AddRange(
            CreateAuthenticationToken(tenantA, owner, $"did:plc:owner-a{identitySuffix}"),
            CreateAuthenticationToken(tenantB, owner, $"did:plc:owner-b{identitySuffix}"),
            CreateAuthenticationToken(tenantA, unrelated, $"did:plc:unrelated{identitySuffix}"),
            CreateExternalLogin(tenantA, owner, $"owner-a{identitySuffix}"),
            CreateExternalLogin(tenantB, owner, $"owner-b{identitySuffix}"),
            CreateExternalLogin(tenantA, unrelated, $"unrelated{identitySuffix}"),
            CreateTenantUser(tenantA, owner, ownerActor, TenantUserStatusEnum.Active),
            CreateTenantUser(tenantB, owner, ownerActor, TenantUserStatusEnum.Removed),
            CreateTenantUser(tenantA, unrelated, unrelatedActor, TenantUserStatusEnum.Active),
            CreatePreference(tenantA, owner.Id, "owner-a"),
            CreatePreference(tenantB, owner.Id, "owner-b"),
            CreatePreference(tenantA, unrelated.Id, "unrelated"));
        await context.SaveChangesAsync();

        Location homeA = CreatePrivateHome(tenantA.Id, owner.Id, "HOME-A-NAME-CANARY");
        Location homeB = CreatePrivateHome(tenantB.Id, owner.Id, "HOME-B-NAME-CANARY");
        homeA.SetManualAddress("HOME-A-ADDRESS-CANARY", "1000");
        homeB.SetManualAddress("HOME-B-ADDRESS-CANARY", "1000");
        context.Locations.AddRange(homeA, homeB);
        await context.SaveChangesAsync();

        var roomA = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantA.Id,
            Tenant = null!,
            LocationId = homeA.Id,
            Location = null!,
            Name = "HOME-A-ROOM-CANARY",
            Description = "HOME-A-ROOM-DESCRIPTION-CANARY",
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        var roomB = new LocationRoom
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantB.Id,
            Tenant = null!,
            LocationId = homeB.Id,
            Location = null!,
            Name = "HOME-B-ROOM-CANARY",
            Description = "HOME-B-ROOM-DESCRIPTION-CANARY",
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
        context.LocationRooms.AddRange(roomA, roomB);

        Explore.Domain.Event eventA = CreateEvent(tenantA.Id, ownerActor.Id, "Home A event");
        Explore.Domain.Event eventB = CreateEvent(tenantB.Id, tenantBActor.Id, "Home B event");
        context.Events.AddRange(eventA, eventB);
        await context.SaveChangesAsync();
        roomB.IsDeleted = true;
        roomB.DeletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync();

        EventLocation eventLocationA = EventLocation.CreatePhysical(
            tenantA.Id, eventA.Id, homeA.Id, owner.Id, DateTime.UtcNow);
        EventLocation eventLocationB = EventLocation.CreatePhysical(
            tenantB.Id, eventB.Id, homeB.Id, owner.Id, DateTime.UtcNow);
        var eventLocationRepository = new EventLocationRepository(context);
        await eventLocationRepository.AddAsync(eventLocationA, CancellationToken.None);
        await eventLocationRepository.AddAsync(eventLocationB, CancellationToken.None);

        DateTime priorAuditAtUtc = DateTime.UtcNow;
        EventLocationDisclosureAudit subjectDisclosureAudit = eventLocationA.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.VenueName,
            LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
            null,
            eventLocationA.PolicyVersion,
            owner.Id,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            priorAuditAtUtc);
        EventLocationDisclosureAudit unrelatedDisclosureAudit = eventLocationB.ChangeDisclosurePolicy(
            EventLocationDisclosureFields.City,
            LocationDisclosureAudienceEnum.ConfirmedParticipant,
            null,
            eventLocationB.PolicyVersion,
            unrelated.Id,
            EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange,
            priorAuditAtUtc);
        EventLocationExactReadAudit subjectExactReadAudit = EventLocationExactReadAudit.Create(
            tenantA.Id,
            eventLocationA.Id,
            owner.Id,
            EventLocationExactReadPurposeEnum.EventManagement,
            wasAuthorized: true,
            priorAuditAtUtc,
            Guid.CreateVersion7(),
            null);
        EventLocationExactReadAudit unrelatedExactReadAudit = EventLocationExactReadAudit.Create(
            tenantB.Id,
            eventLocationB.Id,
            unrelated.Id,
            EventLocationExactReadPurposeEnum.SupportCaseReview,
            wasAuthorized: false,
            priorAuditAtUtc,
            Guid.CreateVersion7(),
            null);
        context.EventLocationDisclosureAudits.AddRange(subjectDisclosureAudit, unrelatedDisclosureAudit);
        context.EventLocationExactReadAudits.AddRange(subjectExactReadAudit, unrelatedExactReadAudit);
        await context.SaveChangesAsync();

        return new ErasureGraph(
            owner.Id,
            ownerActor.Id,
            unrelated.Id,
            unrelatedActor.Id,
            [homeA.Id, homeB.Id],
            [roomA.Id, roomB.Id],
            [eventLocationA.Id, eventLocationB.Id],
            subjectDisclosureAudit.Id,
            unrelatedDisclosureAudit.Id,
            subjectExactReadAudit.Id,
            unrelatedExactReadAudit.Id,
            [homeA.FullName, homeB.FullName],
            [
                owner.Pii.Email,
                owner.Pii.FirstName,
                owner.Pii.LastName,
                ownerActor.Pii!.DisplayName,
                homeA.FullName,
                homeB.FullName,
                homeA.Pii!.Address,
                homeB.Pii!.Address,
                roomA.Name,
                roomB.Name,
                roomA.Description!,
                roomB.Description!,
            ]);
    }

    private static Explore.Domain.Event CreateEvent(Guid tenantId, Guid actorId, string title) => new(EventStatusEnum.Draft)
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        Tenant = null!,
        ActorId = actorId,
        Actor = null!,
        Title = title,
        EventStatus = null!,
        EventFormatId = (int)EventFormatEnum.Local,
        EventFormat = null!,
        EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
        EventProvenanceType = null!,
        VisibilityTypeId = (int)VisibilityTypeEnum.Private,
        VisibilityType = null!,
        ConcurrencyStamp = Guid.CreateVersion7(),
    };

    internal static Task InstallOutboxFailureTriggerAsync(ExploreDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION reject_location_privacy_outbox() RETURNS trigger
            LANGUAGE plpgsql AS $function$
            BEGIN
                IF NEW.event_type IN ('LocationPiiErased', 'LocationPrivacyCorrectionRequested') THEN
                    RAISE EXCEPTION 'forced location privacy outbox rollback';
                END IF;
                RETURN NEW;
            END;
            $function$;
            DROP TRIGGER IF EXISTS tr_reject_location_privacy_outbox ON islamu_event.outbox_messages;
            CREATE TRIGGER tr_reject_location_privacy_outbox
                BEFORE INSERT ON islamu_event.outbox_messages
                FOR EACH ROW EXECUTE FUNCTION reject_location_privacy_outbox();
            """);

    internal static Task RemoveOutboxFailureTriggerAsync(ExploreDbContext context) =>
        context.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS tr_reject_location_privacy_outbox ON islamu_event.outbox_messages;
            DROP FUNCTION IF EXISTS reject_location_privacy_outbox();
            """);

    private static Tenant CreateTenant(string slug)
    {
        return new Tenant
        {
            Id = Guid.CreateVersion7(),
            FullName = slug,
            Slug = $"{slug}-{Guid.NewGuid():N}",
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!,
        };
    }

    private static User CreateUser(string emailPrefix)
    {
        return new User
        {
            Id = Guid.CreateVersion7(),
            Pii = new UserPii
            {
                Email = $"{emailPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Privacy",
                LastName = "Owner",
            },
            EmailVerified = true,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private static UserAuthenticationToken CreateAuthenticationToken(
        Tenant tenant,
        User user,
        string subjectDid) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            Provider = "atproto",
            SubjectDid = subjectDid,
            SessionCiphertext = Enumerable.Repeat((byte)1, 29).ToArray(),
            EncryptionKeyId = "active-key",
            OAuthClientKeyId = "oauth-client-key",
            EnvelopeVersion = 1,
            PdsHost = "https://pds.example.invalid",
        };

    private static UserExternalLogin CreateExternalLogin(
        Tenant tenant,
        User user,
        string providerKey) => new()
        {
            Id = Guid.CreateVersion7(),
            UserId = user.Id,
            User = user,
            AuthenticationProviderId = (int)AuthenticationProviderKind.Keycloak,
            AuthenticationProvider = null!,
            ProviderKey = providerKey,
            ProviderDisplayName = "Keycloak",
            CreatedAt = DateTime.UtcNow,
        };

    private static TenantUser CreateTenantUser(
        Tenant tenant,
        User user,
        Actor actor,
        TenantUserStatusEnum status) => new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenant.Id,
            Tenant = tenant,
            UserId = user.Id,
            User = user,
            ActorId = actor.Id,
            Actor = actor,
            StatusId = (int)status,
            JoinedAt = DateTime.UtcNow,
            RemovedAt = status == TenantUserStatusEnum.Removed ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
        };

    private static UserPreference CreatePreference(Tenant tenant, Guid userId, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        UserId = userId,
        SettingKey = $"privacy-erasure:{value}",
        Value = value,
        CreatedAt = DateTime.UtcNow
    };

    private static AuditLog CreateAudit(Tenant tenant, Guid actorId, string value) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenant.Id,
        Tenant = tenant,
        EntityType = "PrivacyErasureCanary",
        EntityId = Guid.CreateVersion7().ToString(),
        Action = "Updated",
        OldValues = $"{{\"value\":\"{value}-old\"}}",
        NewValues = $"{{\"value\":\"{value}-new\"}}",
        ActorId = actorId,
        Timestamp = DateTime.UtcNow
    };

    private static Location CreatePrivateHome(Guid tenantId, Guid ownerUserId, string name)
    {
        var location = CreateLocation(tenantId, name);
        location.ClassifyAsPrivateHome(ownerUserId);
        location.SetManualAddress($"{name} address", "1000");
        return location;
    }

    private static Location CreateNonHome(Guid tenantId, string name)
    {
        var location = CreateLocation(tenantId, name);
        location.ClassifyAs(LocationKindEnum.CommercialVenue);
        return location;
    }

    private static Location CreateLocation(Guid tenantId, string name)
    {
        return new Location
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            Tenant = null!,
            FullName = name,
            Country = "BE",
            City = "Brussels",
            ConcurrencyStamp = Guid.CreateVersion7(),
        };
    }

    internal sealed record ErasureRuntime(
        RetainedAuthorityPrivacyErasureWorkflow Service,
        PrivacyErasureReplayService ReplayService,
        ServiceProvider CacheProvider) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => CacheProvider.DisposeAsync();
    }

    internal sealed record ErasureGraph(
        Guid OwnerUserId,
        Guid OwnerActorId,
        Guid UnrelatedUserId,
        Guid UnrelatedActorId,
        Guid[] LocationIds,
        Guid[] RoomIds,
        Guid[] EventLocationIds,
        Guid SubjectDisclosureAuditId,
        Guid UnrelatedDisclosureAuditId,
        Guid SubjectExactReadAuditId,
        Guid UnrelatedExactReadAuditId,
        string[] HomeNames,
        string[] PiiCanaries);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
[Category("EventLocationPrivacy")]
[ClassDataSource<ExternalDatabasePrivacyErasurePostgreSqlFixture>(Shared = SharedType.None)]
[NotInParallel("PersistenceDb")]
public sealed class ExternalDatabasePrivacyErasureAuthorityTests(
    ExternalDatabasePrivacyErasurePostgreSqlFixture fixture)
{
    [Test]
    [Timeout(240_000)]
    public async Task ProvisioningAllowsAdministratorButRejectsExplicitRuntimeMembership()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityAdminDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql);
            await context.Database.ExecuteSqlRawAsync(
                "GRANT privacy_erasure_authority_runtime TO CURRENT_USER");

            PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql));

            await Assert.That(exception!.SqlState).IsEqualTo("P0001");
        }
        finally
        {
            await transaction.RollbackAsync();
            await context.Database.CloseConnectionAsync();
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task ProvisioningRejectsFixedRoleMembershipDriftBeforeLifecycleInstallation()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityAdminDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            await context.Database.ExecuteSqlRawAsync(
                "GRANT privacy_erasure_authority_migrator TO privacy_erasure_authority_runtime");
            await context.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql);

            PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(() =>
                context.Database.ExecuteSqlRawAsync(
                    PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql));

            await Assert.That(exception!.SqlState).IsEqualTo("P0001");
        }
        finally
        {
            await transaction.RollbackAsync();
            await context.Database.CloseConnectionAsync();
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task MigratorMaintenance_IsAtomicAndHoldAware()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityAdminDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            DateTime databaseNow = await PrepareRetentionScenarioAsync(context);
            var repository = new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions()));
            IPrivacyErasureAuthorityMaintenance maintenance = repository;
            PrivacyErasureIntent[] facts = new PrivacyErasureIntent[3];
            for (var index = 0; index < facts.Length; index++)
            {
                facts[index] = await repository.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    Guid.CreateVersion7(),
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
            }
            await context.Database.ExecuteSqlRawAsync(
                "SELECT set_config('privacy_erasure_authority.maintenance', 'on', true); "
                + "UPDATE privacy_erasure_authority.erasure_intents "
                + "SET requested_at_utc = {0}, recorded_at_utc = {0}, retention_expires_at_utc = {1} "
                + "WHERE authority_sequence >= {2} AND authority_sequence <= {3};",
                databaseNow.AddDays(-2),
                databaseNow.AddDays(-1),
                facts[0].AuthoritySequence,
                facts[^1].AuthoritySequence);

            var request = new PrivacyErasureRetentionRequest(
                databaseNow,
                100,
                [facts[1].AuthoritySequence]);
            PrivacyErasureRetentionEvaluation dryRun =
                await maintenance.EvaluateRetentionAsync(request);
            PrivacyErasureCompactionResult compacted =
                await maintenance.CompactExpiredIntentsAsync(request);

            await Assert.That(dryRun.EligibleCount).IsEqualTo(1);
            await Assert.That(dryRun.HeldCount).IsEqualTo(1);
            await Assert.That(compacted.DeletedCount).IsEqualTo(1);
            await Assert.That(compacted.PseudonymizedCount).IsEqualTo(1);
            await Assert.That(compacted.State.RetainedFloorSequence)
                .IsEqualTo(facts[1].AuthoritySequence);
            IReadOnlyList<PrivacyErasureIntent> replayable = await repository.ReadAfterAsync(
                compacted.State.RetainedFloorSequence,
                100);
            await Assert.That(replayable.Select(fact => fact.AuthoritySequence))
                .IsEquivalentTo([facts[2].AuthoritySequence]);
        }
        finally
        {
            await transaction.RollbackAsync();
            await context.Database.CloseConnectionAsync();
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task MigratorMaintenance_TailGapRollsBackWithoutAdvancingFloor()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityAdminDbContext();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            DateTime databaseNow = await PrepareRetentionScenarioAsync(context);
            var repository = new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions()));
            PrivacyErasureIntent[] facts = new PrivacyErasureIntent[3];
            for (var index = 0; index < 3; index++)
            {
                facts[index] = await repository.AppendAsync(new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    Guid.CreateVersion7(),
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
            }
            await context.Database.ExecuteSqlRawAsync(
                "SELECT set_config('privacy_erasure_authority.maintenance', 'on', true); "
                + "UPDATE privacy_erasure_authority.erasure_intents "
                + "SET requested_at_utc = {0}, recorded_at_utc = {0}, retention_expires_at_utc = {1}; "
                + "DELETE FROM privacy_erasure_authority.erasure_intents WHERE authority_sequence = {2};",
                databaseNow.AddDays(-2),
                databaseNow.AddDays(-1),
                facts[^1].AuthoritySequence);
            var request = new PrivacyErasureRetentionRequest(databaseNow, 100, []);

            await transaction.CreateSavepointAsync("before_evaluation");
            await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(
                () => repository.EvaluateRetentionAsync(request));
            await transaction.RollbackToSavepointAsync("before_evaluation");
            await transaction.CreateSavepointAsync("before_compaction");
            await Assert.ThrowsAsync<Explore.Application.Exceptions.PrivacyErasureSequenceGapException>(
                () => repository.CompactExpiredIntentsAsync(request));
            await transaction.RollbackToSavepointAsync("before_compaction");
            await Assert.That(await repository.GetStateAsync())
                .IsEqualTo(new PrivacyErasureAuthorityState(facts[^1].AuthoritySequence, 0));
            IReadOnlyList<PrivacyErasureIntent> retained = await repository.ReadAfterAsync(0, 100);
            await Assert.That(retained.Select(fact => fact.AuthoritySequence))
                .IsEquivalentTo(facts[..^1].Select(fact => fact.AuthoritySequence));
        }
        finally
        {
            await transaction.RollbackAsync();
            await context.Database.CloseConnectionAsync();
        }
    }

    private static async Task<DateTime> PrepareRetentionScenarioAsync(
        PrivacyErasureAuthorityDbContext context)
    {
        // The caller's rollback restores other tests' authority facts and sequence state.
        await context.Database.ExecuteSqlRawAsync(
            "SELECT set_config('privacy_erasure_authority.maintenance', 'on', true); "
            + "DELETE FROM privacy_erasure_authority.erasure_intents; "
            + "UPDATE privacy_erasure_authority.authority_counter "
            + "SET last_sequence = 0, retained_floor_sequence = 0 WHERE singleton;");
        return await context.Database.SqlQueryRaw<DateTime>(
            "SELECT statement_timestamp() AS \"Value\"").SingleAsync();
    }

    [Test]
    [Timeout(240_000)]
    public async Task RuntimeRole_CannotCompactOrDeleteAuthorityRows()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        var repository = new EfCorePrivacyErasureAuthorityRepository(
            context,
            Options.Create(new PrivacyErasureOptions()));

        PostgresException? compactDenied = await Assert.ThrowsAsync<PostgresException>(() =>
            repository.CompactExpiredIntentsAsync(
                new PrivacyErasureRetentionRequest(DateTime.UtcNow, 100, [])));
        await Assert.That(compactDenied!.SqlState)
            .IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);

        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var directDelete = new NpgsqlCommand(
                "DELETE FROM privacy_erasure_authority.erasure_intents",
                connection);
            PostgresException? deleteDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                directDelete.ExecuteNonQueryAsync());
            await Assert.That(deleteDenied!.SqlState)
                .IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task RuntimeRole_AppendsAndReadsThroughFunctionsButCannotAccessAuthorityTables()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        var firstOptions = new PrivacyErasureOptions
        {
            MaximumBackupHorizon = TimeSpan.FromDays(14),
            AuthorityRetentionSafetyMargin = TimeSpan.FromHours(12),
        };
        var repository = new EfCorePrivacyErasureAuthorityRepository(
            context,
            Options.Create(firstOptions));
        var request = new PrivacyErasureRequest(
            Guid.CreateVersion7(),
            PrivacyErasureSubjectKind.User,
            Guid.CreateVersion7(),
            PrivacyErasureReasonCode.AccountDeletion,
            1);

        PrivacyErasureIntent first = await repository.AppendAsync(request);
        PrivacyErasureIntent duplicate = await new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions
                {
                    MaximumBackupHorizon = TimeSpan.FromDays(30),
                    AuthorityRetentionSafetyMargin = TimeSpan.Zero,
                }))
            .AppendAsync(request);
        IReadOnlyList<PrivacyErasureIntent> facts =
            await repository.ReadAfterAsync(first.AuthoritySequence - 1, 1);

        await Assert.That(duplicate.AuthoritySequence).IsEqualTo(first.AuthoritySequence);
        await Assert.That(first.RetentionExpiresAtUtc - first.RecordedAtUtc)
            .IsEqualTo(firstOptions.AuthorityRetention);
        await Assert.That(first.RetentionExpiresAtUtc).IsNotEqualTo(DateTime.MaxValue);
        await Assert.That(duplicate.RetentionExpiresAtUtc).IsEqualTo(first.RetentionExpiresAtUtc);
        await Assert.That(facts.Count).IsEqualTo(1);
        await Assert.That(facts.Single().IntentId).IsEqualTo(first.IntentId);

        await context.Database.OpenConnectionAsync();
        try
        {
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            string[] blockedStatements =
            [
                "SELECT count(*) FROM privacy_erasure_authority.erasure_intents",
                "INSERT INTO privacy_erasure_authority.authority_counter (singleton, last_sequence) VALUES (true, 0)",
                "UPDATE privacy_erasure_authority.erasure_intents SET policy_version = policy_version",
                "DELETE FROM privacy_erasure_authority.erasure_intents",
                "TRUNCATE TABLE privacy_erasure_authority.erasure_intents",
                "SELECT * FROM privacy_erasure_authority.append_erasure_intent(NULL::uuid, 1::smallint, NULL::uuid, 1::smallint, 1)"
            ];
            foreach (string statement in blockedStatements)
            {
                await using var command = new NpgsqlCommand(statement, connection);
                PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(() =>
                    command.ExecuteNonQueryAsync());
                await Assert.That(exception!.SqlState)
                    .IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }

        var options = new PrivacyErasureDurabilityOptions
        {
            Topology = PrivacyErasureAuthorityTopology.ExternalDatabase,
        };
        await Assert.That(options.RestoreReplayProtection).IsTrue();
    }

    [Test]
    [Timeout(240_000)]
    public async Task RuntimeRole_FiniteAppendRejectsNonFutureRetentionInterval()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        await context.Database.OpenConnectionAsync();
        try
        {
            var request = new PrivacyErasureRequest(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1);
            var connection = (NpgsqlConnection)context.Database.GetDbConnection();
            await using var command = new NpgsqlCommand(
                $"SELECT * FROM {PrivacyErasureAuthorityDatabaseContract.AppendFunctionSql}(@intent_id, @subject_kind, @subject_id, @reason_code, @policy_version, @authority_retention)",
                connection);
            command.Parameters.AddWithValue("intent_id", NpgsqlTypes.NpgsqlDbType.Uuid, request.IntentId);
            command.Parameters.AddWithValue("subject_kind", NpgsqlTypes.NpgsqlDbType.Smallint, (short)request.SubjectKind);
            command.Parameters.AddWithValue("subject_id", NpgsqlTypes.NpgsqlDbType.Uuid, request.SubjectId);
            command.Parameters.AddWithValue("reason_code", NpgsqlTypes.NpgsqlDbType.Smallint, (short)request.ReasonCode);
            command.Parameters.AddWithValue("policy_version", NpgsqlTypes.NpgsqlDbType.Integer, request.PolicyVersion);
            command.Parameters.AddWithValue("authority_retention", NpgsqlTypes.NpgsqlDbType.Interval, TimeSpan.Zero);

            PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(() =>
                command.ExecuteNonQueryAsync());
            await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.InvalidParameterValue);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    [Test]
    [Timeout(240_000)]
    public async Task ConcurrentAppends_UseFreshContextsAndAllocateOneContiguousRange()
    {
        PrivacyErasureIntent[] before = await ReadAuthorityFactsAsync();
        long previousWatermark = before.LastOrDefault()?.AuthoritySequence ?? 0;
        PrivacyErasureRequest[] requests = Enumerable.Range(0, 8)
            .Select(_ => new PrivacyErasureRequest(
                Guid.CreateVersion7(),
                PrivacyErasureSubjectKind.User,
                Guid.CreateVersion7(),
                PrivacyErasureReasonCode.AccountDeletion,
                1))
            .ToArray();

        PrivacyErasureIntent[] appended = await Task.WhenAll(requests.Select(async request =>
        {
            await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
            return await new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions())).AppendAsync(request);
        }));

        await Assert.That(appended.Select(fact => fact.AuthoritySequence))
            .IsEquivalentTo(Enumerable.Range(1, requests.Length)
                .Select(offset => previousWatermark + offset));
        await Assert.That(appended.Select(fact => fact.AuthoritySequence).Distinct().Count())
            .IsEqualTo(requests.Length);

        PrivacyErasureIntent[] after = await ReadAuthorityFactsAsync();
        await Assert.That(after.Select(fact => fact.AuthoritySequence))
            .IsEquivalentTo(Enumerable.Range(1, after.Length).Select(value => (long)value));
    }

    private async Task<PrivacyErasureIntent[]> ReadAuthorityFactsAsync()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        IReadOnlyList<PrivacyErasureIntent> facts =
            await new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions())).ReadAfterAsync(0, 500);
        return facts.ToArray();
    }
}

[Category("EventLocationPrivacy")]
[ClassDataSource<ExternalDatabasePrivacyErasurePostgreSqlFixture>(Shared = SharedType.PerClass)]
[NotInParallel("PersistenceDb")]
public sealed class ExternalDatabasePrivacyErasureRestoreTests(
    ExternalDatabasePrivacyErasurePostgreSqlFixture fixture)
{
    [Test]
    [Timeout(300_000)]
    public async Task PreErasureApplicationBackup_RestoresAndReplaysFromUntouchedAuthorityExactlyOnce()
    {
        GlobalLocationPrivacyErasureTests.ErasureGraph graph;
        await using (var seedContext = fixture.CreateDbContext())
        {
            graph = await GlobalLocationPrivacyErasureTests.SeedErasureGraphAsync(seedContext);
        }

        var backup = await fixture.CaptureApplicationBackupAsync();
        PrivacyErasureIntent retained;
        await using (PrivacyErasureAuthorityDbContext authorityContext = fixture.CreateAuthorityDbContext())
        {
            retained = await new EfCorePrivacyErasureAuthorityRepository(
                    authorityContext,
                    Options.Create(new PrivacyErasureOptions())).AppendAsync(
                new PrivacyErasureRequest(
                    Guid.CreateVersion7(),
                    PrivacyErasureSubjectKind.User,
                    graph.OwnerUserId,
                    PrivacyErasureReasonCode.AccountDeletion,
                    1));
        }

        AuthorityFactSnapshot[] authorityBeforeRestore = await ReadAuthoritySnapshotAsync();
        await using (var applicationContext = fixture.CreateDbContext())
        await using (PrivacyErasureAuthorityDbContext replayAuthorityContext =
            fixture.CreateAuthorityDbContext())
        await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
            GlobalLocationPrivacyErasureTests.CreateRuntime(
                applicationContext,
                new EfCorePrivacyErasureAuthorityRepository(
                    replayAuthorityContext,
                    Options.Create(new PrivacyErasureOptions()))))
        {
            await runtime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        await using (var erasedContext = fixture.CreateDbContext())
        {
            await AssertErasedAsync(erasedContext, graph);
        }

        string restoredDatabase = await fixture.RestoreApplicationBackupAsync(backup);
        await using (var restoredContext = fixture.CreateDbContext(restoredDatabase))
        {
            await Assert.That(await restoredContext.UserPii
                .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsTrue();
            await Assert.That(await restoredContext.Locations
                .IgnoreQueryFilters()
                .Where(location => graph.LocationIds.Contains(location.Id))
                .Select(location => location.FullName)
                .ToArrayAsync()).IsEquivalentTo(graph.HomeNames);
        }

        int checkpointBeforeReplay;
        int outboxBeforeReplay;
        await using (var beforeReplayContext = fixture.CreateDbContext(restoredDatabase))
        {
            checkpointBeforeReplay = await beforeReplayContext.PrivacyErasureReplayCheckpoints.CountAsync();
            outboxBeforeReplay = await CountPrivacyOutboxAsync(beforeReplayContext);
        }

        await using (var replayContext = fixture.CreateDbContext(restoredDatabase))
        await using (PrivacyErasureAuthorityDbContext replayAuthorityContext =
            fixture.CreateAuthorityDbContext())
        await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
            GlobalLocationPrivacyErasureTests.CreateRuntime(
                replayContext,
                new EfCorePrivacyErasureAuthorityRepository(
                    replayAuthorityContext,
                    Options.Create(new PrivacyErasureOptions()))))
        {
            await runtime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        int checkpointAfterReplay;
        int outboxAfterReplay;
        await using (var verifiedContext = fixture.CreateDbContext(restoredDatabase))
        {
            await AssertErasedAsync(verifiedContext, graph);
            checkpointAfterReplay = await verifiedContext.PrivacyErasureReplayCheckpoints.CountAsync();
            outboxAfterReplay = await CountPrivacyOutboxAsync(verifiedContext);
            await Assert.That(checkpointAfterReplay).IsEqualTo(checkpointBeforeReplay + 1);
            await Assert.That(outboxAfterReplay).IsEqualTo(outboxBeforeReplay + 4);
            PrivacyErasureReplayCheckpoint checkpoint = await verifiedContext
                .PrivacyErasureReplayCheckpoints.SingleAsync();
            await Assert.That(checkpoint.AuthoritySequence).IsEqualTo(retained.AuthoritySequence);
            await Assert.That(checkpoint.IntentId).IsEqualTo(retained.IntentId);
        }

        await using (var repeatedContext = fixture.CreateDbContext(restoredDatabase))
        await using (PrivacyErasureAuthorityDbContext repeatedAuthorityContext =
            fixture.CreateAuthorityDbContext())
        await using (GlobalLocationPrivacyErasureTests.ErasureRuntime runtime =
            GlobalLocationPrivacyErasureTests.CreateRuntime(
                repeatedContext,
                new EfCorePrivacyErasureAuthorityRepository(
                    repeatedAuthorityContext,
                    Options.Create(new PrivacyErasureOptions()))))
        {
            await runtime.ReplayService.ReplayAsync(CancellationToken.None);
        }

        await using (var finalContext = fixture.CreateDbContext(restoredDatabase))
        {
            await AssertErasedAsync(finalContext, graph);
            await Assert.That(await finalContext.PrivacyErasureReplayCheckpoints.CountAsync())
                .IsEqualTo(checkpointAfterReplay);
            await Assert.That(await CountPrivacyOutboxAsync(finalContext)).IsEqualTo(outboxAfterReplay);
        }

        AuthorityFactSnapshot[] authorityAfterRestore = await ReadAuthoritySnapshotAsync();
        await Assert.That(authorityAfterRestore).IsEquivalentTo(authorityBeforeRestore);
        await Assert.That(authorityAfterRestore.Single().AuthoritySequence)
            .IsEqualTo(retained.AuthoritySequence);

        var options = new PrivacyErasureDurabilityOptions
        {
            Topology = PrivacyErasureAuthorityTopology.ExternalDatabase,
        };
        await Assert.That(options.RestoreReplayProtection).IsTrue();
    }

    private async Task<AuthorityFactSnapshot[]> ReadAuthoritySnapshotAsync()
    {
        await using PrivacyErasureAuthorityDbContext context = fixture.CreateAuthorityDbContext();
        IReadOnlyList<PrivacyErasureIntent> facts =
            await new EfCorePrivacyErasureAuthorityRepository(
                context,
                Options.Create(new PrivacyErasureOptions())).ReadAfterAsync(0, 500);
        return facts.Select(fact => new AuthorityFactSnapshot(
            fact.AuthoritySequence,
            fact.IntentId,
            fact.SubjectKind,
            fact.SubjectId,
            fact.ReasonCode,
            fact.PolicyVersion,
            fact.RequestedAtUtc,
            fact.RecordedAtUtc,
            fact.RetentionExpiresAtUtc)).ToArray();
    }

    private static async Task AssertErasedAsync(
        ExploreDbContext context,
        GlobalLocationPrivacyErasureTests.ErasureGraph graph)
    {
        await Assert.That(await context.UserPii
            .AnyAsync(pii => pii.UserId == graph.OwnerUserId)).IsFalse();
        Location[] homes = await context.Locations
            .IgnoreQueryFilters()
            .Include(location => location.Pii)
            .Where(location => graph.LocationIds.Contains(location.Id))
            .ToArrayAsync();
        await Assert.That(homes.All(home =>
            home.OwnerUserId is null
            && home.Pii is null
            && home.LocationPrivacyStateId == (int)LocationPrivacyStateEnum.Erased)).IsTrue();
    }

    private static Task<int> CountPrivacyOutboxAsync(ExploreDbContext context) =>
        context.OutboxMessages.CountAsync(message =>
            message.EventType == LocationPrivacyOutboxMessageFactory.LocationPiiErasedEventType
            || message.EventType == LocationPrivacyOutboxMessageFactory.LocationPrivacyCorrectionRequestedEventType);

    private sealed record AuthorityFactSnapshot(
        long AuthoritySequence,
        Guid IntentId,
        PrivacyErasureSubjectKind SubjectKind,
        Guid SubjectId,
        PrivacyErasureReasonCode ReasonCode,
        int PolicyVersion,
        DateTime RequestedAtUtc,
        DateTime RecordedAtUtc,
        DateTime RetentionExpiresAtUtc);
}

public sealed class ExternalDatabasePrivacyErasurePostgreSqlFixture : IAsyncInitializer, IAsyncDisposable
{
    private const string ApplicationDatabaseName = "global_location_privacy_test";
    private const string AuthorityRuntimeUsername = "global_erasure_runtime";
    private const string AuthorityRuntimePassword = "global-erasure-runtime-password";
    private readonly Guid _fixtureId = Guid.NewGuid();
    private readonly HashSet<string> _restoredDatabases = new(StringComparer.Ordinal);
    private readonly PostgreSqlContainer _applicationContainer = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase(ApplicationDatabaseName)
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private readonly PostgreSqlContainer _authorityContainer = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("privacy_erasure_authority_test")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();
    private string _authorityRuntimeConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_applicationContainer.StartAsync(), _authorityContainer.StartAsync());
        await using (PrivacyErasureAuthorityDbContext authorityContext = CreateAuthorityAdminDbContext())
        {
            await authorityContext.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleProvisioningSql);
            await authorityContext.Database.ExecuteSqlRawAsync(
                PrivacyErasureAuthorityDatabaseContract.RoleIsolationSql);
            await authorityContext.Database.MigrateAsync();
            await ExploreDatabaseMigrator.ApplyExternalPrivacyErasureAuthorityContractAsync(
                authorityContext);
        }
        await using (var connection = new NpgsqlConnection(_authorityContainer.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var roleCommand = connection.CreateCommand();
            roleCommand.CommandText =
                $"""
                CREATE ROLE {AuthorityRuntimeUsername} LOGIN PASSWORD '{AuthorityRuntimePassword}';
                GRANT privacy_erasure_authority_runtime TO {AuthorityRuntimeUsername};
                """;
            await roleCommand.ExecuteNonQueryAsync();
        }
        _authorityRuntimeConnectionString = new NpgsqlConnectionStringBuilder(
            _authorityContainer.GetConnectionString())
        {
            Username = AuthorityRuntimeUsername,
            Password = AuthorityRuntimePassword,
        }.ConnectionString;

        await using var context = CreateDbContext();
        await context.Database.MigrateAsync();
        context.Set<TenantStatus>().Add(new TenantStatus
        {
            Id = (int)TenantStatusEnum.Active,
            MasterCode = "ACTIVE",
            FullName = "Active",
            IsActiveState = true,
        });
        context.Set<ActorType>().AddRange(
            new ActorType
            {
                Id = (int)ActorTypeEnum.User,
                MasterCode = "USER",
                FullName = "User",
            },
            new ActorType
            {
                Id = (int)ActorTypeEnum.Group,
                MasterCode = "GROUP",
                FullName = "Group",
            });
        context.Set<EventStatus>().Add(new EventStatus
        {
            Id = (int)EventStatusEnum.Draft,
            MasterCode = "DRAFT",
            FullName = "Draft",
        });
        context.Set<EventFormat>().Add(new EventFormat
        {
            Id = (int)EventFormatEnum.Local,
            MasterCode = "LOCAL",
            FullName = "Local",
        });
        context.Set<VisibilityType>().Add(new VisibilityType
        {
            Id = (int)VisibilityTypeEnum.Private,
            MasterCode = "PRIVATE",
            FullName = "Private",
        });
        await context.SaveChangesAsync();
        await LookupTableSeeder.SeedAuthenticationProvidersAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedLocationPrivacyLookupsAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedLocationAddressGovernanceLookupsAsync(context, CancellationToken.None);
        await LookupTableSeeder.SeedEventAuthorityLookupsAsync(context, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await _applicationContainer.StopAsync();
        await _applicationContainer.DisposeAsync();
        await _authorityContainer.StopAsync();
        await _authorityContainer.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public PrivacyErasureAuthorityDbContext CreateAuthorityDbContext()
    {
        var options = TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql(_authorityRuntimeConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PrivacyErasureAuthorityDbContext(options);
    }

    public PrivacyErasureAuthorityDbContext CreateAuthorityAdminDbContext()
    {
        var options = TestDbContextOptions.Create<PrivacyErasureAuthorityDbContext>()
            .UseNpgsql(_authorityContainer.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PrivacyErasureAuthorityDbContext(options);
    }

    public ExploreDbContext CreateDbContext()
        => CreateDbContext(ApplicationDatabaseName, enableRetryOnFailure: false);

    public ExploreDbContext CreateDbContext(string restoredDatabase)
    {
        if (!_restoredDatabases.Contains(restoredDatabase))
        {
            throw new InvalidOperationException("The requested restored database is not owned by this fixture.");
        }

        return CreateDbContext(restoredDatabase, enableRetryOnFailure: false);
    }

    public ExploreDbContext CreateRetryingDbContext()
        => CreateDbContext(ApplicationDatabaseName, enableRetryOnFailure: true);

    internal async Task<ApplicationBackup> CaptureApplicationBackupAsync(
        CancellationToken cancellationToken = default)
    {
        string archivePath = $"/tmp/orea-{_fixtureId:N}.dump";
        ExecResult dump = await _applicationContainer.ExecAsync(
            [
                "pg_dump",
                "--format=custom",
                "--no-owner",
                "--no-privileges",
                $"--file={archivePath}",
                "--username=postgres",
                $"--dbname={ApplicationDatabaseName}",
            ],
            cancellationToken);
        EnsureSuccessful(dump, "capture the application backup");

        ExecResult inspect = await _applicationContainer.ExecAsync(
            ["pg_restore", "--list", archivePath],
            cancellationToken);
        EnsureSuccessful(inspect, "inspect the application backup");
        if (string.IsNullOrWhiteSpace(inspect.Stdout))
        {
            throw new InvalidOperationException("The application backup archive is empty.");
        }

        return new ApplicationBackup(_fixtureId, archivePath);
    }

    internal async Task<string> RestoreApplicationBackupAsync(
        ApplicationBackup backup,
        CancellationToken cancellationToken = default)
    {
        if (backup.FixtureId != _fixtureId)
        {
            throw new InvalidOperationException("The application backup is not owned by this fixture.");
        }

        string restoredDatabase = $"orea_restore_{Guid.NewGuid():N}";
        var adminConnectionString = new NpgsqlConnectionStringBuilder(
            _applicationContainer.GetConnectionString())
        {
            Database = "postgres",
        }.ConnectionString;
        await using (var connection = new NpgsqlConnection(adminConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"CREATE DATABASE {new NpgsqlCommandBuilder().QuoteIdentifier(restoredDatabase)} TEMPLATE template0";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        ExecResult restore = await _applicationContainer.ExecAsync(
            [
                "pg_restore",
                "--exit-on-error",
                "--no-owner",
                "--no-privileges",
                $"--dbname={restoredDatabase}",
                "--username=postgres",
                backup.ArchivePath,
            ],
            cancellationToken);
        EnsureSuccessful(restore, "restore the application backup");
        _restoredDatabases.Add(restoredDatabase);
        return restoredDatabase;
    }

    private ExploreDbContext CreateDbContext(string database, bool enableRetryOnFailure)
    {
        var connectionString = new NpgsqlConnectionStringBuilder(
            _applicationContainer.GetConnectionString())
        {
            Database = database,
        }.ConnectionString;
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql(connectionString, npgsql =>
            {
                if (enableRetryOnFailure)
                {
                    npgsql.EnableRetryOnFailure();
                }
            })
            .UseSnakeCaseNamingConvention()
            .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        var context = new ExploreDbContext(options);
        context.EnableTenantFilterBypass("Global location privacy test seed context.");
        return context;
    }

    private static void EnsureSuccessful(ExecResult result, string operation)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to {operation}. ExitCode={result.ExitCode}.");
        }
    }

    public ExploreDbContext CreateTenantFilteredDbContext(ITenantContext? tenantContext = null)
    {
        var context = CreateDbContext();
        context.ClearTenantFilterBypass();
        context.TenantContext = tenantContext;
        return context;
    }

    internal sealed record ApplicationBackup(Guid FixtureId, string ArchivePath);

}
