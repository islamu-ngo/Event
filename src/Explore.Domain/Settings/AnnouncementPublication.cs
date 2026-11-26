namespace Explore.Domain.Settings;

public sealed record AnnouncementOwner
{
    private AnnouncementOwner(Guid? tenantId) => TenantId = tenantId;

    public Guid? TenantId { get; }
    public static AnnouncementOwner Instance { get; } = new((Guid?)null);

    public static AnnouncementOwner ForTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A tenant announcement requires an actual tenant identity.", nameof(tenantId));
        return new(tenantId);
    }
}

public sealed record AnnouncementMaterial
{
    public AnnouncementMaterial(string message, string linkText, string linkUrl)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(linkText);
        ArgumentNullException.ThrowIfNull(linkUrl);
        Message = message.Trim();
        LinkText = linkText.Trim();
        LinkUrl = linkUrl.Trim();
    }

    public string Message { get; }
    public string LinkText { get; }
    public string LinkUrl { get; }
}

public sealed record AnnouncementPublication
{
    public AnnouncementPublication(Guid edition, AnnouncementMaterial material)
    {
        if (edition == Guid.Empty)
            throw new ArgumentException("A publication requires a nonempty material edition.", nameof(edition));
        ArgumentNullException.ThrowIfNull(material);
        Edition = edition;
        Material = material;
    }

    public Guid Edition { get; }
    public AnnouncementMaterial Material { get; }
}

public sealed record AnnouncementGroup
{
    public AnnouncementGroup(Guid selectionEpoch, bool enabled, AnnouncementPublication? publication)
    {
        if (selectionEpoch == Guid.Empty)
            throw new ArgumentException("A source group requires a nonempty selection epoch.", nameof(selectionEpoch));
        if (enabled && publication is null)
            throw new ArgumentException("A source without publication material cannot be enabled.", nameof(enabled));
        SelectionEpoch = selectionEpoch;
        Enabled = enabled;
        Publication = publication;
    }

    public Guid SelectionEpoch { get; }
    public bool Enabled { get; }
    public AnnouncementPublication? Publication { get; }
}

public sealed record AnnouncementSourceSnapshot
{
    public AnnouncementSourceSnapshot(AnnouncementOwner owner, AnnouncementGroup? group, bool isLocked)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (group is null && isLocked)
            throw new ArgumentException("An absent source row cannot carry a lock.", nameof(isLocked));
        Owner = owner;
        Group = group;
        IsLocked = isLocked;
    }

    public AnnouncementOwner Owner { get; }
    public AnnouncementGroup? Group { get; }
    public bool IsLocked { get; }
}

/// <summary>
/// Comparison data supplied with an observation, not proof of source ownership or admission.
/// Lock flags are compared with acquired source facts rather than trusted independently.
/// </summary>
public sealed record AnnouncementSourceWitness
{
    public AnnouncementSourceWitness(Guid? instanceSelectionEpoch, bool instanceIsLocked,
        Guid? tenantSelectionEpoch, bool tenantIsLocked)
    {
        if (instanceSelectionEpoch == Guid.Empty)
            throw new ArgumentException("An instance epoch must be nonempty or absent.", nameof(instanceSelectionEpoch));
        if (tenantSelectionEpoch == Guid.Empty)
            throw new ArgumentException("A tenant epoch must be nonempty or absent.", nameof(tenantSelectionEpoch));
        InstanceSelectionEpoch = instanceSelectionEpoch;
        InstanceIsLocked = instanceIsLocked;
        TenantSelectionEpoch = tenantSelectionEpoch;
        TenantIsLocked = tenantIsLocked;
    }

    public Guid? InstanceSelectionEpoch { get; }
    public bool InstanceIsLocked { get; }
    public Guid? TenantSelectionEpoch { get; }
    public bool TenantIsLocked { get; }
}

public sealed record AnnouncementObservation
{
    public AnnouncementObservation(Guid tenantId, AnnouncementOwner owner, Guid edition,
        AnnouncementSourceWitness sourceWitness)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(sourceWitness);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("An observation requires an actual tenant identity.", nameof(tenantId));
        if (owner.TenantId is { } ownerTenant && ownerTenant != tenantId)
            throw new ArgumentException("The selected tenant owner must belong to the observed tenant.", nameof(owner));
        if (edition == Guid.Empty)
            throw new ArgumentException("An observation requires a nonempty material edition.", nameof(edition));
        TenantId = tenantId;
        Owner = owner;
        Edition = edition;
        SourceWitness = sourceWitness;
    }

    public Guid TenantId { get; }
    public AnnouncementOwner Owner { get; }
    public Guid Edition { get; }
    public AnnouncementSourceWitness SourceWitness { get; }
}

