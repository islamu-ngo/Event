namespace Event.Domain.UnitTests.Entities;

using Explore.Domain.Enums;

public sealed class ActorLifecycleTests
{
    private static readonly DateTime TransitionedAt = new(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExternalUnclassified_HasStableLookupId()
    {
        await Assert.That((int)ActorTypeEnum.ExternalUnclassified).IsEqualTo(6);
    }

    [Test]
    public async Task TombstoneForUserPrivacyErasure_SeversUserOwnerAndRecordsDeletion()
    {
        Actor actor = CreateActor(ActorTypeEnum.User);
        Guid erasedBy = Guid.CreateVersion7();
        actor.UserId = erasedBy;
        actor.CreatedBy = erasedBy;
        actor.UpdatedBy = erasedBy;
        actor.DeletedBy = erasedBy;
        Guid concurrencyStamp = actor.ConcurrencyStamp;

        actor.TombstoneForUserPrivacyErasure(TransitionedAt, erasedBy);

        await Assert.That(OwnerCount(actor)).IsEqualTo(0);
        await Assert.That(actor.IsDeleted).IsTrue();
        await Assert.That(actor.DeletedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.CreatedBy).IsNull();
        await Assert.That(actor.DeletedBy).IsNull();
        await Assert.That(actor.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.UpdatedBy).IsNull();
        await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(concurrencyStamp);
    }

    [Test]
    public async Task PromoteToOrganization_PreservesIdentityAndPii_AndReplacesOnlyExternalOwner()
    {
        var (actor, externalSubject, pii, identity) = CreateExternalActor();
        var actorId = actor.Id;
        var identityId = identity.Id;
        var actorConcurrencyStamp = actor.ConcurrencyStamp;
        var externalConcurrencyStamp = externalSubject.ConcurrencyStamp;
        var promotedBy = Guid.CreateVersion7();
        var organization = CreateOrganization();
        var organizationActorType = CreateActorType(ActorTypeEnum.Organization);

        actor.PromoteToOrganization(organization, organizationActorType, TransitionedAt, promotedBy);

        await Assert.That(actor.Id).IsEqualTo(actorId);
        await Assert.That(identity.Id).IsEqualTo(identityId);
        await Assert.That(identity.ActorId).IsEqualTo(actorId);
        await Assert.That(ReferenceEquals(actor.Pii, pii)).IsTrue();
        await Assert.That(actor.DisplayName).IsEqualTo("External organizer");
        await Assert.That(actor.ProfilePictureUri).IsEqualTo("https://cdn.example/avatar.png");
        await Assert.That(actor.ExternalActorSubjectId).IsNull();
        await Assert.That(actor.ExternalActorSubject).IsNull();
        await Assert.That(actor.OrganizationId).IsEqualTo(organization.Id);
        await Assert.That(ReferenceEquals(actor.Organization, organization)).IsTrue();
        await Assert.That(ReferenceEquals(organization.Actor, actor)).IsTrue();
        await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.Organization);
        await Assert.That(ReferenceEquals(actor.ActorType, organizationActorType)).IsTrue();
        await Assert.That(OwnerCount(actor)).IsEqualTo(1);
        await Assert.That(actor.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.UpdatedBy).IsEqualTo(promotedBy);
        await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(actorConcurrencyStamp);
        await Assert.That(externalSubject.IsDeleted).IsTrue();
        await Assert.That(externalSubject.DeletedAt).IsEqualTo(TransitionedAt);
        await Assert.That(externalSubject.DeletedBy).IsEqualTo(promotedBy);
        await Assert.That(externalSubject.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(externalSubject.UpdatedBy).IsEqualTo(promotedBy);
        await Assert.That(externalSubject.ConcurrencyStamp).IsNotEqualTo(externalConcurrencyStamp);
        await Assert.That(externalSubject.FirstObservedAt).IsEqualTo(TransitionedAt.AddDays(-2));
        await Assert.That(externalSubject.LastObservedAt).IsEqualTo(TransitionedAt.AddDays(-1));
    }

    [Test]
    public async Task PromoteToGroup_PreservesIdentityAndPii_AndReplacesOnlyExternalOwner()
    {
        var (actor, externalSubject, pii, identity) = CreateExternalActor();
        var actorId = actor.Id;
        var identityId = identity.Id;
        var promotedBy = Guid.CreateVersion7();
        var group = new Group { Id = Guid.CreateVersion7(), FullName = "Promoted group" };
        var groupActorType = CreateActorType(ActorTypeEnum.Group);

        actor.PromoteToGroup(group, groupActorType, TransitionedAt, promotedBy);

        await Assert.That(actor.Id).IsEqualTo(actorId);
        await Assert.That(identity.Id).IsEqualTo(identityId);
        await Assert.That(identity.ActorId).IsEqualTo(actorId);
        await Assert.That(ReferenceEquals(actor.Pii, pii)).IsTrue();
        await Assert.That(actor.ExternalActorSubjectId).IsNull();
        await Assert.That(actor.GroupId).IsEqualTo(group.Id);
        await Assert.That(ReferenceEquals(actor.Group, group)).IsTrue();
        await Assert.That(ReferenceEquals(group.Actor, actor)).IsTrue();
        await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.Group);
        await Assert.That(ReferenceEquals(actor.ActorType, groupActorType)).IsTrue();
        await Assert.That(OwnerCount(actor)).IsEqualTo(1);
        await Assert.That(externalSubject.IsDeleted).IsTrue();
        await Assert.That(externalSubject.Actor).IsNull();
    }

