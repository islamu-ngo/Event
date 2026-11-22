using Explore.Domain.Settings;

namespace Event.Domain.UnitTests.Settings;

public sealed class AnnouncementPublicationTests
{
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-4000-8000-000000000001");
    private static readonly Guid OtherTenantId = Guid.Parse("00000000-0000-4000-8000-000000000002");
    private static readonly Guid Edition = Guid.Parse("00000000-0000-4000-8000-000000000011");
    private static readonly Guid NextEdition = Guid.Parse("00000000-0000-4000-8000-000000000012");
    private static readonly Guid EarlierEdition = Guid.Parse("00000000-0000-4000-8000-000000000013");
    private static readonly Guid InstanceEpoch = Guid.Parse("00000000-0000-4000-8000-000000000021");
    private static readonly Guid TenantEpoch = Guid.Parse("00000000-0000-4000-8000-000000000022");
    private static readonly Guid NextEpoch = Guid.Parse("00000000-0000-4000-8000-000000000023");
    private static readonly Guid FinalEpoch = Guid.Parse("00000000-0000-4000-8000-000000000024");
    private static readonly AnnouncementMaterial InstanceMaterial = new("Instance notice", "Details", "/instance-details");
    private static readonly AnnouncementMaterial TenantMaterial = new("Tenant notice", "Read more", "/tenant-details");

    [Test]
    public async Task OwnersDistinguishInstanceFromAnActualTenantAndRejectEmptyTenantIdentity()
    {
        await Assert.That(AnnouncementOwner.Instance.TenantId).IsNull();
        await Assert.That(AnnouncementOwner.ForTenant(TenantId).TenantId).IsEqualTo(TenantId);
        await Assert.That(AnnouncementOwner.ForTenant(TenantId)).IsEqualTo(AnnouncementOwner.ForTenant(TenantId));
        await Assert.That(AnnouncementOwner.ForTenant(TenantId)).IsNotEqualTo(AnnouncementOwner.Instance);
        await Assert.That(() => AnnouncementOwner.ForTenant(Guid.Empty)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task InvalidPublicationGroupsCannotEnterResolution(int invalidPart)
    {
        await Assert.That(() =>
        {
            var group = new AnnouncementGroup(
                invalidPart == 0 ? Guid.Empty : InstanceEpoch,
                true,
                invalidPart == 2 ? null : new AnnouncementPublication(
                    invalidPart == 1 ? Guid.Empty : Edition, InstanceMaterial));
            return Resolve(new(AnnouncementOwner.Instance, group, false));
        }).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FirstPublicationRequiresBothOwnerSuppliedIdentities(bool tenantOwned)
    {
        var owner = tenantOwned ? AnnouncementOwner.ForTenant(TenantId) : AnnouncementOwner.Instance;
        var change = new AnnouncementChange.Publish(InstanceMaterial, true, false);

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(null, change)).IsTrue();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(null, false, change)).IsTrue();
        var written = AnnouncementPublicationPolicy.Apply(owner, null, false, change, Edition, InstanceEpoch);

        await Assert.That(written.Group).IsEqualTo(new AnnouncementGroup(
            InstanceEpoch, true, new AnnouncementPublication(Edition, InstanceMaterial)));
        await Assert.That(written.IsLocked).IsFalse();
    }

    [Test]
    public async Task IdenticalNormalizedPublicationPreservesBothIdentitiesWithoutAllocation()
    {
        var current = Group();
        var change = new AnnouncementChange.Publish(
            new("  Instance notice  ", " Details ", " /instance-details "), true, false);

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsFalse();
        var written = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, null, null);

        await Assert.That(written.Group).IsEqualTo(current);
        await Assert.That(current).IsEqualTo(Group());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ChangingAnyMaterialFieldReplacesEditionAndEpoch(int changedField)
    {
        var current = Group();
        var material = new AnnouncementMaterial(
            changedField == 0 ? "Changed notice" : InstanceMaterial.Message,
            changedField == 1 ? "Changed label" : InstanceMaterial.LinkText,
            changedField == 2 ? "/changed-details" : InstanceMaterial.LinkUrl);
        var change = new AnnouncementChange.Publish(material, true, false);

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsTrue();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsTrue();
        var written = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, NextEdition, NextEpoch);

        await Assert.That(written.Group).IsEqualTo(new AnnouncementGroup(
            NextEpoch, true, new AnnouncementPublication(NextEdition, material)));
        await Assert.That(current).IsEqualTo(Group());
    }

