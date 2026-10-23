using Explore.Application.Authentication;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Users.Requests.Commands;
using Explore.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

/// <summary>Retains the original human, credential and source custody through one closed preference write.</summary>
public sealed class HumanTenantPreferencePreparation(
    IServiceProvider services,
    IHumanCommitScopePreparation humanScope,
    IHttpContextAccessor httpContext,
    IHumanAuthenticationLifetimeObserver lifetimeObserver,
    TimeProvider clock) : IHumanTenantPreferencePreparation
{
    private Mutation? _current;
    private int _consumed;
    public IHumanTenantPreferenceMutation? CurrentMutation => _current;

    public async Task<bool> ExecuteAsync(UpdateUserLastActiveTenantCommand command, CancellationToken token)
    {
        if (Interlocked.Exchange(ref _consumed, 1) != 0)
            throw new InvalidOperationException("A tenant preference preparation permits one physical attempt.");
        await humanScope.PrepareAsync(token);
        var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("An original human is required.");
        var request = httpContext.HttpContext ?? throw new UnauthorizedAccessException("An original request is required.");
        var principal = request.User;
        var lifetime = await lifetimeObserver.ObserveAsync(principal, token)
            ?? throw new UnauthorizedAccessException("Original credential lifetime evidence is required.");
        var credentialCustody = services.GetRequiredService<IHumanOriginalCredentialCustody>();
        credentialCustody.RequireOriginalCredential(lifetime);
        Guid tenantId = command.TenantId;
        if (command.UserId != subject.PrincipalId || tenantId == Guid.Empty || lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("Preference effects must belong to the resolved human.");
        var sourceSet = services.GetRequiredService<IPolicySourceSetFence>();
        var principalSource = services.GetRequiredService<IHumanPrincipalSourceFence>();
        var supportSource = await services.GetRequiredService<IHumanSupportAccessSourceFactory>().CreateAsync(token);
        var authority = services.GetRequiredService<IHumanSubjectCommitAuthority>();
        var finalizer = services.GetRequiredService<IHumanActionCommitFinalizer>();
        var owner = services.GetRequiredService<ISerializableAttemptOwner>();
        var store = services.GetRequiredService<IHumanTenantPreferenceStore>();
        var observed = await principalSource.ObserveAsync(token);
        var originalSupport = await supportSource.ObserveAsync(token);

        void RequireCustody(DateTimeOffset now)
        {
            credentialCustody.RequireOriginalCredential(lifetime);
            if (!ReferenceEquals(humanScope.Subject, subject) || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, principal) || command.UserId != subject.PrincipalId
                || command.TenantId != tenantId || !lifetime.IsValidAt(now))
                throw new UnauthorizedAccessException("Original preference credential or custody changed.");
        }
        void RequirePrincipal(HumanPrincipalSourceFacts facts)
        {
            RequireCustody(clock.GetUtcNow());
            HumanPrincipalSourceValidation.RequireCurrent(subject, lifetime.LocalSession, facts);
            if (facts.SelectedTenantId != observed.SelectedTenantId)
                throw new UnauthorizedAccessException("Preference sources changed their selected owner.");
        }
        void RequireSupport(HumanSupportAccessSourceFacts facts)
        {
            if (!ReferenceEquals(facts.Forwarding.Subject, subject)
                || facts.Forwarding.WasActive || facts.Forwarding.WasForwarded
                || !originalSupport.Sessions.SequenceEqual(facts.Sessions)
                || !originalSupport.Settings.SequenceEqual(facts.Settings))
                throw new UnauthorizedAccessException("Support authority cannot substitute a self-owned preference.");
        }
        static bool HasMembership(HumanPrincipalSourceFacts facts, Guid target) =>
            facts.TenantMemberships.Count(member => member.TenantId == target && !member.IsDeleted
                && member.StatusId == (int)TenantUserStatusEnum.Active) == 1
            && facts.Tenants.Count(tenant => tenant.Id == target) == 1;
        RequirePrincipal(observed);
        RequireSupport(originalSupport);
        try
        {
            return await owner.ExecuteAsync(async cancellationToken =>
            {
                var exclusion = await sourceSet.AcquireAsync(cancellationToken);
                var source = await principalSource.AcquireAsync(exclusion, cancellationToken);
                var support = await supportSource.AcquireAsync(exclusion, source, cancellationToken: cancellationToken);
                async Task RequireCurrentAsync(CancellationToken currentToken)
                {
                    RequireCustody(clock.GetUtcNow());
                    await exclusion.ValidateAsync(currentToken);
                    var current = await source.ValidateAsync(currentToken);
                    var currentSupport = await support.ValidateAsync(currentToken);
                    RequirePrincipal(current);
                    RequireSupport(currentSupport);
                    if (!await authority.IsSubjectCurrentForCommitAsync(subject.PrincipalId, currentToken))
                        throw new UnauthorizedAccessException("The original human is no longer current.");
                    if (!HasMembership(current, tenantId)) throw new MembershipRefused();
                    RequireCustody(clock.GetUtcNow());
                }
                await RequireCurrentAsync(cancellationToken);
                var mutation = new Mutation(subject.PrincipalId, tenantId, clock.GetUtcNow().UtcDateTime, RequireCurrentAsync);
                _current = mutation;
                finalizer.Register(async finalToken =>
                {
                    await store.RequireCompleteAsync(mutation, finalToken);
                    await RequireCurrentAsync(finalToken);
                }, RequireCustody);
                if (!await store.WriteAsync(mutation, cancellationToken)) throw new MembershipRefused();
                return true;
            }, token);
        }
        catch (MembershipRefused) { return false; }
        finally { _current = null; }
    }

    private sealed class MembershipRefused : Exception;

    private sealed record Mutation(Guid SubjectId, Guid TenantId, DateTime UpdatedAt,
        Func<CancellationToken, Task> Validate) : IHumanTenantPreferenceMutation
    {
        public Task RequireCurrentAsync(CancellationToken cancellationToken) => Validate(cancellationToken);
        public override string ToString() => nameof(Mutation);
    }
}
