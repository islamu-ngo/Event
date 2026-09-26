using Explore.Domain.Constants;
using Explore.Domain.Enums;

namespace Explore.Persistence.Seed;

public sealed record AgentBrowserPersona(Guid SubjectId, Guid? OperationId, string Email, string Name, RoleEnum TenantRole);

/// <summary>Non-secret identities reserved for the isolated, opt-in development fixture.</summary>
public static class AgentBrowserPersonaCatalog
{
    public static Guid Id(int suffix) => Guid.Parse($"01998880-0000-7000-8000-{suffix:D12}");
    public static readonly Guid TenantId = PlatformDefaults.DefaultTenantId;
    public static readonly Guid NegativeTenantId = Id(301);
    public static readonly Guid NegativeEventId = Id(302);
    public static readonly Guid EventId = Id(303);
    public static readonly Guid OrganizationId = Id(304);
    public static readonly Guid OrganizationActorId = Id(305);
    public static readonly Guid NegativeOrganizationId = Id(306);
    public static readonly Guid NegativeOrganizationActorId = Id(307);
    public static readonly Guid OrganizationTenantId = Id(308);
    public static readonly Guid NegativeOrganizationTenantId = Id(309);
    public static readonly AgentBrowserPersona Administrator = new(Id(101), null, "admin@agent.example.test", "Administrator", RoleEnum.TenantMember);
    public static readonly AgentBrowserPersona TenantAdministrator = new(Id(102), Id(202), "tenant-admin@agent.example.test", "Tenant administrator", RoleEnum.TenantAdmin);
    public static readonly AgentBrowserPersona Organizer = new(Id(103), Id(203), "organizer@agent.example.test", "Organizer", RoleEnum.TenantMember);
    public static readonly AgentBrowserPersona Manager = new(Id(104), Id(204), "manager@agent.example.test", "Manager", RoleEnum.TenantMember);
    public static readonly AgentBrowserPersona Attendee = new(Id(105), Id(205), "user@agent.example.test", "Attendee", RoleEnum.TenantMember);
    public static readonly AgentBrowserPersona Moderator = new(Id(106), Id(206), "moderator@agent.example.test", "Moderator", RoleEnum.TenantModerator);
    public static IReadOnlyList<AgentBrowserPersona> All { get; } = Array.AsReadOnly(new[]
    {
        Administrator, TenantAdministrator, Organizer, Manager, Attendee, Moderator
    });
}
