using Explore.Application.Authentication;
using Explore.Application.Constants;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Commands;
using Explore.Application.Settings;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;

namespace Explore.Infrastructure.Services;

/// <summary>
/// Preserves the editor's actual authentication branch. Setup selection never confers
/// administrator authority and a human ticket is never reclassified as a setup principal.
/// </summary>
public sealed class OperatorIdentityMutationAuthority(
    IHttpContextAccessor contextAccessor, IHumanCommitScopePreparation preparation,
    IHumanAuthenticationLifetimeObserver lifetimeObserver, ISetupSecretProvider setupProvider, TimeProvider timeProvider)
    : IOperatorIdentityMutationAuthority
{
    private readonly IHttpContextAccessor contexts = contextAccessor;
    private readonly IHumanCommitScopePreparation human = preparation;
    private readonly IHumanAuthenticationLifetimeObserver lifetimes = lifetimeObserver;
    private readonly ISetupSecretProvider setup = setupProvider;
    private readonly TimeProvider clock = timeProvider;

    public Task<IOperatorIdentityMutationSource> CaptureAsync(SaveInstanceOperatorIdentityCommand command,
        CancellationToken cancellationToken) => CaptureCoreAsync(command, null, cancellationToken);

    public Task<IOperatorIdentityMutationSource> CaptureAsync(ImportInstanceOperatorIdentityCommand command,
        Guid actor, CancellationToken cancellationToken) => CaptureCoreAsync(command, actor, cancellationToken);

    private async Task<IOperatorIdentityMutationSource> CaptureCoreAsync(object command, Guid? expectedActor,
        CancellationToken token)
    {
        var request = contexts.HttpContext
            ?? throw new UnauthorizedAccessException("Operator identity requires its actual authenticated request.");
        var principal = request.User;
        var authentication = await request.AuthenticateAsync();
        if (!authentication.Succeeded || authentication.Principal is null
            || !authentication.Principal.Identities.Any(identity =>
                identity.IsAuthenticated && principal.Identities.Contains(identity)))
            throw new UnauthorizedAccessException("The original operator identity authentication ticket is required.");
        bool setupTicket = authentication.Ticket?.AuthenticationScheme == ApiAuthenticationSchemeNames.SetupSecret;
        string? secret = request.Request.Headers["X-Setup-Secret"].FirstOrDefault();
        bool setupSelected = command is SaveInstanceOperatorIdentityCommand
            && (setupTicket || !string.IsNullOrWhiteSpace(secret));
        if (setupTicket && !setupSelected)
            throw new UnauthorizedAccessException("Operator identity import requires a human administrator.");
        if (setupSelected
            && await setup.ValidateSecretAsync(secret, token) != SetupSecretValidationOutcome.Accepted)
            throw new UnauthorizedAccessException("The original operator identity setup secret is not accepted.");

        HumanCommitSubject? subject = null;
        HumanAuthenticationLifetime? lifetime = null;
        if (!setupTicket)
        {
            await human.PrepareAsync(token);
            subject = human.Subject
                ?? throw new UnauthorizedAccessException("Operator identity requires its original resolved human.");
            lifetime = await lifetimes.ObserveAsync(principal, token)
                ?? throw new UnauthorizedAccessException("Operator identity requires its original credential lifetime.");
            if (subject.PrincipalId == Guid.Empty || lifetime.AccountKey != subject.AccountKey
                || expectedActor.HasValue && expectedActor != subject.PrincipalId)
                throw new UnauthorizedAccessException("The actual command actor and credential must agree.");
        }
        else if (human.Subject is not null)
            throw new UnauthorizedAccessException("A selected human cannot become a setup-secret source.");

        var source = new Source(this, command, request, principal, authentication.Ticket!,
            setupSelected, setupTicket, secret, subject, lifetime);
        await source.RequireSourcesAsync(token);
        source.RequireTime();
        return source;
    }

    public void RequireIssued(IOperatorIdentityMutationSource source)
    {
        if (source is not Source issued || !ReferenceEquals(issued.Owner, this))
            throw new UnauthorizedAccessException("The original operator identity authority must issue this source.");
    }

    private sealed class Source(OperatorIdentityMutationAuthority owner, object command,
        HttpContext request, System.Security.Claims.ClaimsPrincipal principal, AuthenticationTicket ticket,
        bool isSetup, bool setupTicket, string? secret, HumanCommitSubject? subject,
        HumanAuthenticationLifetime? lifetime) : IOperatorIdentityMutationSource
    {
        internal OperatorIdentityMutationAuthority Owner => owner;
        public object OriginalCommand => command;
        public bool IsSetup => isSetup;
        public bool IsHuman => !setupTicket;
        public Guid? Actor => subject?.PrincipalId;

        private void RequireCustody()
        {
            request.RequestAborted.ThrowIfCancellationRequested();
            if (!ReferenceEquals(owner.contexts.HttpContext, request) || !ReferenceEquals(request.User, principal)
                || !ReferenceEquals(owner.human.Subject, subject)
                || request.Request.Headers["X-Setup-Secret"].FirstOrDefault() != secret)
                throw new UnauthorizedAccessException("The original operator identity request was replaced.");
            if (isSetup && !owner.setup.ValidateSecret(secret))
                throw new UnauthorizedAccessException("The original operator identity setup secret was replaced or locked.");
        }

        public async Task RequireSourcesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireCustody();
            var current = await request.AuthenticateAsync();
            if (!current.Succeeded || !ReferenceEquals(current.Ticket, ticket))
                throw new UnauthorizedAccessException("The original operator identity authentication ticket changed.");
            if (lifetime is not null && !ReferenceEquals(
                await owner.lifetimes.ObserveAsync(principal, cancellationToken), lifetime))
                throw new UnauthorizedAccessException("The original operator identity credential was replaced.");
            RequireCustody();
        }

        public void RequireTime()
        {
            RequireCustody();
            if (lifetime is not null && !lifetime.IsValidAt(owner.clock.GetUtcNow()))
                throw new UnauthorizedAccessException("The original operator identity credential lifetime ended.");
        }
    }
}