    [Test]
    public async Task RetireAsMergedSource_PreservesExternalOwnerPiiIdentityAndMergeEvidence()
    {
        var (actor, externalSubject, pii, identity) = CreateExternalActor();
        var actorId = actor.Id;
        var externalSubjectId = externalSubject.Id;
        var identityId = identity.Id;
        var sourceConcurrencyStamp = actor.ConcurrencyStamp;
        var retiredBy = Guid.CreateVersion7();
        var merge = ActorMerge.Create(
            actor.Id,
            Guid.CreateVersion7(),
            ActorMergeProofKind.VerifiedDid,
            "did-proof:sha256:abc123",
            TransitionedAt,
            retiredBy);
        actor.MergesFrom.Add(merge);

        actor.RetireAsMergedSource(TransitionedAt, retiredBy);

        await Assert.That(actor.Id).IsEqualTo(actorId);
        await Assert.That(actor.ExternalActorSubjectId).IsEqualTo(externalSubjectId);
        await Assert.That(ReferenceEquals(actor.ExternalActorSubject, externalSubject)).IsTrue();
        await Assert.That(externalSubject.IsDeleted).IsFalse();
        await Assert.That(ReferenceEquals(actor.Pii, pii)).IsTrue();
        await Assert.That(actor.DisplayName).IsEqualTo("External organizer");
        await Assert.That(identity.Id).IsEqualTo(identityId);
        await Assert.That(identity.ActorId).IsEqualTo(actorId);
        await Assert.That(actor.MergesFrom.Single()).IsEqualTo(merge);
        await Assert.That(merge.EvidenceReference).IsEqualTo("did-proof:sha256:abc123");
        await Assert.That(actor.IsDeleted).IsTrue();
        await Assert.That(actor.DeletedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.DeletedBy).IsEqualTo(retiredBy);
        await Assert.That(actor.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.UpdatedBy).IsEqualTo(retiredBy);
        await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(sourceConcurrencyStamp);
        await Assert.That(OwnerCount(actor)).IsEqualTo(1);
    }

