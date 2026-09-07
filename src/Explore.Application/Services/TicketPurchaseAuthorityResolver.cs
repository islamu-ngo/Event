using System.Security.Cryptography;
using System.Text;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed class TicketPurchaseAuthorityResolver(
    ICurrentUserService currentUser,
    ITenantContext tenant,
    IActorRepository actors,
    IGroupMemberRepository groupMembers,
    IOrganizationMemberRepository organizationMembers,
    IRegistrationInventoryRepository inventory) :
    ITicketPurchaseAuthorityResolver
{
    public async Task<TicketPurchaseAuthorityResolution> ResolveAsync(
        TicketPurchaseAuthorityResolutionRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return request.AccessMode switch
        {
            TicketPurchaseAccessMode.AuthenticatedAccount =>
                await ResolveAuthenticatedAsync(
                    request,
                    cancellationToken),
            TicketPurchaseAccessMode.VerifiedContact =>
                await ResolveVerifiedContactAsync(
                    request,
                    cancellationToken),
            TicketPurchaseAccessMode.NameOnly =>
                TicketPurchaseAuthorityResolution.Success(
                    TicketPurchaseAuthorityDimension.NameOnly(
                        request.OrderId)),
            _ => Unavailable(),
        };
    }

    private async Task<TicketPurchaseAuthorityResolution>
        ResolveAuthenticatedAsync(
            TicketPurchaseAuthorityResolutionRequest request,
            CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated
            || currentUser.UserId is not Guid accountId)
        {
            return Unavailable();
        }

        Actor? actor = request.RequestedPurchaserActorId is Guid actorId
            ? await actors.GetPublicActorProfileByTenantAsync(
                tenant.TenantId,
                actorId,
                cancellationToken)
            : await actors.GetActorByUserIdAndTenantId(
                accountId,
                tenant.TenantId,
                cancellationToken);
        if (actor is null
            || !await CanActAsAsync(
                actor,
                accountId,
                cancellationToken))
        {
            return Unavailable();
        }

        return TicketPurchaseAuthorityResolution.Success(
            TicketPurchaseAuthorityDimension.Authenticated(
                accountId,
                actor.Id));
    }

    private async Task<TicketPurchaseAuthorityResolution>
        ResolveVerifiedContactAsync(
            TicketPurchaseAuthorityResolutionRequest request,
            CancellationToken cancellationToken)
    {
        RegistrationOrder? order =
            await inventory.GetOrderWithPiiAsync(
                request.OrderId,
                tenant.TenantId,
                cancellationToken);
        if (order is null
            || order.EventId != request.EventId
            || order.AccountUserId.HasValue
            || order.Pii is not
            {
                IsEmailVerified: true,
                NormalizedEmail: { } normalizedEmail,
            })
        {
            return Unavailable();
        }

        return TicketPurchaseAuthorityResolution.Success(
            TicketPurchaseAuthorityDimension.VerifiedContact(
                Convert.ToBase64String(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(
                            normalizedEmail)))));
    }

    private async Task<bool> CanActAsAsync(
        Actor actor,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (actor.UserId == accountId)
        {
            return true;
        }

        if (actor.GroupId is Guid groupId)
        {
            return await groupMembers.Exists(
                groupId,
                accountId);
        }

        return actor.OrganizationId is Guid organizationId
            && await organizationMembers.Exists(
                organizationId,
                accountId);
    }

    private static TicketPurchaseAuthorityResolution Unavailable() =>
        TicketPurchaseAuthorityResolution.Failure(
            TicketPurchaseFailureCodes.AuthorityUnavailable);
}
