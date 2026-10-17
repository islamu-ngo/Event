using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.Settings;
using Explore.Domain.Settings;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

public sealed partial class HumanSettingsMutationPreparation
{
    public EventResourceProviderOriginal? CurrentEventResourceProviderOriginal =>
        _currentOwner.Value is { Active: true } current ? current.ResourceProviderOriginal : null;

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(BindEventResourceProviderCommand command,
        CancellationToken cancellationToken = default) => PrepareResourceProviderAsync(command, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(BeginEventResourceProviderOperationCommand command,
        CancellationToken cancellationToken = default) => PrepareResourceProviderAsync(command, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(ActivateEventResourceProviderCommand command,
        CancellationToken cancellationToken = default) => PrepareResourceProviderAsync(command, cancellationToken);

    public Task<IPreparedHumanSettingsMutation> PrepareAsync(SyncAuthorizationPolicyPackageCommand command,
        CancellationToken cancellationToken = default) => PrepareResourceProviderAsync(command, cancellationToken);

    public void RequireEventResourceProviderSource(IEventResourceProviderOriginalSource source)
    {
        if (source is not ResourceProviderSource selected || !ReferenceEquals(selected.Issuer, this))
            throw new UnauthorizedAccessException("The original resource-provider source issuer is required.");
        selected.RequireCustody();
    }

    public void CloseEventResourceProvider(IPreparedHumanSettingsMutation prepared)
    {
        var issued = RequireIssued(prepared);
        if (issued.ResourceProviderOriginal is null || issued.Active)
            throw new UnauthorizedAccessException("Only an inactive original resource-provider preparation can close.");
        Interlocked.Exchange(ref issued.Consumed, 1);
        issued.ResourceProviderStore!.Close(issued.ResourceProviderOriginal);
    }

    private async Task<IPreparedHumanSettingsMutation> PrepareResourceProviderAsync(object command, CancellationToken token)
    {
        // This is the established instance-administrator predicate. Observing its route
        // does not add a new Cerbos action, a policy grant, or MixedSettings authority.
        var human = (Prepared)await PrepareCoreAsync(command, SettingScope.Instance, null, Authority.Instance,
            null, (_, _) => [], [], token, observeRouteOnly: true);
        var subject = humanScope.Subject ?? throw new UnauthorizedAccessException("An original human is required.");
        var request = httpContext.HttpContext ?? throw new UnauthorizedAccessException("An original request is required.");
        var principal = request.User;
        var requestServices = request.RequestServices;
        var tenant = tenantContext.TenantId;
        var original = new EventResourceProviderOriginal(command, subject.PrincipalId, tenant, clock.GetUtcNow().UtcDateTime);
        var source = new ResourceProviderSource(this, subject, () =>
        {
            if (!ReferenceEquals(humanScope.Subject, subject) || !ReferenceEquals(httpContext.HttpContext, request)
                || !ReferenceEquals(request.User, principal) || !ReferenceEquals(request.RequestServices, requestServices)
                || tenantContext.TenantId != tenant)
                throw new UnauthorizedAccessException("Original resource-provider request custody changed.");
        }, async cancellation =>
        {
            await (human.ValidateOwner ?? throw new UnauthorizedAccessException("The original owner is not acquired."))(cancellation);
            if (original.Publication is { } publication)
                await services.GetRequiredService<IPolicyPackageService>().RequireOriginalSelectionAsync(
                    publication, original.PublicationSelection
                        ?? throw new UnauthorizedAccessException("The original publication target is absent."), cancellation);
        });
        var store = services.GetRequiredService<IEventResourceProviderOriginalStore>();
        try
        {
            await store.ObserveAsync(original, source, token);
            if (original.Publication is { } publication)
                original.PublicationSelection = await services.GetRequiredService<IPolicyPackageService>()
                    .SelectOriginalPublicationAsync(publication, store.ReadPublicationInput(original), token);
            source.RequireCustody();
            return new Prepared(this, command, [], [], human.Owner, async cancellation =>
            {
                human.ValidateOwner = await human.Acquire(cancellation);
                await store.AcquireAndCompileAsync(original, cancellation);
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(
                    finalToken => store.RequireCompleteAsync(original, finalToken), _ => source.RequireCustody());
                return guardToken => store.VerifyAsync(original, guardToken);
            })
            { ResourceProviderOriginal = original, ResourceProviderStore = store };
        }
        catch
        {
            store.Close(original);
            throw;
        }
    }

    private sealed class ResourceProviderSource(HumanSettingsMutationPreparation issuer, HumanCommitSubject subject,
        Action custody, Func<CancellationToken, Task> validate) : IEventResourceProviderOriginalSource
    {
        internal HumanSettingsMutationPreparation Issuer => issuer;
        public HumanCommitSubject Subject => subject;
        public void RequireCustody() => custody();
        public Task RequireSourcesAsync(CancellationToken cancellationToken)
        {
            RequireCustody();
            return validate(cancellationToken);
        }
    }
}