    [Test]
    public async Task ExternalActorSubject_Retire_UpdatesSoftDeleteAuditAndConcurrency()
    {
        var (_, externalSubject, _, _) = CreateExternalActor();
        var concurrencyStamp = externalSubject.ConcurrencyStamp;
        var retiredBy = Guid.CreateVersion7();

        externalSubject.Retire(TransitionedAt, retiredBy);

        await Assert.That(externalSubject.IsDeleted).IsTrue();
        await Assert.That(externalSubject.DeletedAt).IsEqualTo(TransitionedAt);
        await Assert.That(externalSubject.DeletedBy).IsEqualTo(retiredBy);
        await Assert.That(externalSubject.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(externalSubject.UpdatedBy).IsEqualTo(retiredBy);
        await Assert.That(externalSubject.ConcurrencyStamp).IsNotEqualTo(concurrencyStamp);
    }

    [Test]
    public async Task PromoteToOrganization_FromUserActor_IsRejected()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        actor.UserId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            actor.PromoteToOrganization(
                CreateOrganization(),
                CreateActorType(ActorTypeEnum.Organization),
                TransitionedAt,
                Guid.CreateVersion7())));
    }

    [Test]
    public async Task PromoteToOrganization_WithoutExternalOwner_IsRejected()
    {
        var actor = CreateActor(ActorTypeEnum.ExternalUnclassified);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            actor.PromoteToOrganization(
                CreateOrganization(),
                CreateActorType(ActorTypeEnum.Organization),
                TransitionedAt,
                Guid.CreateVersion7())));
    }

    [Test]
    public async Task PromoteToOrganization_WithAdditionalOwner_IsRejectedWithoutRetiringExternalSubject()
    {
        var (actor, externalSubject, _, _) = CreateExternalActor();
        actor.UserId = Guid.CreateVersion7();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            actor.PromoteToOrganization(
                CreateOrganization(),
                CreateActorType(ActorTypeEnum.Organization),
                TransitionedAt,
                Guid.CreateVersion7())));
        await Assert.That(externalSubject.IsDeleted).IsFalse();
        await Assert.That(actor.ExternalActorSubjectId).IsEqualTo(externalSubject.Id);
    }

    [Test]
    public async Task PromoteToOrganization_WithMismatchedActorType_IsRejectedWithoutMutation()
    {
        var (actor, externalSubject, _, _) = CreateExternalActor();

        await Assert.ThrowsAsync<ArgumentException>(() => Task.Run(() =>
            actor.PromoteToOrganization(
                CreateOrganization(),
                CreateActorType(ActorTypeEnum.Group),
                TransitionedAt,
                Guid.CreateVersion7())));
        await Assert.That(externalSubject.IsDeleted).IsFalse();
        await Assert.That(actor.ActorTypeId).IsEqualTo((int)ActorTypeEnum.ExternalUnclassified);
        await Assert.That(actor.OrganizationId).IsNull();
    }

    [Test]
    public async Task PromoteToOrganization_RepeatedTransition_IsRejected()
    {
        var (actor, _, _, _) = CreateExternalActor();
        var organization = CreateOrganization();
        var organizationActorType = CreateActorType(ActorTypeEnum.Organization);
        var promotedBy = Guid.CreateVersion7();
        actor.PromoteToOrganization(organization, organizationActorType, TransitionedAt, promotedBy);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            actor.PromoteToOrganization(organization, organizationActorType, TransitionedAt.AddMinutes(1), promotedBy)));
    }

    [Test]
    public async Task Transition_FromSuspendedOrDeletedExternalActor_IsRejected()
    {
        var (suspendedActor, suspendedExternalSubject, _, _) = CreateExternalActor();
        suspendedActor.IsSuspended = true;
        var (deletedActor, deletedExternalSubject, _, _) = CreateExternalActor();
        deletedActor.IsDeleted = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            suspendedActor.RetireAsMergedSource(TransitionedAt, Guid.CreateVersion7())));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            deletedActor.RetireAsMergedSource(TransitionedAt, Guid.CreateVersion7())));
        await Assert.That(suspendedExternalSubject.IsDeleted).IsFalse();
        await Assert.That(deletedExternalSubject.IsDeleted).IsFalse();
    }

    [Test]
    public async Task Transition_WithRetiredExternalSubject_IsRejected()
    {
        var (actor, externalSubject, _, _) = CreateExternalActor();
        externalSubject.Retire(TransitionedAt, Guid.CreateVersion7());

        await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            actor.RetireAsMergedSource(TransitionedAt.AddMinutes(1), Guid.CreateVersion7())));
        await Assert.That(actor.IsDeleted).IsFalse();
    }

    [Test]
    public async Task Suspend_ActiveActor_UpdatesCurrentStateAndAppendsImmutableRecord()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        var suspendedBy = Guid.CreateVersion7();
        var concurrencyStamp = actor.ConcurrencyStamp;

        actor.Suspend(" policy-violation ", TransitionedAt, suspendedBy);

        await Assert.That(actor.IsSuspended).IsTrue();
        await Assert.That(actor.SuspendedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.SuspendedBy).IsEqualTo(suspendedBy);
        await Assert.That(actor.ModerationReasonCode).IsEqualTo("policy-violation");
        await Assert.That(actor.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.UpdatedBy).IsEqualTo(suspendedBy);
        await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(concurrencyStamp);
        await Assert.That(actor.ModerationRecords.Count).IsEqualTo(1);
        await Assert.That(actor.ModerationRecords.Single().Action).IsEqualTo(GlobalModerationAction.Suspend);
        await Assert.That(actor.ModerationRecords.Single().ReasonCode).IsEqualTo("policy-violation");
        await Assert.That(actor.ModerationRecords.Single().CreatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.ModerationRecords.Single().CreatedBy).IsEqualTo(suspendedBy);
    }

    [Test]
    public async Task Reinstate_SuspendedActor_ClearsCurrentStateAndAppendsImmutableRecord()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        var suspendedBy = Guid.CreateVersion7();
        var reinstatedBy = Guid.CreateVersion7();
        actor.Suspend("policy-violation", TransitionedAt, suspendedBy);
        var suspension = actor.ModerationRecords.Single();
        var concurrencyStamp = actor.ConcurrencyStamp;
        var reinstatedAt = TransitionedAt.AddHours(1);

        actor.Reinstate(" appeal-granted ", reinstatedAt, reinstatedBy);

        await Assert.That(actor.IsSuspended).IsFalse();
        await Assert.That(actor.SuspendedAt).IsNull();
        await Assert.That(actor.SuspendedBy).IsNull();
        await Assert.That(actor.ModerationReasonCode).IsNull();
        await Assert.That(actor.UpdatedAt).IsEqualTo(reinstatedAt);
        await Assert.That(actor.UpdatedBy).IsEqualTo(reinstatedBy);
        await Assert.That(actor.ConcurrencyStamp).IsNotEqualTo(concurrencyStamp);
        await Assert.That(actor.ModerationRecords.Count).IsEqualTo(2);
        await Assert.That(suspension.Action).IsEqualTo(GlobalModerationAction.Suspend);
        await Assert.That(suspension.ReasonCode).IsEqualTo("policy-violation");
        await Assert.That(actor.ModerationRecords.Last().Action).IsEqualTo(GlobalModerationAction.Reinstate);
        await Assert.That(actor.ModerationRecords.Last().ReasonCode).IsEqualTo("appeal-granted");
        await Assert.That(actor.ModerationRecords.Last().CreatedAt).IsEqualTo(reinstatedAt);
        await Assert.That(actor.ModerationRecords.Last().CreatedBy).IsEqualTo(reinstatedBy);
    }

    [Test]
    public async Task Suspend_AlreadySuspendedActor_IsSuccessfulNoOp()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        var suspendedBy = Guid.CreateVersion7();
        actor.Suspend("policy-violation", TransitionedAt, suspendedBy);
        var concurrencyStamp = actor.ConcurrencyStamp;

        actor.Suspend("different-reason", TransitionedAt.AddHours(1), Guid.CreateVersion7());

        await Assert.That(actor.SuspendedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.SuspendedBy).IsEqualTo(suspendedBy);
        await Assert.That(actor.ModerationReasonCode).IsEqualTo("policy-violation");
        await Assert.That(actor.UpdatedAt).IsEqualTo(TransitionedAt);
        await Assert.That(actor.ConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(actor.ModerationRecords.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Reinstate_ActiveActor_IsSuccessfulNoOp()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        var concurrencyStamp = actor.ConcurrencyStamp;

        actor.Reinstate("appeal-granted", TransitionedAt, Guid.CreateVersion7());

        await Assert.That(actor.IsSuspended).IsFalse();
        await Assert.That(actor.UpdatedAt).IsNull();
        await Assert.That(actor.UpdatedBy).IsNull();
        await Assert.That(actor.ConcurrencyStamp).IsEqualTo(concurrencyStamp);
        await Assert.That(actor.ModerationRecords.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Suspend_RejectsInvalidInputsAndDeletedActor()
    {
        var actor = CreateActor(ActorTypeEnum.User);
        var by = Guid.CreateVersion7();

        await Assert.That(() => actor.Suspend(" ", TransitionedAt, by)).Throws<ArgumentException>();
        await Assert.That(() => actor.Suspend(new string('x', 129), TransitionedAt, by)).Throws<ArgumentException>();
        await Assert.That(() => actor.Suspend("policy-violation", TransitionedAt, Guid.Empty)).Throws<ArgumentException>();
        await Assert.That(() => actor.Suspend(
                "policy-violation",
                DateTime.SpecifyKind(TransitionedAt, DateTimeKind.Local),
                by))
            .Throws<ArgumentException>();

        actor.IsDeleted = true;

        await Assert.That(() => actor.Suspend("policy-violation", TransitionedAt, by))
            .Throws<InvalidOperationException>();
        await Assert.That(() => actor.Reinstate("appeal-granted", TransitionedAt, by))
            .Throws<InvalidOperationException>();
    }

    private static (Actor Actor, ExternalActorSubject ExternalSubject, ActorPii Pii, AtprotoIdentity Identity)
        CreateExternalActor()
    {
        var actor = CreateActor(ActorTypeEnum.ExternalUnclassified);
        var externalSubject = new ExternalActorSubject
        {
            Id = Guid.CreateVersion7(),
            Actor = actor,
            FirstObservedAt = TransitionedAt.AddDays(-2),
            LastObservedAt = TransitionedAt.AddDays(-1),
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        actor.ExternalActorSubjectId = externalSubject.Id;
        actor.ExternalActorSubject = externalSubject;
        var identity = new AtprotoIdentity(Explore.Domain.ValueObjects.AtprotoDid.Parse("did:plc:external-organizer"))
        {
            Id = Guid.CreateVersion7(),

            ActorId = actor.Id,
            Actor = actor,
            PdsHost = "https://pds.example",
            IsActive = true
        };
        actor.AtprotoIdentities.Add(identity);
        return (actor, externalSubject, actor.Pii, identity);
    }

    private static Actor CreateActor(ActorTypeEnum actorType)
    {
        var actorId = Guid.CreateVersion7();
        return new Actor
        {
            Id = actorId,
            ActorTypeId = (int)actorType,
            ActorType = CreateActorType(actorType),
            Pii = new ActorPii
            {
                ActorId = actorId,
                DisplayName = "External organizer",
                ProfilePictureUri = "https://cdn.example/avatar.png"
            },
            ConcurrencyStamp = Guid.CreateVersion7()
        };
    }

    private static ActorType CreateActorType(ActorTypeEnum actorType) => new()
    {
        Id = (int)actorType,
        FullName = actorType.ToString(),
        MasterCode = actorType.ToString().ToUpperInvariant()
    };

    private static Organization CreateOrganization()
    {
        var organizationId = Guid.CreateVersion7();
        return new Organization
        {
            Id = organizationId,
            Pii = new OrganizationPii
            {
                OrganizationId = organizationId,
                FullName = "Promoted organization"
            }
        };
    }

    private static int OwnerCount(Actor actor) => new Guid?[]
    {
        actor.UserId,
        actor.OrganizationId,
        actor.GroupId,
        actor.ExternalActorSubjectId,
        actor.ServicePrincipalId
    }.Count(id => id.HasValue);
}
