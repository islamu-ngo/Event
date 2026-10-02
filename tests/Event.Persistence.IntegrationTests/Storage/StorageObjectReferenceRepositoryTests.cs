using System.Security.Cryptography;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Interfaces;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ResourceScope = Event.Persistence.IntegrationTests.EventResourcePersistenceTests.ResourceScope;

namespace Event.Persistence.IntegrationTests.Storage;

[ClassDataSource<EventResourceFileUploadTests.Database>(Shared = SharedType.PerClass)]
[NotInParallel]
public sealed class StorageObjectReferenceRepositoryTests(EventResourceFileUploadTests.Database database)
{
    private static readonly DateTime Now = new(2040, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task EveryCurrentModelOwnerRelationBlocksUntilDetachedOrRolledBack()
    {
        var scope = await SeedAsync();
        await using var modelContext = database.CreateContext();
        var relations = modelContext.Model.FindEntityType(typeof(StorageObject))!.GetReferencingForeignKeys()
            .Where(key => key.DeclaringEntityType.ClrType != typeof(StorageUploadSession))
            .ToArray();
        await Assert.That(relations.Length).IsGreaterThan(0);
        foreach (var relation in relations)
        {
            await using var context = database.CreateContext();
            await using var transaction = await FenceAsync(context, scope.StorageAId);
            IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
            object owner = await CreateOwnerAsync(context, scope, relation.DeclaringEntityType.ClrType);
            var entry = context.Entry(owner);
            var idProperty = relation.Properties.Where((_, index) =>
                relation.PrincipalKey.Properties[index].Name == nameof(StorageObject.Id)).Single();
            entry.Property(idProperty.Name).CurrentValue = scope.StorageAId;
            // Resource payload constraints require detachment before soft deletion;
            // its draft attachment still blocks independently of public visibility.
            if (owner is ISoftDeletable deleted && owner is not EventResource)
                deleted.IsDeleted = true;
            await context.SaveChangesAsync();
            context.TenantContext = new TenantScope(scope.TenantBId);

            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsTrue();
            await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageBId, default)).IsFalse();
            if (idProperty.IsNullable)
            {
                entry.Property(idProperty.Name).CurrentValue = null;
                await context.SaveChangesAsync();
                await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
            }
            Console.WriteLine($"Verified blocking relation: {relation.DeclaringEntityType.ClrType.Name}.{idProperty.Name}");
            await transaction.RollbackAsync();
        }
        await using var verify = database.CreateContext();
        await using var finalTransaction = await FenceAsync(verify, scope.StorageAId);
        await Assert.That(await new StorageObjectReferenceRepository(verify)
            .HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
    }

    [Test]
    public async Task DetachingVisibleUsePreservesHiddenSharedOtherTenantUse()
    {
        var scope = await SeedAsync();
        await using (var seed = database.CreateContext())
        {
            var visible = await seed.Events.SingleAsync(row => row.Id == scope.EventAId);
            var hidden = await seed.Events.SingleAsync(row => row.Id == scope.EventBId);
            visible.FeaturedImageId = scope.StorageAId;
            hidden.BackgroundImageId = scope.StorageAId;
            hidden.IsDeleted = true;
            await seed.SaveChangesAsync();
        }
        await using var context = database.CreateContext();
        context.TenantContext = new TenantScope(scope.TenantAId);
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        var first = await context.Events.SingleAsync(row => row.Id == scope.EventAId);
        first.FeaturedImageId = null;
        await context.SaveChangesAsync();
        await Assert.That(await context.Events.AnyAsync(row => row.Id == scope.EventBId)).IsFalse();
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsTrue();
        var last = await context.Events.IgnoreQueryFilters().SingleAsync(row => row.Id == scope.EventBId);
        last.BackgroundImageId = null;
        await context.SaveChangesAsync();
        await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProducerCustodyIsNotAReadableReferenceOrRetentionHold(bool settled)
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        var binding = await context.Set<StorageProviderBinding>().SingleAsync(row => row.Id == source.StorageProviderBindingId);
        var session = new StorageUploadSession
        {
            Id = source.Id, TenantId = source.TenantId, Provider = source.Provider,
            StorageProviderBindingId = binding.Id, ObjectKey = source.ObjectKey,
            StorageObjectId = source.Id, ContentType = source.ContentType!,
            SafeDisplayName = source.SafeDisplayName, Purpose = source.Purpose,
            Visibility = source.Visibility, Status = StorageUploadSessionStates.Reserved,
            ExpiresAt = Now.AddHours(1)
        };
        session.MarkUploading(Now);
        var producer = StorageProducerOperation.Create(source.Id, source.TenantId, binding, source.ObjectKey!, Now);
        if (settled)
        {
            session.RecordProducerSettlement(source.Id, binding.Id, source.ObjectKey!, null);
            session.Finalize(source.Id, source.ObjectKey!, null, Now);
            producer.Settle(binding.Id, source.Provider, source.ObjectKey!, null);
        }
        context.AddRange(session, producer);
        await context.SaveChangesAsync();
        await using var transaction = await FenceAsync(context, source.Id);
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        await Assert.That(await repository.HasBlockingReferencesAsync(source.Id, default)).IsFalse();
        await Assert.That(await repository.HasBlockingHoldsAsync(source.Id, Now, default)).IsFalse();
        await Assert.That(await context.StorageUploadSessions.AnyAsync(row => row.Id == source.Id)).IsTrue();
        await Assert.That(await context.Set<StorageProducerOperation>().AnyAsync(row => row.Id == source.Id)).IsTrue();
    }

