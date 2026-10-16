using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>Projects current native human sources without ordinary private-table reads or a new owner.</summary>
public sealed class HumanCerbosPrincipalReader(
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor contexts,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    IMachinePrincipalAccessor machine,
    ITenantContext tenantContext,
    TimeProvider clock,
    Func<IHumanPrincipalSourceFence> resolvePrincipal) : IHumanCerbosPrincipalReader
{
    public async Task<HumanPrincipalSourceFacts> ReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (machine.IsMachineCaller || userId == Guid.Empty)
            throw new UnauthorizedAccessException("An original human is required for human principal inputs.");
        await humanScope.PrepareAsync(cancellationToken);
        var subject = humanScope.Subject
            ?? throw new UnauthorizedAccessException("The original resolved human is required.");
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("The original authenticated request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Authentic credential lifetime is required.");
        Guid selectedTenant = tenantContext.TenantId;
        if (subject.PrincipalId != userId || subject.AccountKey != lifetime.AccountKey)
            throw new UnauthorizedAccessException("Principal inputs belong to the original authenticated account.");

        void RequireCustody()
        {
            if (machine.IsMachineCaller || !ReferenceEquals(humanScope.Subject, subject)
                || !ReferenceEquals(contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || tenantContext.TenantId != selectedTenant || !lifetime.IsValidAt(clock.GetUtcNow()))
                throw new UnauthorizedAccessException("Original human principal read custody changed.");
        }

        RequireCustody();
        // The source captures the actual selected physical owner, including a legitimate
        // instance owner. Observe deliberately rejects any already-open transaction.
        var source = resolvePrincipal();
        var facts = await source.ObserveAsync(cancellationToken);
        RequireCustody();
        HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
        if (facts.PrincipalId != userId)
            throw new UnauthorizedAccessException("Principal sources belong to another human.");
        return facts;
    }
}
