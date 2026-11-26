using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Application.Features.Announcements;

/// <summary>Converts transport data to native values without establishing mutation authority.</summary>
public static class AnnouncementTransportMapper
{
    public static AnnouncementOwner ToNative(AnnouncementOwnerDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input.TenantId is { } tenant ? AnnouncementOwner.ForTenant(tenant) : AnnouncementOwner.Instance;
    }

    public static AnnouncementOwnerDto ToDto(AnnouncementOwner value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new() { TenantId = value.TenantId };
    }

    public static AnnouncementMaterial ToNative(AnnouncementMaterialDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.Message, input.LinkText, input.LinkUrl);
    }

    public static AnnouncementMaterialDto ToDto(AnnouncementMaterial value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new() { Message = value.Message, LinkText = value.LinkText, LinkUrl = value.LinkUrl };
    }

    public static AnnouncementPublication ToNative(AnnouncementPublicationDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.Edition, ToNative(input.Material));
    }

    public static AnnouncementPublicationDto ToDto(AnnouncementPublication value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new() { Edition = value.Edition, Material = ToDto(value.Material) };
    }

    public static AnnouncementGroup ToNative(AnnouncementGroupDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.SelectionEpoch, input.Enabled,
            input.Publication is null ? null : ToNative(input.Publication));
    }

    public static AnnouncementGroupDto ToDto(AnnouncementGroup value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new()
        {
            SelectionEpoch = value.SelectionEpoch,
            Enabled = value.Enabled,
            Publication = value.Publication is null ? null : ToDto(value.Publication)
        };
    }

    public static AnnouncementSourceSnapshot ToNative(AnnouncementSourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(ToNative(input.Owner), input.Group is null ? null : ToNative(input.Group), input.IsLocked);
    }

    public static AnnouncementSourceDto ToDto(AnnouncementSourceSnapshot value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new()
        {
            Owner = ToDto(value.Owner),
            Group = value.Group is null ? null : ToDto(value.Group),
            IsLocked = value.IsLocked
        };
    }

    public static AnnouncementSourceWitness ToNative(AnnouncementSourceWitnessDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.InstanceSelectionEpoch, input.InstanceIsLocked,
            input.TenantSelectionEpoch, input.TenantIsLocked);
    }

    public static AnnouncementSourceWitnessDto ToDto(AnnouncementSourceWitness value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new()
        {
            InstanceSelectionEpoch = value.InstanceSelectionEpoch,
            InstanceIsLocked = value.InstanceIsLocked,
            TenantSelectionEpoch = value.TenantSelectionEpoch,
            TenantIsLocked = value.TenantIsLocked
        };
    }

    public static AnnouncementObservation ToNative(AnnouncementObservationDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(input.TenantId, ToNative(input.Owner), input.Edition, ToNative(input.SourceWitness));
    }

    public static AnnouncementObservationDto ToDto(AnnouncementObservation value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new()
        {
            TenantId = value.TenantId,
            Owner = ToDto(value.Owner),
            Edition = value.Edition,
            SourceWitness = ToDto(value.SourceWitness)
        };
    }

    public static AnnouncementDismissalMarker ToNative(AnnouncementDismissalMarkerDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new(ToNative(input.Observation));
    }

    public static AnnouncementDismissalMarkerDto ToDto(AnnouncementDismissalMarker value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new() { Observation = ToDto(value.Observation) };
    }

    public static AnnouncementChange ToNative(AnnouncementChangeDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return input switch
        {
            { Kind: "publish", Material: not null, Enabled: not null, Redisplay: not null, IsLocked: null } =>
                new AnnouncementChange.Publish(ToNative(input.Material), input.Enabled.Value, input.Redisplay.Value),
            { Kind: "set-enabled", Material: null, Enabled: not null, Redisplay: null, IsLocked: null } =>
                new AnnouncementChange.SetEnabled(input.Enabled.Value),
            { Kind: "set-lock", Material: null, Enabled: null, Redisplay: null, IsLocked: not null } =>
                new AnnouncementChange.SetLock(input.IsLocked.Value),
            { Kind: "remove-override", Material: null, Enabled: null, Redisplay: null, IsLocked: null } =>
                new AnnouncementChange.RemoveOverride(),
            _ => throw new ArgumentException("An exact supported announcement change is required.", nameof(input))
        };
    }

    public static AnnouncementChangeDto ToDto(AnnouncementChange value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            AnnouncementChange.Publish publish => new()
            {
                Kind = "publish",
                Material = ToDto(publish.Material),
                Enabled = publish.Enabled,
                Redisplay = publish.Redisplay
            },
            AnnouncementChange.SetEnabled enabled => new() { Kind = "set-enabled", Enabled = enabled.Enabled },
            AnnouncementChange.SetLock locked => new() { Kind = "set-lock", IsLocked = locked.IsLocked },
            AnnouncementChange.RemoveOverride => new() { Kind = "remove-override" },
            _ => throw new ArgumentException("A closed announcement change is required.", nameof(value))
        };
    }

    public static AnnouncementUpdate ToNative(AnnouncementUpdateDto input, AnnouncementOwner selectedOwner)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(selectedOwner);
        var expected = ToNative(input.ExpectedSource);
        if (expected.Owner != selectedOwner)
            throw new ArgumentException("The expected source must belong to the selected owner.", nameof(input));
        var change = ToNative(input.Change);
        if (change is AnnouncementChange.RemoveOverride && selectedOwner.TenantId is null)
            throw new ArgumentException("Only a tenant owner can remove its override.", nameof(input));
        return new(change, expected);
    }

    public static AnnouncementUpdateDto ToDto(AnnouncementUpdate value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new() { Change = ToDto(value.Change), ExpectedSource = ToDto(value.Expected) };
    }

    public static (AnnouncementObservation Observed, AnnouncementDismissalMarker? ExpectedMarker) ToNative(
        AnnouncementDismissRequestDto input, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("A selected tenant identity is required.", nameof(tenantId));
        var observed = ToNative(input.Observed);
        var expected = input.ExpectedMarker is null ? null : ToNative(input.ExpectedMarker);
        if (observed.TenantId != tenantId || expected is not null && expected.Observation.TenantId != tenantId)
            throw new ArgumentException("Dismissal observations must belong to the selected tenant.", nameof(input));
        return (observed, expected);
    }

    public static AnnouncementPublicStateDto ToDto(AnnouncementResolution value, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (tenantId == Guid.Empty || value.Observation is not null && value.Observation.TenantId != tenantId)
            throw new ArgumentException("The public observation must belong to the selected tenant.", nameof(tenantId));
        return new()
        {
            TenantId = tenantId,
            Observation = value.Observation is null ? null : ToDto(value.Observation),
            Material = value.Material is null ? null : ToDto(value.Material),
            IsVisible = value.IsVisible
        };
    }

    public static AnnouncementSelfStateDto ToSelfStateDto(Guid tenantId,
        AnnouncementObservation? currentObservation, AnnouncementDismissalMarker? marker)
    {
        if (tenantId == Guid.Empty || currentObservation is not null && currentObservation.TenantId != tenantId
            || marker is not null && marker.Observation.TenantId != tenantId)
            throw new ArgumentException("Private announcement state must belong to the selected tenant.", nameof(tenantId));
        return new()
        {
            TenantId = tenantId,
            CurrentObservation = currentObservation is null ? null : ToDto(currentObservation),
            Marker = marker is null ? null : ToDto(marker)
        };
    }

    public static AnnouncementDismissalDecisionDto ToDto(AnnouncementDismissalDecision value) => value switch
    {
        AnnouncementDismissalDecision.Apply => AnnouncementDismissalDecisionDto.Apply,
        AnnouncementDismissalDecision.AlreadyDismissed => AnnouncementDismissalDecisionDto.AlreadyDismissed,
        AnnouncementDismissalDecision.SourceChanged => AnnouncementDismissalDecisionDto.SourceChanged,
        AnnouncementDismissalDecision.MarkerChanged => AnnouncementDismissalDecisionDto.MarkerChanged,
        AnnouncementDismissalDecision.NotVisible => AnnouncementDismissalDecisionDto.NotVisible,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