public sealed record AnnouncementResolution
{
    public AnnouncementResolution(AnnouncementObservation? observation, AnnouncementMaterial? material, bool isVisible)
    {
        if ((observation is null) != (material is null))
            throw new ArgumentException("Selected material and its observation must be present together.", nameof(material));
        if (isVisible && observation is null)
            throw new ArgumentException("An absent publication cannot be visible.", nameof(isVisible));
        Observation = observation;
        Material = material;
        IsVisible = isVisible;
    }

    public AnnouncementObservation? Observation { get; }
    public AnnouncementMaterial? Material { get; }
    public bool IsVisible { get; }
}

public sealed record AnnouncementDismissalMarker
{
    public AnnouncementDismissalMarker(AnnouncementObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        Observation = observation;
    }

    public AnnouncementObservation Observation { get; }
}

public sealed record AnnouncementWrite
{
    public AnnouncementWrite(AnnouncementGroup? group, bool isLocked)
    {
        if (group is null && isLocked)
            throw new ArgumentException("An absent source row cannot carry a lock.", nameof(isLocked));
        Group = group;
        IsLocked = isLocked;
    }

    public AnnouncementGroup? Group { get; }
    public bool IsLocked { get; }
}

public abstract class AnnouncementChange
{
    private AnnouncementChange() { }

    public sealed class Publish : AnnouncementChange
    {
        public Publish(AnnouncementMaterial material, bool enabled, bool redisplay)
        {
            ArgumentNullException.ThrowIfNull(material);
            Material = material;
            Enabled = enabled;
            Redisplay = redisplay;
        }

        public AnnouncementMaterial Material { get; }
        public bool Enabled { get; }
        public bool Redisplay { get; }
    }

    public sealed class SetEnabled(bool enabled) : AnnouncementChange
    {
        public bool Enabled { get; } = enabled;
    }

    public sealed class SetLock(bool isLocked) : AnnouncementChange
    {
        public bool IsLocked { get; } = isLocked;
    }

    public sealed class RemoveOverride : AnnouncementChange;
}

public enum AnnouncementDismissalDecision
{
    Apply,
    AlreadyDismissed,
    SourceChanged,
    MarkerChanged,
    NotVisible
}

/// <summary>
/// Pure source transitions and comparison decisions. Supplied tokens and snapshots are data;
/// the native mutation owner must establish authority, exclusion and final admission.
/// </summary>
public static class AnnouncementPublicationPolicy
{
    public static bool RequiresMaterialEdition(AnnouncementGroup? current, AnnouncementChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change switch
        {
            AnnouncementChange.Publish publish => current?.Publication is not { } publication
                || publication.Material != publish.Material || publish.Redisplay,
            AnnouncementChange.SetEnabled { Enabled: true } when current?.Publication is null =>
                throw new ArgumentException("Enabling a source requires existing publication material.", nameof(change)),
            AnnouncementChange.SetEnabled or AnnouncementChange.SetLock or AnnouncementChange.RemoveOverride => false,
            _ => throw new ArgumentException("A closed announcement change is required.", nameof(change))
        };
    }

    public static bool RequiresSelectionEpoch(
        AnnouncementGroup? current, bool currentLocked, AnnouncementChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (current is null && currentLocked)
            throw new ArgumentException("An absent source row cannot carry a lock.", nameof(currentLocked));
        return change switch
        {
            AnnouncementChange.Publish publish => RequiresMaterialEdition(current, change)
                || current?.Enabled != publish.Enabled,
            AnnouncementChange.SetEnabled { Enabled: true } when current?.Publication is null =>
                throw new ArgumentException("Enabling a source requires existing publication material.", nameof(change)),
            AnnouncementChange.SetEnabled enabled => (current?.Enabled ?? false) != enabled.Enabled,
            AnnouncementChange.SetLock locked => currentLocked != locked.IsLocked,
            AnnouncementChange.RemoveOverride => current?.Publication is not null,
            _ => throw new ArgumentException("A closed announcement change is required.", nameof(change))
        };
    }

