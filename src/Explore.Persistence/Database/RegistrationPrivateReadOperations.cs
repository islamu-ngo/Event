using System.Data;
using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Waitlist;
using Explore.Application.Contracts.Admissions;
using Explore.Domain;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;

namespace Explore.Persistence.Database;

internal sealed class RegistrationPrivateReadOperations(
    ExploreDbContext context,
    ExploreDbContextExecutionLease lease,
    IConfiguration configuration,
    IRegistrationPrivateReadCustody custody) : IRegistrationPrivateReadRepository
{
    public async Task<RegistrationPrivateGraph?> ReadAsync(RegistrationPrivateRead request, CancellationToken token)
    {
        var admission = custody.RequireActive(request);
        context.ValidateTenantLease();
        if (lease.Context is null)
            lease.Bind(context, context.BindTenantLease());
        var connection = RelationalConnectionIdentity.Capture(context);
        var transaction = context.Database.CurrentTransaction
            ?? throw new UnauthorizedAccessException("Registration reads require their original transaction.");
        var human = lease.HumanScope;
        var lifetime = lease.HumanLifetime;
        void Require()
        {
            lease.RequireOriginalOwner(context);
            if (!ReferenceEquals(custody.RequireActive(request), admission)
                || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                || context.TenantFilterTenantId != admission.TenantId
                || admission.TenantId == Guid.Empty || !connection.Matches(context)
                || connection.State != ConnectionState.Open
                || !ReferenceEquals(context.Database.CurrentTransaction, transaction)
                || transaction.GetDbTransaction().IsolationLevel != IsolationLevel.Serializable
                || !connection.Owns(transaction.GetDbTransaction()))
                throw new UnauthorizedAccessException("Registration reads cannot substitute their tenant or physical owner.");
            if (admission.PrincipalId is { } user)
            {
                if (human is null || lifetime is null || !ReferenceEquals(lease.HumanScope, human)
                    || !ReferenceEquals(lease.HumanLifetime, lifetime)
                    || human.Purpose != HumanAdministratorCommitPurpose.RuntimeTenant
                    || human.Subject.PrincipalId != user || human.Subject.AccountKey != lifetime.AccountKey
                    || !ReferenceEquals(context.HumanCommitAuditSubject, human.Subject)
                    || !context.HasHumanCommitContextLease || !context.HumanCommitAttemptOwnsTransaction)
                    throw new UnauthorizedAccessException("Registration account reads require the original human.");
                human.RequireOwner(context);
                context.RequireHumanCommitAdmission();
            }
            else if (human is not null)
                throw new UnauthorizedAccessException("Anonymous reads cannot borrow a human execution owner.");
        }
        Require();
        var graph = context.Database.IsNpgsql()
            ? await PostgresRegistrationPrivateReadContract.ReadAsync(context,
                PostgresNativeQueueEnrollment.Bind(configuration), request, admission, token)
            : await ReadPortableAsync(request, admission, token);
        Require();
        if (graph is not null)
        {
            if (!AllowsAccount(request, admission, graph))
                graph.BearerExpiresAt = request.Kind == RegistrationPrivateReadKind.Transfer
                    ? graph.Transfer!.Transfer.ExpiresAt
                    : request.Kind is RegistrationPrivateReadKind.Readiness or RegistrationPrivateReadKind.PurchaseOrder
                        ? graph.Order.ExpiresAt : null;
        }
        return graph;
    }

    private static bool AllowsAccount(RegistrationPrivateRead request,
        RegistrationPrivateReadAdmission admission, RegistrationPrivateGraph graph)
    {
        if (admission.PrincipalId is not { } user || request.Kind == RegistrationPrivateReadKind.PurchaseOrder)
            return false;
        bool orderOwner = graph.Order.AccountUserId == user;
        return request.Kind switch
        {
            RegistrationPrivateReadKind.Readiness => orderOwner || admission.CanManageAttendees
                || graph.Participant?.LinkedUserId == user || graph.Eligibility?.SubjectUserId == user,
            RegistrationPrivateReadKind.Transfer => orderOwner || graph.Transfer!.SourceParticipant.LinkedUserId == user
                || graph.Transfer.Ticket.HolderSubjectUserId == user || graph.Transfer.RecipientParticipant?.LinkedUserId == user,
            _ => orderOwner
        };
    }

    private static bool Allows(RegistrationPrivateRead request, RegistrationPrivateReadAdmission admission,
        RegistrationPrivateGraph graph)
    {
        if (AllowsAccount(request, admission, graph)) return true;
        if (request.Kind == RegistrationPrivateReadKind.AccountOrder) return false;
        if (request.Kind == RegistrationPrivateReadKind.Transfer)
            return graph.Transfer!.Transfer.IsOpen && graph.Transfer.Transfer.ExpiresAt >= admission.EvaluatedAt
                && Matches(graph.Transfer.Transfer.CapabilityDigest, admission.CapabilityHash);
        if (request.Kind == RegistrationPrivateReadKind.AddOns && graph.Order.AccountUserId is not null)
            return false;
        if (request.Kind is RegistrationPrivateReadKind.Readiness or RegistrationPrivateReadKind.PurchaseOrder
            && graph.Order.ExpiresAt is { } expiry && expiry <= admission.EvaluatedAt)
            return false;
        return Matches(graph.Order.GuestAccessTokenHash?.Value, admission.CapabilityHash);
    }

    private static bool Matches(string? expected, string? actual) => expected is not null && actual is not null
        && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected), Convert.FromBase64String(actual));

    private async Task<RegistrationPrivateGraph?> ReadPortableAsync(
        RegistrationPrivateRead request, RegistrationPrivateReadAdmission admission, CancellationToken token)
    {
        Guid tenant = admission.TenantId;
        var transfers = new AdmissionTicketTransferRepository(context);
        AdmissionTicketTransferAccessContext? transfer = request.Kind == RegistrationPrivateReadKind.Transfer
            ? await transfers.GetAccessAsync(tenant, request.EventId, request.TicketId, request.TransferId, token) : null;
        RegistrationOrder? order = transfer?.Order ?? await context.RegistrationOrders.AsNoTracking()
            .Where(row => row.TenantId == tenant && row.EventId == request.EventId && row.Id == request.OrderId)
            .SingleOrDefaultAsync(token);
        if (order is null || request.Kind == RegistrationPrivateReadKind.Transfer && transfer is null) return null;
        var participant = request.Kind == RegistrationPrivateReadKind.Readiness
            ? await context.RegistrationParticipants.AsNoTracking().SingleOrDefaultAsync(row =>
                row.TenantId == tenant && row.RegistrationOrderId == order.Id && row.Id == request.ParticipantId, token) : null;
        var eligibility = request.Kind == RegistrationPrivateReadKind.Readiness
            ? await context.ParticipantAdmissionEligibilities.AsNoTracking().SingleOrDefaultAsync(row =>
                row.TenantId == tenant && row.EventId == request.EventId && row.RegistrationOrderId == order.Id
                && row.ParticipantId == request.ParticipantId && row.RegistrationTicketAssignmentId == request.AssignmentId, token) : null;
        if (request.Kind == RegistrationPrivateReadKind.Readiness && (participant is null || eligibility is null)) return null;
        var admitted = new RegistrationPrivateGraph { Order = order, Participant = participant, Eligibility = eligibility, Transfer = transfer };
        if (!Allows(request, admission, admitted)) return null;
        var addOns = new EventAddOnRepository(context);
        var waitlists = new FairReturnWaitlistRepository(context);
        FairReturnWaitlistAccessContext? waitlist = request.Kind == RegistrationPrivateReadKind.Waitlist
            ? await waitlists.GetAccessAsync(tenant, request.EventId, order.Id, request.LineId, token) : null;
        if (request.Kind == RegistrationPrivateReadKind.Waitlist && waitlist is null) return null;
        order = request.Kind switch
        {
            RegistrationPrivateReadKind.AddOns => (await addOns.GetOrderWithAddOnsAsync(tenant, request.EventId, order.Id, token))!,
            RegistrationPrivateReadKind.Waitlist => waitlist!.Order,
            RegistrationPrivateReadKind.Readiness or RegistrationPrivateReadKind.PurchaseOrder or RegistrationPrivateReadKind.AccountOrder =>
                (await new RegistrationInventoryRepository(context).GetOrderWithLinesAsync(order.Id, tenant, token))!,
            _ => order
        };
        var saleControls = new PaidCheckoutActivationRepository(context);
        return new RegistrationPrivateGraph
        {
            Order = order,
            Event = request.Kind is RegistrationPrivateReadKind.AddOns or RegistrationPrivateReadKind.Readiness
                ? await context.Events.AsNoTracking().Include(row => row.Actor).Include(row => row.OrganizerActor)
                    .SingleOrDefaultAsync(row => row.TenantId == tenant && row.Id == request.EventId, token) : null,
            AddOnCatalog = request.Kind == RegistrationPrivateReadKind.AddOns
                ? await addOns.GetPublishedCatalogAsync(tenant, request.EventId, token) : null,
            Fulfillments = request.Kind == RegistrationPrivateReadKind.AddOns
                ? await addOns.ListFulfillmentsAsync(tenant, request.EventId, order.Id, token) : [],
            Refunds = request.Kind == RegistrationPrivateReadKind.AddOns
                ? await addOns.ListRefundAllocationsAsync(tenant, request.EventId, order.Id, token) : [],
            Waitlist = waitlist,
            ReplacementSettled = waitlist?.Binding is { } binding
                && await waitlists.HasReplacementSettlementAsync(tenant, binding.Id, token),
            TenantSaleControl = waitlist is not null ? await saleControls.GetSaleControlAsync(tenant, null, false, token) : null,
            EventSaleControl = waitlist is not null ? await saleControls.GetSaleControlAsync(tenant, request.EventId, false, token) : null,
            Participant = participant,
            Eligibility = eligibility,
            Ticket = eligibility is not null ? await context.AdmissionTickets.AsNoTracking()
                .SingleOrDefaultAsync(row => row.TenantId == tenant && row.RegistrationTicketAssignmentId == request.AssignmentId, token) : null,
            Transfer = transfer
        };
    }
}
