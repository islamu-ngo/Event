using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Infrastructure.SupportAccess;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>Owns the original human or anonymous bearer through acquisition and final disclosure.</summary>
public sealed class RegistrationPrivateReader(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenant,
    TimeProvider clock,
    Func<IAuthorizationProvider> resolveAuthorization,
    Func<IHumanPrincipalSourceFence> resolvePrincipal,
    Func<IHumanAuthorizationRouteSource> resolveRoute,
    Func<IHumanEventAssignmentSourceFactory> resolveAssignments,
    Func<IHumanSupportAccessSourceFactory> resolveSupport,
    Func<IPolicySourceSetFence> resolveExclusion,
    Func<ISerializableAttemptOwner> resolveAttempt,
    Func<IHumanActionCommitFinalizer> resolveFinalizer,
    Func<IRegistrationPrivateReadRepository> resolveRepository)
    : IRegistrationPrivateReader, IRegistrationPrivateReadCustody
{
    private RegistrationPrivateRead? active;
    private RegistrationPrivateReadAdmission? admission;
    private Action? requireCustody;

    public RegistrationPrivateReadAdmission RequireActive(RegistrationPrivateRead request)
    {
        if (!ReferenceEquals(active, request) || admission is null || requireCustody is null)
            throw new UnauthorizedAccessException("The registration read has no original admitted owner.");
        requireCustody();
        return admission;
    }

    public async Task<RegistrationPrivateGraph?> ReadAsync(
        RegistrationPrivateRead request, string? capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind))
            throw new ArgumentOutOfRangeException(nameof(request));
        if (request.Kind == RegistrationPrivateReadKind.GuestRecovery)
            throw new UnauthorizedAccessException("Guest recovery requires the protected original challenge's exact digest.");
        if (machine.IsMachineCaller || tenant.TenantId == Guid.Empty || request.EventId == Guid.Empty)
            return null;
        var http = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("An original registration request is required.");
        var principal = http.User;
        Guid tenantId = tenant.TenantId;
        string? hash = string.IsNullOrWhiteSpace(capability) ? null
            : Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(capability)));
        void Custody()
        {
            if (machine.IsMachineCaller || !ReferenceEquals(contexts.HttpContext, http)
                || !ReferenceEquals(http.User, principal) || tenant.TenantId != tenantId)
                throw new UnauthorizedAccessException("Original registration read custody changed.");
        }

        RegistrationPrivateGraph? result;
        if (principal.Identities.All(identity => !identity.IsAuthenticated))
        {
            result = await resolveAttempt().ExecuteAsync(
                token => ReadOwnedAsync(null, false, Custody, token), cancellationToken);
        }
        else
        {
            await humanScope.PrepareAsync(cancellationToken);
            var subject = humanScope.Subject;
            var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken);
            if (subject is null || lifetime is null || subject.AccountKey != lifetime.AccountKey)
                return null;
            var source = resolvePrincipal();
            var routeSource = resolveRoute();
            var assignmentsSource = resolveAssignments().Create(tenantId, [request.EventId]);
            var supportSource = await resolveSupport().CreateAsync(cancellationToken);
            var exclusionSource = resolveExclusion();
            var original = await source.ObserveAsync(cancellationToken);
            var route = await routeSource.ObserveAsync(cancellationToken);
            var assignments = await assignmentsSource.ObserveAsync(cancellationToken);
            var support = await supportSource.ObserveAsync(cancellationToken);
            void HumanCustody()
            {
                Custody();
                if (!ReferenceEquals(humanScope.Subject, subject) || !lifetime.IsValidAt(clock.GetUtcNow()))
                    throw new UnauthorizedAccessException("Original registration credential custody ended.");
            }
            void Validate(HumanPrincipalSourceFacts current)
            {
                HumanCustody();
                HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, current);
                if (!Same(original, current) || current.SelectedTenantId != tenantId)
                    throw new UnauthorizedAccessException("Original registration account sources changed.");
            }
            Validate(original);
            var facts = HumanEventAssignmentProjection.Resource(assignments, request.EventId);
            var question = facts is null ? null : new AuthorizationRequest(
                AuthorizationCapabilityCatalog.Require(ResourceKinds.Event, AuthorizationActions.Events.ManageAttendees),
                request.EventId.ToString("D"), Scope: new AuthorizationScope(TenantId: tenantId.ToString("D")), Facts: facts);
            bool canManage = request.Kind == RegistrationPrivateReadKind.Readiness && question is not null
                && (await resolveAuthorization().AuthorizeAsync(question, cancellationToken)).IsAllowed;
            void ValidateTime(HumanSupportAccessSourceFacts current, DateTimeOffset now)
            {
                HumanCustody();
                if (!Same(support, current)
                    || current.Forwarding.WasForwarded && !HumanSupportAccessProjection.Context(current, now).IsActive
                    || question is not null && HumanSupportAccessProjection.BoundaryDenialReason(
                        current, now, question.ResourceKind, question.Action,
                        AuthorizationFactAttributeProjection.ToAttributes(question.Facts)) is not null)
                    throw new UnauthorizedAccessException("Registration read support custody ended.");
            }
            result = await resolveAttempt().ExecuteAsync(async token =>
            {
                var exclusion = await exclusionSource.AcquireAsync(token);
                var held = await source.AcquireAsync(exclusion, token);
                var heldRoute = await routeSource.AcquireAsync(exclusion, token);
                var heldAssignments = await assignmentsSource.AcquireAsync(exclusion, token);
                var heldSupport = await supportSource.AcquireAsync(exclusion, held, heldAssignments, token);
                Validate(held.Facts);
                RequirePolicy(heldRoute.Facts, heldAssignments.Facts);
                ValidateTime(heldSupport.Facts, clock.GetUtcNow());
                HumanSupportAccessSourceFacts? finalSupport = null;
                resolveFinalizer().Register(async finalToken =>
                {
                    await exclusion.ValidateAsync(finalToken);
                    Validate(await held.ValidateAsync(finalToken));
                    RequirePolicy(await heldRoute.ValidateAsync(finalToken), await heldAssignments.ValidateAsync(finalToken));
                    finalSupport = await heldSupport.ValidateAsync(finalToken);
                }, now => ValidateTime(finalSupport
                    ?? throw new UnauthorizedAccessException("Registration final support sources are missing."), now));
                return await ReadOwnedAsync(subject.PrincipalId, canManage, HumanCustody, token);
            }, cancellationToken);
            HumanCustody();

            void RequirePolicy(HumanAuthorizationRouteFacts currentRoute, HumanEventAssignmentSourceFacts currentAssignments)
            {
                if (!Same(route, currentRoute) || !Same(assignments, currentAssignments))
                    throw new UnauthorizedAccessException("Original registration read policy sources changed.");
            }
        }
        Custody();
        // Do not disclose an expired bearer result when the deadline passed during the read.
        if (result is not null && result.BearerExpiresAt is { } deadline)
        {
            DateTime now = clock.GetUtcNow().UtcDateTime;
            if (request.Kind == RegistrationPrivateReadKind.Transfer ? deadline < now : deadline <= now)
                return null;
        }
        return result;

        async Task<RegistrationPrivateGraph?> ReadOwnedAsync(
            Guid? user, bool manage, Action validate, CancellationToken token)
        {
            if (active is not null)
                throw new UnauthorizedAccessException("A registration read cannot replace an active invocation.");
            validate();
            active = request;
            admission = new(tenantId, user, hash, manage, clock.GetUtcNow().UtcDateTime);
            requireCustody = validate;
            try
            {
                var graph = await resolveRepository().ReadAsync(request, token);
                validate();
                if (graph is not null)
                {
                    graph.PrincipalId = user;
                    graph.CanManageAttendees = manage;
                }
                return graph;
            }
            finally
            {
                active = null;
                admission = null;
                requireCustody = null;
            }
        }
    }

    private static bool Same<T>(T expected, T actual) =>
        string.Equals(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual), StringComparison.Ordinal);
}
