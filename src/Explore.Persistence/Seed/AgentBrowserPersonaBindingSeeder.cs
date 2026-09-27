using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Services.Scheduling;
using Explore.Domain.Settings.Documents;
using Explore.Domain.Settings.Documents.Payloads;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Identity;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using static Explore.Persistence.Seed.AgentBrowserPersonaCatalog;

namespace Explore.Persistence.Seed;

/// <summary>Atomic fixture graphs; existing exact bindings are grant-write finality, not repair requests.</summary>
public sealed class AgentBrowserPersonaBindingSeeder(ExploreDbContext database, TimeProvider timeProvider)
{
    public async Task EnsureOwnershipAsync(InstanceBootstrapState? marker, CancellationToken token)
    {
        bool restore = !database.IsTenantFilterBypassed;
        database.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        try
        {
            if (await database.TenantSettingOverrides.AsNoTracking().AnyAsync(row =>
                    row.SettingKey == GovernanceSettingKeys.Cerbos.CustomEndpoint
                    || row.SettingKey == GovernanceSettingKeys.Cerbos.CustomAdminEndpoint
                    || row.SettingKey == GovernanceSettingKeys.Cerbos.Mode
                    || row.SettingKey == GovernanceSettingKeys.Cerbos.GrpcEndpoint, token))
                throw Failure("tenant_pdp_configuration");
            if (marker is null)
            {
                if (await database.Tenants.AsNoTracking().AnyAsync(token)
                    || await database.Users.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(token)
                    || await database.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(token)
                    || await database.Organizations.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(token)
                    || await database.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(token)
                    || await database.Set<LocalIdentityUser>().AnyAsync(token)
                    || await database.Set<LocalIdentityCredentialOperation>().AnyAsync(token)
                    || await database.PlatformUserRoles.AnyAsync(token)
                    || await database.TenantUserRoleGrants.AnyAsync(token)
                    || await database.EventRoleAssignments.AnyAsync(token))
                    throw Failure("foreign_database");
                return;
            }

            if (await database.PlatformUserRoles.AnyAsync(row => row.UserId != Administrator.SubjectId || row.RoleId != (int)RoleEnum.Admin, token)
                || await database.TenantUserRoleGrants.AnyAsync(row =>
                    (row.RoleId == (int)RoleEnum.TenantAdmin && row.TenantUser.UserId != TenantAdministrator.SubjectId)
                    || (row.RoleId == (int)RoleEnum.TenantModerator && row.TenantUser.UserId != Moderator.SubjectId)
                    || ((row.RoleId == (int)RoleEnum.TenantAdmin || row.RoleId == (int)RoleEnum.TenantModerator) && row.TenantId != TenantId), token)
                || await database.OrganizationMembers.AnyAsync(row => row.RoleId == (int)RoleEnum.OrgAdmin
                    && (row.UserId != Organizer.SubjectId || row.OrganizationTenantId != OrganizationTenantId), token)
                || await database.EventRoleAssignments.AnyAsync(row =>
                    (row.UserId == Organizer.SubjectId || row.UserId == Manager.SubjectId)
                    && (row.EventId != EventId || row.TenantId != TenantId), token))
                throw Failure("foreign_privilege");
            Guid[] subjects = All.Select(persona => persona.SubjectId).ToArray();
            string[] emails = All.Select(persona => persona.Email).ToArray();
            if (await database.Users.AsNoTracking().AnyAsync(row => !subjects.Contains(row.Id)
                    && emails.Any(email => EF.Functions.ILike(row.Pii.Email, email)), token)
                || await database.Set<LocalIdentityUser>().AsNoTracking().AnyAsync(row => !subjects.Contains(row.Id)
                    && row.Email != null && emails.Any(email => EF.Functions.ILike(row.Email, email)), token))
                throw Failure("profile_collision");
            if (marker.Status != InstanceBootstrapStatus.Completed
                && (await database.Users.AsNoTracking().AnyAsync(row => !subjects.Contains(row.Id), token)
                    || await database.Set<LocalIdentityUser>().AsNoTracking().AnyAsync(row => !subjects.Contains(row.Id), token)
                    || await database.Tenants.AnyAsync(row => row.Id != TenantId && row.Id != NegativeTenantId, token)
                    || await database.Organizations.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.Id != OrganizationId && row.Id != NegativeOrganizationId, token)
                    || await database.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.Id != EventId && row.Id != NegativeEventId, token)
                    || await database.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row =>
                        row.Id != OrganizationActorId && row.Id != NegativeOrganizationActorId
                        && (row.UserId == null || !subjects.Contains(row.UserId.Value)), token)))
                throw Failure("foreign_identity");
            await ValidateFoundationAsync(marker.Status == InstanceBootstrapStatus.Completed, token);
        }
        finally { if (restore) database.ClearTenantFilterBypass(); }
    }

    public async Task SeedFoundationAsync(InstanceBootstrapStatus markerStatus, CancellationToken token)
    {
        bool restore = !database.IsTenantFilterBypassed;
        database.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        try
        {
            await ExecuteRetryableAsync(async () =>
            {
                if (await ValidateFoundationAsync(markerStatus == InstanceBootstrapStatus.Completed, token)) return;
                await using var transaction = await database.Database.BeginTransactionAsync(token);
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                SystemSetting? baseDomain = await database.Set<SystemSetting>().SingleOrDefaultAsync(
                    row => row.SettingKey == GovernanceSettingKeys.Domains.InstanceBaseDomain, token);
                if (baseDomain is null
                    || baseDomain.Id != SeedIds.SystemSettingDomainsInstanceBaseDomainId
                    || baseDomain.Value != "\"\""
                    || await database.Set<SystemSetting>().AnyAsync(
                        row => row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled, token))
                    throw Failure("routing_conflict");
                baseDomain.Value = "\"localhost\"";
                baseDomain.UpdatedAt = now;
                database.Set<SystemSetting>().Add(new SystemSetting
                {
                    Id = Id(324),
                    SettingKey = GovernanceSettingKeys.Routing.ResolverSubdomainEnabled,
                    Value = "true",
                    ValueType = SettingValueType.Boolean,
                    IsLocked = false,
                    Description = "Enable agent fixture tenant hosts under localhost",
                    Category = "Routing",
                    DisplayOrder = 22,
                    CreatedAt = now
                });
                await CreateTenantAsync(TenantId, "default", "Agent browser", Id(320), Id(321), now, token);
                await CreateTenantAsync(NegativeTenantId, "agent-negative", "Agent negative control", Id(322), Id(323), now, token);
                await database.SaveChangesAsync(token);
                database.TenantSettingOverrides.AddRange(
                    new TenantSetting
                    {
                        Id = Id(325), TenantId = TenantId, Tenant = null!,
                        SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain,
                        Value = "\"default\"", CreatedAt = now
                    },
                    new TenantSetting
                    {
                        Id = Id(326), TenantId = NegativeTenantId, Tenant = null!,
                        SettingKey = GovernanceSettingKeys.Domains.TenantSubdomain,
                        Value = "\"agent-negative\"", CreatedAt = now
                    });
                database.Organizations.AddRange(
                    Organization(OrganizationId, "Agent organizer", now),
                    Organization(NegativeOrganizationId, "Agent negative organizer", now));
                await database.SaveChangesAsync(token);
                database.Actors.AddRange(
                    OrganizationActor(OrganizationActorId, OrganizationId, "Agent organizer", now),
                    OrganizationActor(NegativeOrganizationActorId, NegativeOrganizationId, "Agent negative organizer", now));
                await database.SaveChangesAsync(token);
                database.Set<OrganizationTenant>().AddRange(
                    Participation(OrganizationTenantId, TenantId, OrganizationId, now),
                    Participation(NegativeOrganizationTenantId, NegativeTenantId, NegativeOrganizationId, now));
                var positive = Event(EventId, TenantId, OrganizationActorId, "Agent browser event", "agent-browser", now);
                var negative = Event(NegativeEventId, NegativeTenantId, NegativeOrganizationActorId, "Agent negative event", "agent-negative", now);
                database.Events.AddRange(positive, negative);
                await database.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            });
        }
        finally { if (restore) database.ClearTenantFilterBypass(); }
    }

    public async Task BindAsync(AgentBrowserPersona persona, LocalCredentialProvisioningSnapshot snapshot, CancellationToken token)
    {
        LocalCredentialOperationReceipt receipt = snapshot.Receipt;
        if (receipt.LocalSubjectId != persona.SubjectId || receipt.OperationId != persona.OperationId
            || receipt.Kind != LocalCredentialOperationKind.Create
            || receipt.InitiatingApplicationUserId != Administrator.SubjectId)
            throw Failure("receipt_mismatch");
        bool restore = !database.IsTenantFilterBypassed;
        database.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        try
        {
            await ExecuteRetryableAsync(async () =>
            {
                if (await HasExactGraphAsync(receipt, token))
                {
                    await RequireMembershipAsync(persona, receipt, token);
                    return;
                }
                if (receipt.Stage != LocalCredentialOperationStage.ProvisioningPending
                    || await database.Users.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.Id == persona.SubjectId, token)
                    || await database.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.Id == receipt.PersonalActorId || row.UserId == persona.SubjectId, token)
                    || await database.UserExternalLogins.AnyAsync(row => row.Id == receipt.ExternalLoginId || row.UserId == persona.SubjectId
                        || (row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local && row.ProviderKey == persona.SubjectId.ToString()), token))
                    throw Failure("binding_conflict");
                await using var transaction = await database.Database.BeginTransactionAsync(token);
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                database.Users.Add(new User { Id = persona.SubjectId, Pii = new UserPii { Email = snapshot.Email!, FirstName = snapshot.FirstName, LastName = snapshot.LastName }, EmailVerified = snapshot.EmailVerified, CreatedAt = now });
                await database.SaveChangesAsync(token);
                database.Actors.Add(new Actor { Id = receipt.PersonalActorId, UserId = persona.SubjectId, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!, Pii = new ActorPii { DisplayName = snapshot.FirstName }, CreatedAt = now });
                database.UserExternalLogins.Add(new UserExternalLogin { Id = receipt.ExternalLoginId, UserId = persona.SubjectId, User = null!, AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!, ProviderKey = persona.SubjectId.ToString("D"), ProviderDisplayName = "Local", CreatedAt = now });
                await database.SaveChangesAsync(token);
                int index = All.ToList().IndexOf(persona);
                Guid membership = Id(400 + index);
                database.TenantUsers.Add(new TenantUser { Id = membership, TenantId = TenantId, Tenant = null!, UserId = persona.SubjectId, User = null!, ActorId = receipt.PersonalActorId, StatusId = (int)TenantUserStatusEnum.Active, JoinedAt = now, CreatedAt = now });
                database.TenantUserRoleGrants.Add(new TenantUserRoleGrant { Id = Id(410 + index), TenantId = TenantId, Tenant = null!, TenantUserId = membership, TenantUser = null!, RoleId = (int)persona.TenantRole, Role = null!, RoleScopeId = (int)RoleScopeEnum.Tenant, GrantedAt = now, GrantedBy = Administrator.SubjectId, CreatedAt = now });
                if (persona == Organizer)
                {
                    database.OrganizationMembers.Add(new OrganizationMember { Id = Id(420), OrganizationTenantId = OrganizationTenantId, OrganizationTenant = null!, UserId = persona.SubjectId, User = null!, TenantId = TenantId, Tenant = null!, RoleId = (int)RoleEnum.OrgAdmin, Role = null!, CreatedAt = now });
                    AddEventRole(persona.SubjectId, RoleEnum.EventOwner, Id(421), now);
                }
                if (persona == Manager)
                {
                    AddEventRole(persona.SubjectId, RoleEnum.EventManager, Id(422), now);
                    AddEventRole(persona.SubjectId, RoleEnum.RegistrationManager, Id(423), now);
                }
                await database.SaveChangesAsync(token);
                await transaction.CommitAsync(token);
            });
        }
        finally { if (restore) database.ClearTenantFilterBypass(); }
    }

    private Task ExecuteRetryableAsync(Func<Task> operation)
    {
        bool attempted = false;
        return database.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempted)
                database.ChangeTracker.Clear();
            attempted = true;
            await operation();
        });
    }

    private Task<bool> HasExactGraphAsync(LocalCredentialOperationReceipt receipt, CancellationToken token) =>
        database.UserExternalLogins.AsNoTracking().AnyAsync(login => login.Id == receipt.ExternalLoginId
            && login.UserId == receipt.LocalSubjectId && login.ProviderKey == receipt.LocalSubjectId.ToString()
            && login.AuthenticationProviderId == (int)AuthenticationProviderKind.Local
            && database.Users.Any(user => user.Id == receipt.LocalSubjectId && !user.IsDeleted)
            && database.Actors.Any(actor => actor.Id == receipt.PersonalActorId && actor.UserId == receipt.LocalSubjectId
                && actor.ActorTypeId == (int)ActorTypeEnum.User && !actor.IsDeleted && !actor.IsSuspended), token);

    public async Task ValidateReceiptGraphAsync(AgentBrowserPersona persona, LocalCredentialOperationReceipt? receipt, CancellationToken token)
    {
        bool restore = !database.IsTenantFilterBypassed;
        database.EnableTenantFilterBypass(TenantFilterBypassReasons.DatabaseSeeding);
        try
        {
            if (receipt is not null && await HasExactGraphAsync(receipt, token))
            {
                if (persona != Administrator) await RequireMembershipAsync(persona, receipt, token);
                return;
            }
            if ((receipt is not null && receipt.Stage != LocalCredentialOperationStage.ProvisioningPending)
                || await database.Users.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.Id == persona.SubjectId, token)
                || await database.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).AnyAsync(row => row.UserId == persona.SubjectId, token)
                || await database.UserExternalLogins.AnyAsync(row => row.UserId == persona.SubjectId
                    || (row.AuthenticationProviderId == (int)AuthenticationProviderKind.Local && row.ProviderKey == persona.SubjectId.ToString()), token)
                || (receipt is null && await database.Set<LocalIdentityUser>().AnyAsync(row => row.Id == persona.SubjectId, token)))
                throw Failure("binding_conflict");
        }
        finally { if (restore) database.ClearTenantFilterBypass(); }
    }

    private async Task RequireMembershipAsync(AgentBrowserPersona persona, LocalCredentialOperationReceipt receipt, CancellationToken token)
    {
        Guid membership = Id(400 + All.ToList().IndexOf(persona));
        if (!await database.TenantUsers.AsNoTracking().AnyAsync(row => row.Id == membership
                && row.UserId == persona.SubjectId && row.TenantId == TenantId && row.ActorId == receipt.PersonalActorId, token))
            throw Failure("membership_incomplete");
    }

    private async Task<bool> ValidateFoundationAsync(bool required, CancellationToken token)
    {
        int tenants = await database.Tenants.CountAsync(row => row.Id == TenantId || row.Id == NegativeTenantId, token);
        int organizations = await database.Organizations.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync(row => row.Id == OrganizationId || row.Id == NegativeOrganizationId, token);
        int actors = await database.Actors.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync(row => row.Id == OrganizationActorId || row.Id == NegativeOrganizationActorId, token);
        int events = await database.Events.IgnoreQueryFilters([QueryFilterNames.SoftDelete]).CountAsync(row => row.Id == EventId || row.Id == NegativeEventId, token);
        int participations = await database.Set<OrganizationTenant>().CountAsync(row => row.Id == OrganizationTenantId || row.Id == NegativeOrganizationTenantId, token);
        Guid[] documentIds = [Id(320), Id(321), Id(322), Id(323)];
        int documents = await database.TenantSettingsDocuments.CountAsync(row => documentIds.Contains(row.Id), token);
        if (!required && tenants + organizations + actors + events + participations + documents == 0) return false;
        if (tenants != 2 || organizations != 2 || actors != 2 || events != 2 || participations != 2 || documents != 4
            || (!required && (
                !await database.Set<SystemSetting>().AnyAsync(row =>
                    row.Id == SeedIds.SystemSettingDomainsInstanceBaseDomainId
                    && row.Value == "\"localhost\"", token)
                || !await database.Set<SystemSetting>().AnyAsync(row =>
                    row.Id == Id(324)
                    && row.SettingKey == GovernanceSettingKeys.Routing.ResolverSubdomainEnabled
                    && row.Value == "true", token)
                || !await database.TenantSettingOverrides.AnyAsync(row =>
                    row.Id == Id(325) && row.TenantId == TenantId
                    && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain
                    && row.Value == "\"default\"", token)
                || !await database.TenantSettingOverrides.AnyAsync(row =>
                    row.Id == Id(326) && row.TenantId == NegativeTenantId
                    && row.SettingKey == GovernanceSettingKeys.Domains.TenantSubdomain
                    && row.Value == "\"agent-negative\"", token)))
            || !await database.Tenants.AnyAsync(row => row.Id == TenantId && row.Slug == "default" && row.TenantStatusId == (int)TenantStatusEnum.Active, token)
            || !await database.Tenants.AnyAsync(row => row.Id == NegativeTenantId && row.Slug == "agent-negative" && row.TenantStatusId == (int)TenantStatusEnum.Active, token)
            || !await database.Actors.AnyAsync(row => row.Id == OrganizationActorId && row.OrganizationId == OrganizationId && !row.IsSuspended, token)
            || !await database.Actors.AnyAsync(row => row.Id == NegativeOrganizationActorId && row.OrganizationId == NegativeOrganizationId && !row.IsSuspended, token)
            || !await database.Events.AnyAsync(row => row.Id == EventId && row.TenantId == TenantId && row.ActorId == OrganizationActorId, token)
            || !await database.Events.AnyAsync(row => row.Id == NegativeEventId && row.TenantId == NegativeTenantId && row.ActorId == NegativeOrganizationActorId, token)
            || !await database.Set<OrganizationTenant>().AnyAsync(row => row.Id == OrganizationTenantId && row.TenantId == TenantId && row.OrganizationId == OrganizationId && row.IsOrganizerEligible, token)
            || !await database.Set<OrganizationTenant>().AnyAsync(row => row.Id == NegativeOrganizationTenantId && row.TenantId == NegativeTenantId && row.OrganizationId == NegativeOrganizationId && row.IsOrganizerEligible, token))
            throw Failure("foundation_conflict");
        return true;
    }

    private void AddEventRole(Guid user, RoleEnum role, Guid id, DateTime now)
    {
        var assignment = EventRoleAssignment.Create(TenantId, EventId, user, (int)role, EventRoleAssignmentStatus.Active, now, null, Administrator.SubjectId);
        assignment.Id = id;
        assignment.CreatedAt = now;
        database.EventRoleAssignments.Add(assignment);
    }

    private async Task CreateTenantAsync(Guid tenantId, string slug, string name, Guid brandingId, Guid identityId, DateTime now, CancellationToken token)
    {
        var branding = TenantBrandingSettingsDocumentDefaults.Create(tenantId, name);
        var identity = TenantDirectoryOperatorIdentityDocumentDefaults.Create(tenantId, new TenantDirectoryOperatorIdentitySettings
        {
            PublicName = name, LegalName = name + " synthetic fixture", OperatorKindCode = TenantDirectoryOperatorKinds.UnincorporatedAssociation,
            JurisdictionCountryCode = "BE", PublicContactEmail = "operator@agent.example.test",
            LegalNoticeUrl = "https://agent.example.test/legal", PrivacyUrl = "https://agent.example.test/privacy", TermsUrl = "https://agent.example.test/terms"
        });
        var creator = new TenantCreationService(new TenantRepository(database), new TenantSettingsDocumentRepository(database));
        await creator.CreateInCurrentTransactionAsync(new TenantCreationRequest(tenantId, name, slug, (int)TenantStatusEnum.Active, null, now,
            new TenantBrandingDocumentSeed(brandingId, branding.SchemaVersion, branding.DefaultsVersion, branding.PayloadJson),
            new TenantDirectoryOperatorIdentityDocumentSeed(identityId, identity.SchemaVersion, identity.DefaultsVersion, identity.PayloadJson)), token);
    }

    private static Organization Organization(Guid id, string name, DateTime now) => new() { Id = id, Pii = new OrganizationPii { FullName = name }, CreatedAt = now };
    private static Actor OrganizationActor(Guid id, Guid organization, string name, DateTime now) => new() { Id = id, OrganizationId = organization, ActorTypeId = (int)ActorTypeEnum.Organization, ActorType = null!, Pii = new ActorPii { DisplayName = name }, CreatedAt = now };
    private static OrganizationTenant Participation(Guid id, Guid tenant, Guid organization, DateTime now) => new() { Id = id, TenantId = tenant, Tenant = null!, OrganizationId = organization, Organization = null!, ApprovalStatusId = (int)ApprovalStatusEnum.Approved, ApprovalStatus = null!, IsVisible = true, IsOrganizerEligible = true, ApprovedAt = now, CreatedAt = now };
    private static Explore.Domain.Event Event(Guid id, Guid tenant, Guid actor, string title, string slug, DateTime now)
    {
        var entity = new Explore.Domain.Event { Id = id, TenantId = tenant, Tenant = null!, ActorId = actor, Actor = null!, OrganizerActorId = actor, Title = title, Slug = slug, PublicCode = id == EventId ? "agent0000303" : "agent0000302", EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated, VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!, EventStatus = null!, EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!, CreatedAt = now, ConcurrencyStamp = Guid.CreateVersion7() };
        DateTimeOffset start = new(now.Date.AddDays(7), TimeSpan.Zero);
        var session = new EventSession { Id = id == EventId ? Id(310) : Id(311), EventId = id, Event = entity, TenantId = tenant, Tenant = null!, Title = title, CreatedAt = now, ConcurrencyStamp = Guid.CreateVersion7() };
        session.Reschedule(UtcInstantRange.Create(start, start.AddHours(2)), "UTC", new EventScheduleProjectionCalculator());
        entity.Sessions.Add(session);
        entity.ApplyScheduleTimeZone("UTC", new EventScheduleProjectionCalculator());
        entity.Publish(now);
        session.Publish(EventStatusEnum.Published, now);
        entity.RecalculateScheduleSummaryFromSessions();
        return entity;
    }

    private static InvalidOperationException Failure(string reason) => new($"agent_browser_{reason}");
}
