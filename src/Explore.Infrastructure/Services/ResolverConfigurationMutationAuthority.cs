using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Keeps the actual resolver administrator request and credential. The resolver action has
/// neither a setup-secret alternative nor a configured startup enrollment.
/// </summary>
public sealed class ResolverConfigurationMutationAuthority(
    IHttpContextAccessor contexts, IHumanCommitScopePreparation human,
    IHumanAuthenticationLifetimeObserver lifetimes, TimeProvider clock)
    : IResolverConfigurationMutationAuthority
{
    private readonly IHttpContextAccessor _contexts = contexts;
    private readonly IHumanCommitScopePreparation _human = human;
    private readonly IHumanAuthenticationLifetimeObserver _lifetimes = lifetimes;
    private readonly TimeProvider _clock = clock;

    public async Task<IResolverConfigurationMutationSource> CaptureAsync(
        UpdateResolverConfigurationCommand command, CancellationToken cancellationToken)
    {
        var request = _contexts.HttpContext
            ?? throw new UnauthorizedAccessException("Resolver configuration requires its original authenticated request.");
        var principal = request.User;
        var authentication = await request.AuthenticateAsync();
        if (!authentication.Succeeded || authentication.Principal is null
            || authentication.Ticket?.AuthenticationScheme == ApiAuthenticationSchemeNames.SetupSecret
            || !authentication.Principal.Identities.Any(identity =>
                identity.IsAuthenticated && principal.Identities.Contains(identity)))
            throw new UnauthorizedAccessException("Resolver configuration requires a genuine human credential.");
        await _human.PrepareAsync(cancellationToken);
        var subject = _human.Subject
            ?? throw new UnauthorizedAccessException("Resolver configuration requires its original human subject.");
        var lifetime = await _lifetimes.ObserveAsync(principal, cancellationToken)
            ?? throw new UnauthorizedAccessException("Resolver configuration requires its original credential lifetime.");
        if (command.UserId == Guid.Empty || subject.PrincipalId != command.UserId
            || lifetime.AccountKey != subject.AccountKey)
            throw new UnauthorizedAccessException("The original resolver actor and credential must agree.");
        var source = new Source(this, command, request, principal, authentication.Ticket!, subject, lifetime,
            request.Request.Headers.Authorization.ToString(), request.Request.Headers.Cookie.ToString());
        await source.RequireSourcesAsync(cancellationToken);
        source.RequireTime();
        return source;
    }

    public void RequireIssued(IResolverConfigurationMutationSource source)
    {
        if (source is not Source issued || !ReferenceEquals(issued.Owner, this))
            throw new UnauthorizedAccessException("The original scoped resolver authority must issue this source.");
    }

    private sealed class Source(ResolverConfigurationMutationAuthority owner,
        UpdateResolverConfigurationCommand command, HttpContext request,
        System.Security.Claims.ClaimsPrincipal principal, AuthenticationTicket ticket,
        HumanCommitSubject subject, HumanAuthenticationLifetime lifetime, string authorization, string cookie)
        : IResolverConfigurationMutationSource
    {
        private readonly IServiceProvider requestServices = request.RequestServices;
        private readonly string method = request.Request.Method;
        private readonly PathString path = request.Request.Path;
        private readonly PathString pathBase = request.Request.PathBase;
        private readonly QueryString query = request.Request.QueryString;
        private readonly Dictionary<string, string> supportHeaders = request.Request.Headers
            .Where(header => SupportAccessHeaderNames.IsSupportAccessHeader(header.Key))
            .ToDictionary(header => header.Key, header => header.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        internal ResolverConfigurationMutationAuthority Owner => owner;
        public UpdateResolverConfigurationCommand OriginalCommand => command;
        public HumanCommitSubject Subject => subject;
        public HumanAuthenticationLifetime Lifetime => lifetime;
        public Guid Actor => subject.PrincipalId;

        private void RequireCustody()
        {
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(owner._contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(owner._human.Subject, subject)
                || !ReferenceEquals(request.RequestServices, requestServices)
                || request.Request.Method != method || request.Request.Path != path
                || request.Request.PathBase != pathBase || request.Request.QueryString != query
                || request.Request.Headers.Authorization.ToString() != authorization
                || request.Request.Headers.Cookie.ToString() != cookie
                || request.Request.Headers.Count(header => SupportAccessHeaderNames.IsSupportAccessHeader(header.Key))
                    != supportHeaders.Count
                || supportHeaders.Any(header => request.Request.Headers[header.Key].ToString() != header.Value))
                throw new UnauthorizedAccessException("The original resolver request or credential was replaced.");
        }

        public async Task RequireSourcesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireCustody();
            var current = await request.AuthenticateAsync();
            if (!current.Succeeded || !ReferenceEquals(current.Ticket, ticket)
                || !ReferenceEquals(await owner._lifetimes.ObserveAsync(principal, cancellationToken), lifetime))
                throw new UnauthorizedAccessException("The original resolver ticket or lifetime changed.");
            RequireCustody();
        }

        public void RequireTime()
        {
            RequireCustody();
            if (!lifetime.IsValidAt(owner._clock.GetUtcNow()))
                throw new UnauthorizedAccessException("The original resolver credential lifetime ended.");
        }
    }
}