    [Test]
    [Arguments(-1, true)]
    [Arguments(0, false)]
    [Arguments(1, false)]
    public async Task PublishedCsvRetentionExpiresAtExactServerDeadlineWithoutSourceAnswers(int ticks, bool blocked)
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        var graph = await SeedRegistrationAsync(context, scope);
        await AttachCsvAsync(context, scope, graph.Submission.Id);
        await Assert.That(await context.RegistrationAnswers.AnyAsync(row =>
            row.RegistrationSubmissionId == graph.Submission.Id)).IsFalse();
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
        await Assert.That(await repository.HasBlockingHoldsAsync(scope.StorageAId, Now.AddTicks(ticks), default))
            .IsEqualTo(blocked);
    }

    [Test]
    [Arguments("legal_hold")]
    [Arguments("unbounded_policy")]
    [Arguments("draft")]
    [Arguments("no_fields")]
    [Arguments("missing_submission")]
    [Arguments("wrong_tenant")]
    [Arguments("no_deadline")]
    [Arguments("no_anonymous_authority")]
    public async Task CsvRequiresCompleteFinitePublishedRetentionAuthority(string condition)
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        var registrationScope = condition == "wrong_tenant"
            ? scope with { TenantAId = scope.TenantBId, EventAId = scope.EventBId, CatalogAId = scope.CatalogBId }
            : scope;
        var graph = await SeedRegistrationAsync(context, registrationScope, publish: condition != "draft",
            boundedOrder: condition != "no_anonymous_authority");
        await AttachCsvAsync(context, scope, graph.Submission.Id);
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        switch (condition)
        {
            case "legal_hold":
                context.Entry(graph.Field).Property(row => row.RetentionPolicyId).CurrentValue =
                    (int)RegistrationRetentionPolicyEnum.LegalHold;
                break;
            case "unbounded_policy":
                var policy = new RegistrationRetentionPolicy
                {
                    Id = 9000, MasterCode = "UNBOUNDED", FullName = "Unbounded retention",
                    DurationDays = null, IsLegalHold = false
                };
                context.Add(policy);
                context.Entry(graph.Field).Property(row => row.RetentionPolicyId).CurrentValue = policy.Id;
                break;
            case "no_fields":
                await context.RegistrationFormFields.Where(row => row.RegistrationFormVersionId == graph.Version.Id)
                    .ExecuteDeleteAsync();
                context.Entry(graph.Field).State = EntityState.Detached;
                break;
            case "missing_submission":
                source.OwningResourceId = Guid.CreateVersion7();
                break;
            case "no_deadline":
                source.RegistrationContentRetentionUntilUtc = null;
                break;
        }
        graph.Submission.IsDeleted = true;
        graph.Version.IsDeleted = true;
        if (condition != "no_fields") graph.Field.IsDeleted = true;
        graph.Order.IsDeleted = true;
        await context.SaveChangesAsync();
        context.TenantContext = new TenantScope(scope.TenantBId);
        await Assert.That(await context.RegistrationSubmissions.AnyAsync(row => row.Id == graph.Submission.Id)).IsFalse();
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        await Assert.That(await repository.HasBlockingHoldsAsync(scope.StorageAId, Now.AddYears(1), default)).IsTrue();
        await Assert.That(await repository.HasBlockingHoldsAsync(scope.StorageBId, Now, default)).IsFalse();
    }

    [Test]
    public async Task HiddenExpiredAuthorityDoesNotInventAPermanentHold()
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        var graph = await SeedRegistrationAsync(context, scope);
        await AttachCsvAsync(context, scope, graph.Submission.Id);
        graph.Submission.IsDeleted = true;
        graph.Version.IsDeleted = true;
        graph.Field.IsDeleted = true;
        graph.Order.IsDeleted = true;
        await context.SaveChangesAsync();
        context.TenantContext = new TenantScope(scope.TenantBId);
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        await Assert.That(await new StorageObjectReferenceRepository(context)
            .HasBlockingHoldsAsync(scope.StorageAId, Now, default)).IsFalse();
    }

    [Test]
    [Arguments("pending", true)]
    [Arguments("processing", true)]
    [Arguments("retry", true)]
    [Arguments("parked", true)]
    [Arguments("missing", true)]
    [Arguments("completed", false)]
    [Arguments("dead_letter", false)]
    public async Task ProviderDeliveryMustBeSettledBeforeExpiredCsvCanRetire(string state, bool blocked)
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        var graph = await SeedRegistrationAsync(context, scope, provider: true);
        await AttachCsvAsync(context, scope, graph.Submission.Id);
        if (state != "missing")
        {
            var effect = RegistrationProviderSubmissionWriteEffect.Create(graph.Attempt, graph.Submission, Now);
            if (state != "pending")
            {
                Guid token = Guid.CreateVersion7();
                effect.Claim("retirement-test", token, Now.AddMinutes(1), Now);
                switch (state)
                {
                    case "retry": effect.ScheduleRetry(token, effect.ProcessingFence, "unavailable", Now.AddHours(1), Now); break;
                    case "parked": effect.ParkAmbiguous(token, effect.ProcessingFence, "provider_handoff_uncertain", Now); break;
                    case "completed": effect.Complete(token, effect.ProcessingFence, Now); break;
                    case "dead_letter": effect.DeadLetter(token, effect.ProcessingFence, "rejected_before_handoff", Now); break;
                }
            }
            context.Add(effect);
            await context.SaveChangesAsync();
        }
        context.TenantContext = new TenantScope(scope.TenantBId);
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        await Assert.That(await new StorageObjectReferenceRepository(context)
            .HasBlockingHoldsAsync(scope.StorageAId, Now.AddDays(1), default)).IsEqualTo(blocked);
    }

    [Test]
    public async Task ScansRequireCallerTransactionAndUtcClockWithoutCreatingAuthority()
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.HasBlockingReferencesAsync(scope.StorageAId, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.HasBlockingHoldsAsync(scope.StorageAId, Now, default));
        await Assert.That(context.Database.CurrentTransaction).IsNull();
        await using var transaction = await FenceAsync(context, scope.StorageAId);
        await Assert.ThrowsAsync<ArgumentException>(() => repository.HasBlockingHoldsAsync(
            scope.StorageAId, DateTime.SpecifyKind(Now, DateTimeKind.Unspecified), default));
        await Assert.That(await repository.HasBlockingReferencesAsync(scope.StorageAId, default)).IsFalse();
        await Assert.That(await repository.HasBlockingHoldsAsync(scope.StorageAId, Now, default)).IsFalse();
        await Assert.That(context.Database.CurrentTransaction!.TransactionId).IsEqualTo(transaction.TransactionId);
        await Assert.That(await context.StorageObjectDeletionTombstones.AnyAsync(row => row.Id == scope.StorageAId)).IsFalse();
    }

    private async Task<ResourceScope> SeedAsync()
    {
        await using var seed = EventResourcePersistenceTests.TestDatabase.CreateProvider(() => database.CreateContext());
        return await seed.SeedScopeAsync();
    }

    [Test]
    public async Task DeclaredWholeSetAllowsReverseOrderAcrossMultipleSaves()
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        IStorageObjectReferenceRepository repository = new StorageObjectReferenceRepository(context);
        var sources = await repository.FenceAsync([scope.StorageAId, scope.StorageBId], default);
        var ordered = sources.OrderBy(source => source.TenantId).ThenBy(source => source.Id).ToArray();
        var actor = await context.Actors.SingleAsync(row => row.Id == scope.ActorId);
        actor.Pii.SetProfilePicture(ordered[1].Id, null);
        await context.SaveChangesAsync();
        actor.Pii.SetProfilePicture(ordered[0].Id, null);
        await context.SaveChangesAsync();
        await Assert.That(await repository.HasBlockingReferencesAsync(ordered[0].Id, default)).IsTrue();
        await Assert.That(await repository.HasBlockingReferencesAsync(ordered[1].Id, default)).IsFalse();
        await transaction.CommitAsync();

        await using var verify = database.CreateContext();
        await Assert.That((await verify.Set<ActorPii>().SingleAsync(row => row.ActorId == scope.ActorId))
            .ProfilePictureStorageObjectId).IsEqualTo(ordered[0].Id);
    }

    [Test]
    public async Task TranslatedReferenceFailureCannotCommitTheJoinedUnitOfWork()
    {
        var scope = await SeedAsync();
        await using var context = database.CreateContext();
        IUnitOfWork unitOfWork = new EfCoreUnitOfWork(context);
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var actor = await context.Actors.SingleAsync(row => row.Id == scope.ActorId, token);
            actor.Pii.SetProfilePicture(Guid.CreateVersion7(), null);
            await Assert.ThrowsAsync<ConcurrencyConflictException>(() => context.SaveChangesAsync(token));
            return true;
        }));

        await using var verify = database.CreateContext();
        await Assert.That((await verify.Set<ActorPii>().SingleAsync(row => row.ActorId == scope.ActorId))
            .ProfilePictureStorageObjectId).IsNull();
    }

    private static async Task<IDbContextTransaction> FenceAsync(ExploreDbContext context, Guid id)
    {
        var transaction = await context.Database.BeginTransactionAsync();
        Guid stamp = Guid.CreateVersion7();
        await context.StorageObjects.IgnoreQueryFilters().Where(row => row.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyStamp, stamp));
        foreach (var entry in context.ChangeTracker.Entries<StorageObject>().Where(entry => entry.Entity.Id == id).ToArray())
            await entry.ReloadAsync();
        return transaction;
    }

    private static async Task<object> CreateOwnerAsync(ExploreDbContext context, ResourceScope scope, Type ownerType)
    {
        if (ownerType == typeof(Explore.Domain.Event))
            return await context.Events.SingleAsync(row => row.Id == scope.EventAId);
        if (ownerType == typeof(EventSession))
            return await context.EventSessions.SingleAsync(row => row.Id == scope.SessionAId);
        if (ownerType == typeof(EventDay))
            return await context.EventDays.SingleAsync(row => row.Id == scope.DayCId);
        if (ownerType == typeof(ActorPii))
            return await context.Set<ActorPii>().SingleAsync(row => row.ActorId == scope.ActorId);
        object owner;
        if (ownerType == typeof(EventSeries))
            owner = new EventSeries
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, ActorId = scope.ActorId,
                Title = "Unpublished series", VisibilityTypeId = (int)VisibilityTypeEnum.Private, VisibilityType = null!
            };
        else if (ownerType == typeof(GroupTenant))
            owner = new GroupTenant
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                Group = new Group { Id = Guid.CreateVersion7(), FullName = "Hidden group" },
                ApprovalStatusId = (int)ApprovalStatusEnum.Rejected, ApprovalStatus = null!, IsVisible = false
            };
        else if (ownerType == typeof(OrganizationTenant) || ownerType == typeof(OrganizationTenantEvidence))
        {
            var organization = new OrganizationTenant
            {
                Id = Guid.CreateVersion7(), TenantId = scope.TenantAId, Tenant = null!,
                Organization = new Organization { Id = Guid.CreateVersion7(), Pii = new OrganizationPii { FullName = "Hidden organization" } },
                ApprovalStatusId = (int)ApprovalStatusEnum.Rejected, ApprovalStatus = null!, IsVisible = false
            };
            if (ownerType == typeof(OrganizationTenantEvidence))
            {
                var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
                var evidence = OrganizationTenantEvidence.CreatePending(organization, source);
                Guid userId = (await context.Actors.SingleAsync(row => row.Id == scope.ActorId)).UserId!.Value;
                evidence.Review(false, userId, null, Now);
                owner = evidence;
            }
            else owner = organization;
        }
        else if (ownerType == typeof(EventResource))
            owner = EventResourcePersistenceTests.CreateDraft(scope.TenantAId, scope.EventAId);
        else if (ownerType == typeof(EventReportEvidence) || ownerType == typeof(EventReportTarget))
        {
            var report = EventReport.Create(scope.TenantAId, scope.EventAId, null, null,
                EventReporterKind.System, EventReportSourceKind.LocalRule, "storage", null,
                EventReportPriority.Normal, null, false, false, null, null, null, Now);
            context.Add(report);
            owner = ownerType == typeof(EventReportEvidence)
                ? EventReportEvidence.CreateSystemSignal(scope.TenantAId, report.Id, null,
                    EventReportEvidenceClassification.Sensitive, Now.AddYears(1), Now)
                : EventReportTarget.CreateEventTarget(scope.TenantAId, report.Id, scope.EventAId);
        }
        else if (ownerType == typeof(RegistrationAnswerFile))
        {
            var graph = await SeedRegistrationAsync(context, scope);
            var source = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
            var file = RegistrationAnswerFile.Create(scope.TenantAId, graph.Submission.Id, graph.Field, source, Now);
            Guid userId = (await context.Actors.SingleAsync(row => row.Id == scope.ActorId)).UserId!.Value;
            context.Add(file.ReleaseManually(userId, "Reviewed attachment", Now));
            owner = file;
        }
        else throw new InvalidOperationException($"A real SQLite owner fixture is required for {ownerType.Name}.");
        context.Add(owner);
        return owner;
    }

    private static async Task<RegistrationGraph> SeedRegistrationAsync(
        ExploreDbContext context, ResourceScope scope, bool publish = true, bool provider = false, bool boundedOrder = true)
    {
        DateTime created = Now.AddDays(-10);
        var workflow = RegistrationWorkflow.Create(scope.TenantAId, scope.EventAId, "RETIREMENT", created);
        var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_SYNC,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, created);
        workflow.AddRequirement(requirement);
        var form = RegistrationForm.Create(scope.TenantAId, scope.EventAId, "native", "retirement", "Retirement", created);
        var version = RegistrationFormVersion.Create(form, 1, "en", null, null, created);
        var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Documents", created);
        version.AddSection(section);
        var field = RegistrationFormField.Create(Guid.CreateVersion7(), section, 1, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, (int)RegistrationRetentionPolicyEnum.StandardOperational,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, false, created);
        version.AddField(section, field);
        if (publish) new FormSchemaArtifactPublicationService(new FormSchemaArtifactGenerator()).Publish(version, created);
        form.AddVersion(version);
        RegistrationProviderBinding? binding = null;
        var hash = RegistrationEvidenceHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        if (provider)
        {
            var connection = RegistrationProviderConnection.Create(scope.TenantAId, "CSV retention", RegistrationProviderKindEnum.ExternalApi,
                RegistrationProviderDeploymentKindEnum.HostedSaas, "EXCEL_COMPATIBLE", "CSV_STORAGE", "v1",
                "ISLAMU_EVENT_APPROVED_FIELDS_CSV_V1", "2026-08-12", "https://8.8.8.8", "https://8.8.8.8",
                StorageProviders.Local, null, null, created);
            binding = RegistrationProviderBinding.Create(scope.TenantAId, connection.Id, form.Id, version.Id,
                RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
                RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, created);
            binding.Publish(hash, created);
            context.AddRange(connection, binding);
        }
        var channel = RegistrationChannel.Create(requirement, 1, binding is null, binding?.Id, created);
        requirement.AddChannel(channel);
        var order = RegistrationOrder.Create(scope.TenantAId, scope.EventAId, null, null, BookingPartyTypeEnum.Individual,
            scope.CatalogAId, RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(),
                (int)ParticipationHandlingModeEnum.PlatformManaged, (int)AdvanceRegistrationObligationEnum.Required,
                (int)IdentityAccessModeEnum.GuestAllowed, GuestRecoveryPolicyEnum.EmailOptional), workflow.Id,
            CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))), "USD", created, Now.AddDays(1),
            boundedOrder ? Now : null);
        context.AddRange(workflow, form, order);
        await context.SaveChangesAsync();
        var attempt = RegistrationAttempt.Create(scope.TenantAId, scope.EventAId, order.Id, workflow.Id, requirement.Id,
            channel.Id, form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            binding?.Id, binding is null ? null : hash, created, Now.AddDays(1));
        var submission = binding is null
            ? RegistrationSubmission.Create(attempt, hash, created, null, null, null, null)
            : attempt.SubmitHeadlessProvider(hash, created.AddMinutes(1), null);
        context.AddRange(attempt, submission);
        await context.SaveChangesAsync();
        return new(order, version, field, attempt, submission);
    }

    private static async Task AttachCsvAsync(ExploreDbContext context, ResourceScope scope, Guid submissionId)
    {
        var storage = await context.StorageObjects.SingleAsync(row => row.Id == scope.StorageAId);
        storage.OwningResourceKind = "registration_submission_sink";
        storage.OwningResourceId = submissionId;
        storage.RegistrationContentRetentionUntilUtc = Now;
        await context.SaveChangesAsync();
    }

    private sealed record TenantScope(Guid TenantId) : ITenantContext;
    private sealed record RegistrationGraph(RegistrationOrder Order, RegistrationFormVersion Version,
        RegistrationFormField Field, RegistrationAttempt Attempt, RegistrationSubmission Submission);
}
