using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Seed;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

public sealed partial class RegistrationProviderFoundationPersistenceTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task PublicationPersistsFinalEvidenceWithoutChangingBindingIdentity(bool saveDraftFirst)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        Guid bindingId = graph.Binding.Id;
        if (saveDraftFirst)
        {
            await context.SaveChangesAsync();
            await Assert.That(graph.Binding.Publication).IsNull();
        }

        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        Guid publicationId = graph.Binding.Publication!.Id;
        context.RegistrationAttempts.Add(graph.ProviderAttempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == bindingId);

        await Assert.That(reloaded.Id).IsEqualTo(bindingId);
        await Assert.That(publicationId).IsNotEqualTo(Guid.Empty);
        await Assert.That(reloaded.StateId).IsEqualTo((int)RegistrationProviderBindingStateEnum.Published);
        await Assert.That(reloaded.PublishedMappingRevisionHash!.Value).IsEqualTo(Hash().Value);
        await Assert.That(reloaded.PublishedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(reloaded.Publication).IsNotNull();
        await Assert.That(reloaded.Publication!.Id).IsEqualTo(publicationId);
        await Assert.That(reloaded.Publication.TenantId).IsEqualTo(graph.Binding.TenantId);
        await Assert.That(reloaded.Publication.RegistrationProviderBindingId).IsEqualTo(bindingId);
        await Assert.That(reloaded.Publication.MappingRevisionHashKey).IsEqualTo(Hash().Value);
        await Assert.That(reloaded.Publication.PublishedAt).IsEqualTo(Now.AddMinutes(1));
        var attempt = await context.RegistrationAttempts.SingleAsync(value => value.Id == graph.ProviderAttempt.Id);
        await Assert.That(attempt.RegistrationProviderBindingId).IsEqualTo(bindingId);
        await Assert.That(attempt.ProviderMappingRevisionHash!.Value).IsEqualTo(reloaded.Publication.MappingRevisionHashKey);
    }

    [Test]
    [Arguments("TenantId")]
    [Arguments("RegistrationProviderBindingId")]
    [Arguments("MappingRevisionHashKey")]
    [Arguments("PublishedAt")]
    public async Task SavedPublicationRejectsChangesToFinalEvidence(string propertyName)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        var publication = graph.Binding.Publication!;
        object replacement = propertyName switch
        {
            "TenantId" or "RegistrationProviderBindingId" => Guid.CreateVersion7(),
            "MappingRevisionHashKey" => OtherPublicationHash().Value,
            _ => Now.AddMinutes(2)
        };

        await Assert.That(async () =>
        {
            context.Entry(publication).Property(propertyName).CurrentValue = replacement;
            await context.SaveChangesAsync();
        }).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();

        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == graph.Binding.Id);
        await Assert.That(reloaded.Publication!.TenantId).IsEqualTo(graph.Binding.TenantId);
        await Assert.That(reloaded.Publication.RegistrationProviderBindingId).IsEqualTo(graph.Binding.Id);
        await Assert.That(reloaded.Publication.MappingRevisionHashKey).IsEqualTo(Hash().Value);
        await Assert.That(reloaded.Publication.PublishedAt).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task SavedPublicationRejectsDeletionAndRetainsEvidence()
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        Guid publicationId = graph.Binding.Publication!.Id;

        await Assert.That(async () =>
        {
            context.Remove(graph.Binding.Publication!);
            await context.SaveChangesAsync();
        }).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();

        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == graph.Binding.Id);
        await Assert.That(reloaded.Publication!.Id).IsEqualTo(publicationId);
        await Assert.That(reloaded.Publication.MappingRevisionHashKey).IsEqualTo(Hash().Value);
    }

    [Test]
    public async Task SecondPublicationWithoutPublishingOwnerIsRejectedBeforePersistence()
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        RegistrationProviderBinding donor = Binding(graph.Binding.TenantId, graph.Binding.RegistrationProviderConnectionId);
        donor.Publish(OtherPublicationHash(), Now.AddMinutes(2));
        var duplicate = donor.Publication!;
        context.ChangeTracker.Clear();
        context.Add(duplicate);
        context.Entry(duplicate).Property(nameof(duplicate.RegistrationProviderBindingId)).CurrentValue = graph.Binding.Id;

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();

        await Assert.That(await CountPublicationRowsAsync(context, duplicate)).IsEqualTo(1);
        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == graph.Binding.Id);
        await Assert.That(reloaded.Publication!.MappingRevisionHashKey).IsEqualTo(Hash().Value);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AddedPublicationWithoutPublishingTransitionLeavesDraftAndNoEvidence(bool keepUnchangedOwner)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        await context.SaveChangesAsync();
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        var publication = graph.Binding.Publication!;
        if (keepUnchangedOwner)
        {
            context.Entry(graph.Binding).State = EntityState.Unchanged;
        }
        else
        {
            context.ChangeTracker.Clear();
        }
        context.Add(publication);
        await Assert.That(context.Entry(publication).State).IsEqualTo(EntityState.Added);
        if (keepUnchangedOwner)
        {
            await Assert.That(context.Entry(graph.Binding).State).IsEqualTo(EntityState.Unchanged);
        }

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();

        await Assert.That(await CountPublicationRowsAsync(context, publication)).IsEqualTo(0);
        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == graph.Binding.Id);
        await Assert.That(reloaded.StateId).IsEqualTo((int)RegistrationProviderBindingStateEnum.Draft);
        await Assert.That(reloaded.Publication).IsNull();
        await Assert.That(reloaded.PublishedMappingRevisionHash).IsNull();
        await Assert.That(reloaded.PublishedAt).IsNull();
    }

    [Test]
    [Arguments("wrong_hash")]
    [Arguments("wrong_tenant")]
    [Arguments("native_channel_with_provider_pair")]
    public async Task AttemptRejectsProviderEvidenceOutsideExactPublishedLineage(string mismatch)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        Guid bindingId = graph.Binding.Id;
        if (mismatch == "wrong_tenant")
        {
            PublicationGraph foreign = await StagePublicationGraphAsync(context, initializeDatabase: false);
            foreign.Binding.Publish(Hash(), Now.AddMinutes(1));
            await context.SaveChangesAsync();
            bindingId = foreign.Binding.Id;
        }

        context.TenantContext = new TestTenantContext(graph.Binding.TenantId);
        RegistrationAttempt lineage = mismatch == "native_channel_with_provider_pair" ? graph.NativeAttempt : graph.ProviderAttempt;
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            lineage.TenantId, lineage.EventId, lineage.RegistrationOrderId, lineage.RegistrationWorkflowId,
            lineage.RegistrationRequirementId, lineage.RegistrationChannelId, lineage.RegistrationFormId,
            lineage.RegistrationFormVersionId, lineage.CapabilityTokenHash, bindingId,
            mismatch == "wrong_hash" ? OtherPublicationHash() : Hash(), Now.AddMinutes(2), Now.AddHours(1));
        context.RegistrationAttempts.Add(attempt);

        if (mismatch == "native_channel_with_provider_pair")
        {
            await Assert.That(() => context.SaveChangesAsync()).Throws<DbUpdateException>();
        }
        else
        {
            await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
        }
        context.ChangeTracker.Clear();
        await Assert.That(await context.RegistrationAttempts.AnyAsync(value => value.Id == attempt.Id)).IsFalse();
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AttemptAcceptsNativeNullPairOrExactPublishedProviderEvidence(bool native)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        RegistrationAttempt attempt = native ? graph.NativeAttempt : graph.ProviderAttempt;
        context.RegistrationAttempts.Add(attempt);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        RegistrationAttempt reloaded = await context.RegistrationAttempts.SingleAsync(value => value.Id == attempt.Id);
        await Assert.That(reloaded.RegistrationProviderBindingId).IsEqualTo(native ? (Guid?)null : graph.Binding.Id);
        await Assert.That(reloaded.ProviderMappingRevisionHash?.Value).IsEqualTo(native ? null : Hash().Value);
    }

    [Test]
    public async Task RolledBackPublicationLeavesDraftAndNoPublicationEvidence()
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        await context.SaveChangesAsync();

        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            graph.Binding.Publish(Hash(), Now.AddMinutes(1));
            await context.SaveChangesAsync();
            await Assert.That(await CountPublicationRowsAsync(context, graph.Binding.Publication!)).IsEqualTo(1);
            await transaction.RollbackAsync();
        }

        var publication = graph.Binding.Publication!;
        context.ChangeTracker.Clear();
        RegistrationProviderBinding reloaded = await context.RegistrationProviderBindings
            .Include(binding => binding.Publication).SingleAsync(binding => binding.Id == graph.Binding.Id);

        await Assert.That(reloaded.StateId).IsEqualTo((int)RegistrationProviderBindingStateEnum.Draft);
        await Assert.That(reloaded.Publication).IsNull();
        await Assert.That(reloaded.PublishedAt).IsNull();
        await Assert.That(reloaded.PublishedMappingRevisionHash).IsNull();
        await Assert.That(await CountPublicationRowsAsync(context, publication)).IsEqualTo(0);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task AttemptCannotPersistHalfOfItsProviderPublicationCoordinates(bool removeBinding)
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));
        await context.SaveChangesAsync();
        context.RegistrationAttempts.Add(graph.ProviderAttempt);
        if (removeBinding)
            context.Entry(graph.ProviderAttempt).Property(value => value.RegistrationProviderBindingId).CurrentValue = null;
        else
            context.Entry(graph.ProviderAttempt).Property(value => value.ProviderMappingRevisionHash).CurrentValue = null;

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();
        await Assert.That(await context.RegistrationAttempts.AnyAsync(value => value.Id == graph.ProviderAttempt.Id)).IsFalse();
        await Assert.That(await context.RegistrationProviderBindingPublications.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task PersistedBlockedBindingCannotMasqueradeAsThePublishingDraft()
    {
        await using ExploreDbContext context = CreateSqliteContext(":memory:");
        PublicationGraph graph = await StagePublicationGraphAsync(context);
        graph.Binding.SetDriftClass(RegistrationProviderDriftClassEnum.TypeChanged);
        await context.SaveChangesAsync();
        context.Entry(graph.Binding).Property(binding => binding.StateId).CurrentValue = (int)RegistrationProviderBindingStateEnum.Draft;
        graph.Binding.SetDriftClass(RegistrationProviderDriftClassEnum.NoDrift);
        graph.Binding.Publish(Hash(), Now.AddMinutes(1));

        await Assert.That(() => context.SaveChangesAsync()).Throws<InvalidOperationException>();
        context.ChangeTracker.Clear();
        var reloaded = await context.RegistrationProviderBindings.SingleAsync(binding => binding.Id == graph.Binding.Id);
        await Assert.That(reloaded.StateId).IsEqualTo((int)RegistrationProviderBindingStateEnum.DriftBlocked);
        await Assert.That(reloaded.Publication).IsNull();
        await Assert.That(await context.RegistrationProviderBindingPublications.AnyAsync()).IsFalse();
    }

    private static async Task<PublicationGraph> StagePublicationGraphAsync(ExploreDbContext context, bool initializeDatabase = true)
    {
        if (initializeDatabase)
        {
            await context.Database.OpenConnectionAsync();
            await context.Database.EnsureCreatedAsync();
            await LookupTableSeeder.SeedAsync(context, CancellationToken.None);
        }

        Guid tenantId = Guid.CreateVersion7();
        context.TenantContext = new TestTenantContext(tenantId);
        User user = new() { Pii = new UserPii { Email = $"{Guid.CreateVersion7():N}@example.com", FirstName = "Publication", LastName = "Owner" } };
        context.AddRange(Tenant(tenantId, "publication"), user);
        await context.SaveChangesAsync();
        Actor actor = new() { Pii = new ActorPii { DisplayName = "Publication owner" }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = "Publication",
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenantId,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        RegistrationForm form = RegistrationForm.Create(tenantId, @event.Id, "platform.registration", "publication", "Publication", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        form.AddVersion(version);
        RegistrationProviderConnection connection = Connection(tenantId, "publication", false);
        RegistrationProviderBinding binding = RegistrationProviderBinding.Create(
            tenantId, connection.Id, form.Id, version.Id, RegistrationProviderPresentationModeEnum.Redirect,
            RegistrationProviderCollectionModeEnum.ProviderHosted, RegistrationProviderCompletionModeEnum.Callback,
            RegistrationProviderTrustLevelEnum.SelectedFields, null, Now);
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, @event.Id, "ATTENDEE_REGISTRATION", Now);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_SYNC,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, Now);
        RegistrationChannel providerChannel = RegistrationChannel.Create(requirement, 1, false, binding.Id, Now);
        RegistrationChannel nativeChannel = RegistrationChannel.Create(requirement, 2, true, null, Now);
        requirement.AddChannel(providerChannel);
        requirement.AddChannel(nativeChannel);
        workflow.AddRequirement(requirement);
        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenantId, @event.Id, "EUR", 1);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId, @event.Id, user.Id, null, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", Now, Now.AddHours(1));
        context.AddRange(form, connection, binding, workflow, catalog, order);

        RegistrationAttempt nativeAttempt = RegistrationAttempt.Create(
            tenantId, @event.Id, order.Id, workflow.Id, requirement.Id, nativeChannel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(Hash().Value), null, null, Now.AddMinutes(2), Now.AddHours(1));
        RegistrationAttempt providerAttempt = RegistrationAttempt.Create(
            tenantId, @event.Id, order.Id, workflow.Id, requirement.Id, providerChannel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(OtherPublicationHash().Value), binding.Id, Hash(), Now.AddMinutes(2), Now.AddHours(1));
        return new(binding, nativeAttempt, providerAttempt);
    }

    private static Task<int> CountPublicationRowsAsync<TPublication>(ExploreDbContext context, TPublication _)
        where TPublication : class => context.Set<TPublication>().CountAsync();

    private static RegistrationEvidenceHash OtherPublicationHash() =>
        RegistrationEvidenceHash.Create(Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()));

    private sealed record PublicationGraph(
        RegistrationProviderBinding Binding,
        RegistrationAttempt NativeAttempt,
        RegistrationAttempt ProviderAttempt);
}