    public static AnnouncementWrite Apply(
        AnnouncementOwner owner, AnnouncementGroup? current, bool currentLocked,
        AnnouncementChange change, Guid? allocatedEdition, Guid? allocatedSelectionEpoch)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(change);
        if (change is AnnouncementChange.RemoveOverride && owner.TenantId is null)
            throw new ArgumentException("Only a tenant owner can remove its override.", nameof(change));
        bool editionRequired = RequiresMaterialEdition(current, change);
        bool epochRequired = RequiresSelectionEpoch(current, currentLocked, change);
        RequireToken(allocatedEdition, editionRequired, current?.Publication?.Edition, nameof(allocatedEdition));
        RequireToken(allocatedSelectionEpoch, epochRequired, current?.SelectionEpoch, nameof(allocatedSelectionEpoch));
        if (!epochRequired)
            return new(current, currentLocked);

        Guid epoch = allocatedSelectionEpoch
            ?? throw new ArgumentException("The transition requires a selection epoch.", nameof(allocatedSelectionEpoch));
        return change switch
        {
            AnnouncementChange.Publish publish => new(new(epoch, publish.Enabled,
                new(allocatedEdition ?? current?.Publication?.Edition
                    ?? throw new ArgumentException("Publishing requires a material edition.", nameof(allocatedEdition)),
                    publish.Material)), currentLocked),
            AnnouncementChange.SetEnabled enabled => new(
                new(epoch, enabled.Enabled, current?.Publication), currentLocked),
            AnnouncementChange.SetLock locked => new(
                new(epoch, current?.Enabled ?? false, current?.Publication), locked.IsLocked),
            AnnouncementChange.RemoveOverride => new(new(epoch, false, null), currentLocked),
            _ => throw new ArgumentException("A closed announcement change is required.", nameof(change))
        };
    }

    public static AnnouncementResolution Resolve(
        Guid tenantId, AnnouncementSourceSnapshot instance, AnnouncementSourceSnapshot tenant)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(tenant);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Resolution requires an actual tenant identity.", nameof(tenantId));
        if (instance.Owner.TenantId is not null)
            throw new ArgumentException("The instance source must belong to the instance owner.", nameof(instance));
        if (tenant.Owner.TenantId != tenantId)
            throw new ArgumentException("The tenant source must belong to the selected tenant.", nameof(tenant));

        var selected = instance.IsLocked || tenant.Group?.Publication is null ? instance : tenant;
        var group = selected.Group;
        if (group is null || group.Publication is not { } publication)
            return new(null, null, false);
        var witness = new AnnouncementSourceWitness(
            instance.Group?.SelectionEpoch, instance.IsLocked, tenant.Group?.SelectionEpoch, tenant.IsLocked);
        return new(new(tenantId, selected.Owner, publication.Edition, witness), publication.Material, group.Enabled);
    }

    public static AnnouncementDismissalDecision DecideDismissal(
        AnnouncementObservation observed, AnnouncementResolution current,
        AnnouncementDismissalMarker? expectedMarker, AnnouncementDismissalMarker? currentMarker)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(current);
        if (!current.IsVisible || current.Observation is null)
            return AnnouncementDismissalDecision.NotVisible;
        if (observed != current.Observation)
            return AnnouncementDismissalDecision.SourceChanged;
        if (expectedMarker is not null && expectedMarker.Observation.TenantId != observed.TenantId)
            throw new ArgumentException("The expected marker must belong to the observed tenant.", nameof(expectedMarker));
        if (currentMarker is not null && currentMarker.Observation.TenantId != observed.TenantId)
            throw new ArgumentException("The current marker must belong to the observed tenant.", nameof(currentMarker));

        if (currentMarker is { Observation: var dismissed }
            && dismissed.TenantId == observed.TenantId && dismissed.Owner == observed.Owner
            && dismissed.Edition == observed.Edition)
            return AnnouncementDismissalDecision.AlreadyDismissed;
        return currentMarker == expectedMarker
            ? AnnouncementDismissalDecision.Apply : AnnouncementDismissalDecision.MarkerChanged;
    }

    private static void RequireToken(Guid? allocated, bool required, Guid? previous, string parameterName)
    {
        if (required)
        {
            if (allocated is null || allocated == Guid.Empty || allocated == previous)
                throw new ArgumentException("A required token must be nonempty and replace its previous identity.", parameterName);
        }
        else if (allocated is not null)
            throw new ArgumentException("This transition does not permit an allocated token.", parameterName);
    }
}
