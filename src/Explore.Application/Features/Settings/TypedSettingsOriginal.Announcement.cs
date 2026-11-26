using System.Text.Json.Serialization;
using Explore.Application.Authorization;
using Explore.Application.Features.Announcements;
using Explore.Application.Features.Announcements.Requests.Commands;
using Explore.Domain.Settings;
using ISLAMU.Wire.Contracts.Announcements;

namespace Explore.Application.Features.Settings;

public sealed partial class TypedSettingsOriginal
{
    internal TypedSettingsOriginal(UpdateInstanceAnnouncementCommand command) : this(new AnnouncementSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateTenantAnnouncementCommand command) : this(new AnnouncementSettingsOriginal(command)) { }
    internal TypedSettingsOriginal(UpdateManagedTenantAnnouncementCommand command) : this(new AnnouncementSettingsOriginal(command)) { }

    private TypedSettingsOriginal(AnnouncementSettingsOriginal announcement)
    {
        Announcement = announcement;
        Actor = announcement.Actor;
        TenantId = announcement.Owner.TenantId;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public AnnouncementSettingsOriginal? Announcement { get; }

    [JsonIgnore]
    public AnnouncementUpdate? OnboardingAnnouncement { get; }
}

/// <summary>A closed capture of the actual command; the serialized input is data, never permission.</summary>
public sealed class AnnouncementSettingsOriginal
{
    internal AnnouncementSettingsOriginal(object original)
    {
        (Command, Actor, Owner, Update, Producer) = original switch
        {
            UpdateInstanceAnnouncementCommand command => ((ISecureRequest)command, command.ActorUserId,
                AnnouncementOwner.Instance, command.Update, "announcement-instance"),
            UpdateTenantAnnouncementCommand command => ((ISecureRequest)command, command.ActorUserId,
                AnnouncementOwner.ForTenant(command.TenantId), command.Update, "announcement-tenant"),
            UpdateManagedTenantAnnouncementCommand command => ((ISecureRequest)command, command.ActorUserId,
                AnnouncementOwner.ForTenant(command.TenantId), command.Update, "announcement-managed-tenant"),
            _ => throw new UnauthorizedAccessException("An exact announcement command is required.")
        };
        ArgumentNullException.ThrowIfNull(Update);
        ArgumentNullException.ThrowIfNull(Update.Change);
        ArgumentNullException.ThrowIfNull(Update.Expected);
        if (Actor == Guid.Empty || Update.Expected.Owner != Owner)
            throw new ArgumentException("The announcement actor and expected source must match the original target.");
        if (Owner.TenantId is null && Update.Change is AnnouncementChange.RemoveOverride)
            throw new ArgumentException("An instance publication cannot remove a tenant override.");
        Input = AnnouncementTransportMapper.ToDto(Update);
    }

    [JsonIgnore]
    public ISecureRequest Command { get; }
    [JsonIgnore]
    public Guid Actor { get; }
    [JsonIgnore]
    public AnnouncementOwner Owner { get; }
    [JsonIgnore]
    public AnnouncementUpdate Update { get; }
    public string Producer { get; }
    public AnnouncementUpdateDto Input { get; }
}