    [Test]
    public async Task ExplicitRedisplayReplacesBothIdentitiesWithoutChangingMaterial()
    {
        var current = Group();
        var change = new AnnouncementChange.Publish(InstanceMaterial, true, true);

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsTrue();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsTrue();
        var written = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, NextEdition, NextEpoch);

        await Assert.That(written.Group).IsEqualTo(new AnnouncementGroup(
            NextEpoch, true, new AnnouncementPublication(NextEdition, InstanceMaterial)));
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task RequiredPublicationTokensCannotBeMissingEmptyOrReused(int invalidToken)
    {
        Guid? edition = invalidToken switch { 0 => null, 1 => Guid.Empty, 2 => Edition, _ => NextEdition };
        Guid? epoch = invalidToken switch { 3 => null, 4 => Guid.Empty, 5 => InstanceEpoch, _ => NextEpoch };
        var current = Group();

        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false,
            new AnnouncementChange.Publish(TenantMaterial, true, false), edition, epoch))
            .Throws<ArgumentException>();
        await Assert.That(current).IsEqualTo(Group());
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task UnchangedPublicationRejectsExtraneousTokens(bool editionSupplied, bool epochSupplied)
    {
        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, Group(), false,
            new AnnouncementChange.Publish(InstanceMaterial, true, false),
            editionSupplied ? NextEdition : null, epochSupplied ? NextEpoch : null))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task VisibilityAndLockChangesPreserveMaterialEdition(int transition)
    {
        var current = Group();
        bool changeLock = transition == 2;
        AnnouncementChange change = transition switch
        {
            0 => new AnnouncementChange.SetEnabled(false),
            1 => new AnnouncementChange.Publish(InstanceMaterial, false, false),
            _ => new AnnouncementChange.SetLock(true)
        };

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsTrue();
        var written = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, null, NextEpoch);

        await Assert.That(written.Group).IsEqualTo(new AnnouncementGroup(
            NextEpoch, changeLock, new AnnouncementPublication(Edition, InstanceMaterial)));
        await Assert.That(written.IsLocked).IsEqualTo(changeLock);
        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, NextEdition, NextEpoch))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task MaskOnlyTransitionRequiresANonemptyReplacementEpoch(int invalidToken)
    {
        Guid? epoch = invalidToken switch { 0 => null, 1 => Guid.Empty, _ => InstanceEpoch };

        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, Group(), false,
            new AnnouncementChange.SetLock(true), null, epoch)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RepeatingVisibilityOrLockStateDoesNotAllocate(bool changeLock)
    {
        var current = Group();
        AnnouncementChange change = changeLock
            ? new AnnouncementChange.SetLock(false) : new AnnouncementChange.SetEnabled(true);

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, current, false, change, null, null))
            .IsEqualTo(new AnnouncementWrite(current, false));
    }

    [Test]
    public async Task RemovingTenantPublicationRetainsTombstoneWithoutRepublishingInstanceMaterial()
    {
        var instance = Instance();
        var tenantGroup = new AnnouncementGroup(TenantEpoch, true, new AnnouncementPublication(NextEdition, TenantMaterial));
        var change = new AnnouncementChange.RemoveOverride();

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(tenantGroup, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(tenantGroup, false, change)).IsTrue();
        var removed = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), tenantGroup, false, change, null, NextEpoch);
        var resolved = Resolve(instance, removed.Group);

        await Assert.That(removed.Group).IsEqualTo(new AnnouncementGroup(NextEpoch, false, null));
        await Assert.That(Observed(resolved).Owner).IsEqualTo(AnnouncementOwner.Instance);
        await Assert.That(Observed(resolved).Edition).IsEqualTo(Edition);
        await Assert.That(Observed(resolved).SourceWitness.TenantSelectionEpoch).IsEqualTo(NextEpoch);
        await Assert.That(instance).IsEqualTo(Instance());
        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), tenantGroup, false, change, EarlierEdition, NextEpoch))
            .Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RemovingAnAlreadyInheritedPublicationPreservesAbsenceOrTombstone(bool hasTombstone)
    {
        AnnouncementGroup? current = hasTombstone ? new(TenantEpoch, false, null) : null;
        var change = new AnnouncementChange.RemoveOverride();

        await Assert.That(AnnouncementPublicationPolicy.RequiresMaterialEdition(current, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.RequiresSelectionEpoch(current, false, change)).IsFalse();
        await Assert.That(AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), current, false, change, null, null))
            .IsEqualTo(new AnnouncementWrite(current, false));
    }

    [Test]
    public async Task InstanceOwnerCannotRemoveATenantOverride()
    {
        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, Group(), false,
            new AnnouncementChange.RemoveOverride(), null, NextEpoch)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false, false, true)]
    [Arguments(false, true, true)]
    [Arguments(false, false, false)]
    [Arguments(true, true, true)]
    [Arguments(true, false, false)]
    public async Task SelectionUsesOneCompleteOwnerIncludingDisabledTenantOverrides(
        bool instanceLocked, bool tenantLocked, bool tenantEnabled)
    {
        var instance = new AnnouncementSourceSnapshot(AnnouncementOwner.Instance, Group(), instanceLocked);
        var tenant = new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(TenantId),
            new(TenantEpoch, tenantEnabled, new(NextEdition, TenantMaterial)), tenantLocked);

        var resolved = AnnouncementPublicationPolicy.Resolve(TenantId, instance, tenant);
        var observation = Observed(resolved);

        await Assert.That(observation.TenantId).IsEqualTo(TenantId);
        await Assert.That(observation.Owner).IsEqualTo(
            instanceLocked ? AnnouncementOwner.Instance : AnnouncementOwner.ForTenant(TenantId));
        await Assert.That(observation.Edition).IsEqualTo(instanceLocked ? Edition : NextEdition);
        await Assert.That(resolved.Material).IsEqualTo(instanceLocked ? InstanceMaterial : TenantMaterial);
        await Assert.That(resolved.IsVisible).IsEqualTo(instanceLocked || tenantEnabled);
        await Assert.That(observation.SourceWitness).IsEqualTo(
            new AnnouncementSourceWitness(InstanceEpoch, instanceLocked, TenantEpoch, tenantLocked));
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AbsentOrRemovedTenantPublicationInheritsWithoutAllocating(bool hasTombstone)
    {
        var instance = Instance();
        AnnouncementGroup? tenantGroup = hasTombstone ? new(TenantEpoch, false, null) : null;
        var first = Resolve(instance, tenantGroup);
        var second = Resolve(instance, tenantGroup);

        await Assert.That(first).IsEqualTo(second);
        await Assert.That(first.Material).IsEqualTo(InstanceMaterial);
        await Assert.That(Observed(first)).IsEqualTo(new AnnouncementObservation(
            TenantId, AnnouncementOwner.Instance, Edition,
            new(InstanceEpoch, false, hasTombstone ? TenantEpoch : null, false)));
        await Assert.That(instance).IsEqualTo(Instance());
    }

    [Test]
    public async Task ReadingAbsentSourcesDoesNotManufacturePublicationOrIdentity()
    {
        var instance = new AnnouncementSourceSnapshot(AnnouncementOwner.Instance, null, false);

        await Assert.That(Resolve(instance)).IsEqualTo(new AnnouncementResolution(null, null, false));
        await Assert.That(Resolve(instance)).IsEqualTo(new AnnouncementResolution(null, null, false));
        await Assert.That(instance.Group).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    public async Task ResolutionRejectsEmptyTenantOrSubstitutedSourceOwners(int substitution)
    {
        var instance = substitution == 0
            ? new AnnouncementSourceSnapshot(AnnouncementOwner.ForTenant(TenantId), Group(), false) : Instance();
        var tenant = new AnnouncementSourceSnapshot(substitution switch
        {
            1 => AnnouncementOwner.Instance,
            2 => AnnouncementOwner.ForTenant(OtherTenantId),
            _ => AnnouncementOwner.ForTenant(TenantId)
        }, null, false);

        await Assert.That(() => AnnouncementPublicationPolicy.Resolve(
            substitution == 3 ? Guid.Empty : TenantId, instance, tenant)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MissingOrDisabledPublicationCannotBeDismissed(bool missing)
    {
        var observed = Observed(Resolve(Instance()));
        var current = Resolve(new(AnnouncementOwner.Instance,
            missing ? null : new AnnouncementGroup(NextEpoch, false, new(Edition, InstanceMaterial)), false));

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(observed, current, null, null))
            .IsEqualTo(AnnouncementDismissalDecision.NotVisible);
    }

    [Test]
    public async Task CurrentObservationWithExplicitMarkerAbsenceAdmitsCreation()
    {
        var current = Resolve(Instance());

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(Observed(current), current, null, null))
            .IsEqualTo(AnnouncementDismissalDecision.Apply);
    }

    [Test]
    public async Task ExactPriorMarkerAdmitsReplacementForCurrentMaterial()
    {
        var current = Resolve(Instance());
        var previous = EarlierMarker();

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(Observed(current), current, previous, previous))
            .IsEqualTo(AnnouncementDismissalDecision.Apply);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MarkerPresenceChangesCannotBeOverwritten(bool markerAppeared)
    {
        var current = Resolve(Instance());
        var previous = EarlierMarker();

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(Observed(current), current,
            markerAppeared ? null : previous, markerAppeared ? previous : null))
            .IsEqualTo(AnnouncementDismissalDecision.MarkerChanged);
    }

    [Test]
    public async Task ExpectedMarkerComparisonIncludesItsExactStoredWitness()
    {
        var current = Resolve(Instance());
        var stored = EarlierMarker();
        var expected = new AnnouncementDismissalMarker(new(
            TenantId, AnnouncementOwner.Instance, EarlierEdition, new(NextEpoch, false, null, false)));

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(Observed(current), current, expected, stored))
            .IsEqualTo(AnnouncementDismissalDecision.MarkerChanged);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MaskCyclesRejectOldAdmissionButPreserveAlreadyDismissedMaterial(bool lockCycle)
    {
        var original = Resolve(Instance());
        var marker = new AnnouncementDismissalMarker(Observed(original));
        AnnouncementChange away = lockCycle
            ? new AnnouncementChange.SetLock(true) : new AnnouncementChange.SetEnabled(false);
        AnnouncementChange back = lockCycle
            ? new AnnouncementChange.SetLock(false) : new AnnouncementChange.SetEnabled(true);
        var first = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, Group(), false, away, null, NextEpoch);
        var returned = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.Instance, first.Group, first.IsLocked, back, null, FinalEpoch);
        var current = Resolve(new(AnnouncementOwner.Instance, returned.Group, returned.IsLocked));

        await Assert.That(returned.Group).IsEqualTo(
            new AnnouncementGroup(FinalEpoch, true, new(Edition, InstanceMaterial)));
        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(
            Observed(original), current, marker, marker)).IsEqualTo(AnnouncementDismissalDecision.SourceChanged);
        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(
            Observed(current), current, null, marker)).IsEqualTo(AnnouncementDismissalDecision.AlreadyDismissed);
        await Assert.That(marker.Observation).IsEqualTo(Observed(original));
    }

    [Test]
    public async Task OverrideAwayAndBackRejectsTheOldInheritedObservation()
    {
        var original = Resolve(Instance());
        var published = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), null, false,
            new AnnouncementChange.Publish(TenantMaterial, true, false), NextEdition, TenantEpoch);
        var selected = Resolve(Instance(), published.Group);
        var removed = AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), published.Group, published.IsLocked,
            new AnnouncementChange.RemoveOverride(), null, NextEpoch);
        var returned = Resolve(Instance(), removed.Group);

        await Assert.That(Observed(selected).Owner).IsEqualTo(AnnouncementOwner.ForTenant(TenantId));
        await Assert.That(Observed(returned).Owner).IsEqualTo(AnnouncementOwner.Instance);
        await Assert.That(Observed(returned).Edition).IsEqualTo(Observed(original).Edition);
        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(
            Observed(original), returned, null, null)).IsEqualTo(AnnouncementDismissalDecision.SourceChanged);
        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(
            Observed(returned), returned, null, null)).IsEqualTo(AnnouncementDismissalDecision.Apply);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task DismissalCannotSubstituteTenantOrSelectedOwner(bool substituteOwner)
    {
        var current = Resolve(Instance());
        var observed = Observed(current);
        var substituted = new AnnouncementObservation(
            substituteOwner ? TenantId : OtherTenantId,
            substituteOwner ? AnnouncementOwner.ForTenant(TenantId) : AnnouncementOwner.Instance,
            observed.Edition, observed.SourceWitness);

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(substituted, current, null, null))
            .IsEqualTo(AnnouncementDismissalDecision.SourceChanged);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task EditionAndEverySourceWitnessComponentParticipateInFinalAdmission(int changedPart)
    {
        var current = Resolve(Instance());
        var observed = new AnnouncementObservation(TenantId, AnnouncementOwner.Instance,
            changedPart == 0 ? NextEdition : Edition, new(
                changedPart == 1 ? NextEpoch : InstanceEpoch,
                changedPart == 2,
                changedPart == 3 ? TenantEpoch : null,
                changedPart == 4));

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(observed, current, null, null))
            .IsEqualTo(AnnouncementDismissalDecision.SourceChanged);
    }

    [Test]
    public async Task MatchingEditionFromAnotherOwnerDoesNotCountAsAlreadyDismissed()
    {
        var prior = new AnnouncementDismissalMarker(Observed(Resolve(Instance())));
        var current = Resolve(Instance(), new(TenantEpoch, true, new(Edition, TenantMaterial)));

        await Assert.That(AnnouncementPublicationPolicy.DecideDismissal(
            Observed(current), current, prior, prior)).IsEqualTo(AnnouncementDismissalDecision.Apply);
    }

    [Test]
    public async Task EnablingAnAbsentPublicationCannotCreateMaterial()
    {
        await Assert.That(() => AnnouncementPublicationPolicy.Apply(
            AnnouncementOwner.ForTenant(TenantId), null, false,
            new AnnouncementChange.SetEnabled(true), null, NextEpoch)).Throws<ArgumentException>();
    }

    private static AnnouncementGroup Group() =>
        new(InstanceEpoch, true, new(Edition, InstanceMaterial));

    private static AnnouncementSourceSnapshot Instance() =>
        new(AnnouncementOwner.Instance, Group(), false);

    private static AnnouncementResolution Resolve(
        AnnouncementSourceSnapshot instance, AnnouncementGroup? tenantGroup = null) =>
        AnnouncementPublicationPolicy.Resolve(
            TenantId, instance, new(AnnouncementOwner.ForTenant(TenantId), tenantGroup, false));

    private static AnnouncementObservation Observed(AnnouncementResolution resolution) =>
        resolution.Observation ?? throw new InvalidOperationException("The fixture requires a selected publication.");

    private static AnnouncementDismissalMarker EarlierMarker() =>
        new(new(TenantId, AnnouncementOwner.Instance, EarlierEdition, new(InstanceEpoch, false, null, false)));
}
