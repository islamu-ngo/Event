using System.Data;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Extensions;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Repositories;

internal sealed class PrivateEventManagementRepository(ExploreDbContext context, ExploreDbContextExecutionLease lease,
    Func<IPrivateEventManagementReadCustody> resolveCustody)
    : IPrivateEventManagementRepository
{
    public async Task<PrivateEventManagementGraph> ReadAsync(PrivateEventManagementRead request, CancellationToken token)
    {
        var custody = resolveCustody();
        custody.RequireActive(request);
        lease.RequireOriginalOwner(context);
        var human = lease.HumanScope ?? throw new UnauthorizedAccessException("Original private read human required.");
        var lifetime = lease.HumanLifetime ?? throw new UnauthorizedAccessException("Original private read credential required.");
        human.RequireOwner(context);
        if (human.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
            || human.Subject.AccountKey != lifetime.AccountKey || human.Subject.PrincipalId == Guid.Empty
            || context.IsConfigurationInstanceImportOwner || !context.HasHumanCommitContextLease
            || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
            || lease.SelectedTenantId is not { } tenant || tenant == Guid.Empty || human.TenantId != tenant
            || context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
            || RelationalConnectionIdentity.Capture(context).State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Private reads require the original Runtime human Serializable owner.");
        context.RequireHumanCommitAdmission();
        var transaction = context.Database.CurrentTransaction;
        var result = PostgresPrivateEventManagementContract.For(context) is { } native
            ? await native.ReadAsync(
                human.Enrollment ?? throw new UnauthorizedAccessException("Private reads cannot enroll Runtime."),
                human.Subject.PrincipalId, request, token)
            : await ReadPortableAsync(request, tenant, human.Subject.PrincipalId, token);
        lease.RequireOriginalOwner(context);
        human.RequireOwner(context);
        context.RequireHumanCommitAdmission();
        custody.RequireActive(request);
        if (!ReferenceEquals(lease.HumanScope, human) || !ReferenceEquals(lease.HumanLifetime, lifetime)
            || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
            || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction
            || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject))
            throw new UnauthorizedAccessException("The private read owner or transaction was substituted.");
        return result;
    }

    private async Task<PrivateEventManagementGraph> ReadPortableAsync(
        PrivateEventManagementRead request, Guid tenant, Guid user, CancellationToken token)
    {
        var result = new PrivateEventManagementGraph();
        Guid id = request.EventId ?? Guid.Empty;
        var events = context.Events.AsNoTrackingWithIdentityResolution().AsSplitQuery();
        switch (request.Kind)
        {
            case PrivateEventManagementReadKind.Team:
                result.Team.AddRange(await context.EventRoleAssignments.AsNoTracking()
                    .Include(a => a.User).ThenInclude(u => u.Pii).Include(a => a.Role)
                    .Where(a => a.TenantId == tenant && a.EventId == id).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.SessionAuthorizationTarget:
                result.Events.AddRange(await events.Include(e => e.Actor).Include(e => e.OrganizerActor)
                    .Include(e => e.ParticipationConfiguration).Include(e => e.EventProvenanceType)
                    .Where(e => context.EventSessions.Any(session => session.Id == request.SessionId && session.EventId == e.Id))
                    .ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.AuthorizationTarget:
                result.Events.AddRange(await events.Include(e => e.Actor).Include(e => e.OrganizerActor)
                    .Include(e => e.ParticipationConfiguration).Include(e => e.EventProvenanceType)
                    .Where(e => e.Id == id).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.Details:
            case PrivateEventManagementReadKind.Readiness:
                result.Events.AddRange(await events.IncludeStandardDetails().Include(e => e.AtprotoRecord)
                    .Where(e => e.Id == id).ToListAsync(token));
                if (request.Kind == PrivateEventManagementReadKind.Details)
                {
                    result.Tags.AddRange(await context.EventTags.AsNoTracking().Where(e => e.EventId == id).Select(e => e.Tag).ToListAsync(token));
                    result.Categories.AddRange(await context.EventCategories.AsNoTracking().Where(e => e.EventId == id).Select(e => e.Category).ToListAsync(token));
                    result.ModerationRecords.AddRange(await context.EventModerationRecords.AsNoTracking().Where(e => e.EventId == id).ToListAsync(token));
                }
                else result.Locations.AddRange(await context.EventLocations.AsNoTracking()
                    .Include(e => e.Location).Include(e => e.FullDetailsAudience).Where(e => e.EventId == id).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.MyEvents:
                var actorIds = context.Actors.Where(a => a.UserId == user).Select(a => a.Id)
                    .Union(context.OrganizationMembers.Where(m => m.UserId == user && m.OrganizationTenant.Organization.Actor != null)
                        .Select(m => m.OrganizationTenant.Organization.Actor!.Id))
                    .Union(context.GroupMembers.Where(m => m.UserId == user && m.GroupTenant.Group.Actor != null)
                        .Select(m => m.GroupTenant.Group.Actor!.Id));
                var owned = events.Where(e => actorIds.Contains(e.ActorId));
                result.TotalCount = await owned.CountAsync(token);
                result.Events.AddRange(await owned.IncludeStandardDetails().Include(e => e.AtprotoRecord)
                    .OrderByDescending(e => e.FirstSessionDate).ThenBy(e => e.Id)
                    .Skip(checked((request.PageNumber - 1) * request.PageSize)).Take(request.PageSize).ToListAsync(token));
                Guid[] ids = result.Events.Select(e => e.Id).ToArray();
                result.DeliveryStates.AddRange(await context.PdsSyncOutbox.AsNoTracking()
                    .Where(row => row.TenantId == tenant && row.SourceEntityType == "Event"
                        && ids.Contains(row.SourceEntityId) && row.SupersededAt == null)
                    .GroupBy(row => row.SourceEntityId).Select(group => group.OrderByDescending(row => row.CreatedAt)
                        .ThenByDescending(row => row.Id).First()).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.Program:
                result.Sessions.AddRange(await context.EventSessions.AsNoTracking().AsSplitQuery().IncludeStandardDetails()
                    .Where(e => e.EventId == id).ToListAsync(token));
                result.SessionGroups.AddRange(await context.EventSessionGroups.AsNoTracking().Include(e => e.Location).Include(e => e.Room)
                    .Where(e => e.EventId == id).ToListAsync(token));
                result.Days.AddRange(await context.EventDays.AsNoTracking().Where(e => e.EventId == id).ToListAsync(token));
                result.AgendaItems.AddRange(await context.EventAgendaItems.AsNoTracking().Where(e => e.EventId == id).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.CustomProperties:
                result.Definitions.AddRange(await context.EventCustomPropertyDefinitions.AsNoTracking().Include(e => e.Options)
                    .Where(e => e.EventId == id).ToListAsync(token));
                result.Values.AddRange(await context.EventCustomPropertyValues.AsNoTracking().Where(e => e.EventId == id).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.Registrations:
                result.Registrations.AddRange(await context.RegistrationOrders.AsNoTracking().Include(e => e.Lines)
                    .Include(e => e.PlatformContribution).Where(e => e.EventId == id && e.TenantId == tenant).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.Templates:
                result.Templates.AddRange(await context.EventTemplates.AsNoTracking().Include(e => e.Definitions)
                    .Where(e => e.TenantId == tenant && (!request.EventTypeId.HasValue || e.EventTypeId == request.EventTypeId))
                    .ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.SessionTemplates:
                result.SessionTemplates.AddRange(await context.EventSessionTemplates.AsNoTracking().Include(e => e.Definitions)
                    .Where(e => e.TenantId == tenant).ToListAsync(token));
                break;
            case PrivateEventManagementReadKind.EventSyncHistory:
            case PrivateEventManagementReadKind.SessionSyncHistory:
            case PrivateEventManagementReadKind.SessionSyncContext:
                string type = request.Kind == PrivateEventManagementReadKind.EventSyncHistory ? nameof(Event) : nameof(EventSession);
                if (request.Kind == PrivateEventManagementReadKind.SessionSyncContext)
                {
                    result.Sessions.AddRange(await context.EventSessions.AsNoTracking()
                        .Where(e => e.EventId == id && e.Id == request.SessionId).ToListAsync(token));
                    if (result.Sessions.Count == 0) break;
                }
                if (request.Kind == PrivateEventManagementReadKind.SessionSyncHistory
                    && !await context.EventSessions.AnyAsync(e => e.EventId == id && e.Id == request.SessionId, token))
                    throw new UnauthorizedAccessException("The session must belong to the original event.");
                string target = (request.Kind == PrivateEventManagementReadKind.EventSyncHistory ? id : request.SessionId!.Value).ToString();
                result.History.AddRange(await context.AuditLogs.AsNoTracking()
                    .Where(e => e.EntityType == type && e.EntityId == target && e.Action == "TemplateSyncApplied").ToListAsync(token));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(request));
        }
        return result;
    }
}
