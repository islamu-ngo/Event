using System.Text.Json;
using System.Collections.Immutable;
using System.Text.Json.Serialization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.EventResourceProviderActivation.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Services;
using Explore.Application.Settings;

namespace Explore.Application.Features.Settings;

/// <summary>
/// The actual command is retained by identity. Its nonsecret immutable input is captured
/// once, before observation; generated identities and represented time never change on retry.
/// </summary>
public sealed class EventResourceProviderOriginal
{
    internal EventResourceProviderOriginal(object command, Guid actor, Guid ambientTenant, DateTime occurredAt)
    {
        Command = command;
        Actor = actor;
        AmbientTenant = ambientTenant;
        OccurredAt = new DateTime(occurredAt.Ticks - occurredAt.Ticks % 10, DateTimeKind.Utc);
        switch (command)
        {
            case BindEventResourceProviderCommand bind:
                ArgumentNullException.ThrowIfNull(bind.Binding);
                ArgumentNullException.ThrowIfNull(bind.Binding.Endpoints);
                var binding = bind.Binding with
                {
                    Endpoints = bind.Binding.Endpoints.Select(EventResourceProviderBindingDocument.NormalizeEndpoint)
                        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                };
                binding.Validate();
                Bind = bind with { Binding = binding };
                break;
            case BeginEventResourceProviderOperationCommand begin:
                Begin = begin;
                break;
            case ActivateEventResourceProviderCommand activate:
                Activate = activate;
                break;
            case SyncAuthorizationPolicyPackageCommand publication when !publication.Request.InspectOnly:
                Publication = publication;
                break;
            default:
                throw new UnauthorizedAccessException("An original resource-provider command is required.");
        }
    }

    [JsonIgnore]
    public object Command { get; }
    public Guid Actor { get; }
    public Guid AmbientTenant { get; }
    public DateTime OccurredAt { get; }
    public Guid OperationId { get; } = Guid.CreateVersion7();
    public Guid Revision { get; } = Guid.CreateVersion7();
    public Guid SettingId { get; } = Guid.CreateVersion7();
    public Guid ConcurrencyStamp { get; } = Guid.CreateVersion7();
    public BindEventResourceProviderCommand? Bind { get; }
    public BeginEventResourceProviderOperationCommand? Begin { get; }
    public ActivateEventResourceProviderCommand? Activate { get; }
    [JsonIgnore]
    public SyncAuthorizationPolicyPackageCommand? Publication { get; }
    [JsonIgnore]
    public IEventResourceProviderPublicationSelection? PublicationSelection { get; internal set; }

    // Never serialize Command or Publication: the one-time administrator pair is not
    // an authority image and must not reach a database parameter, source stamp or log.
    public string Serialize() => JsonSerializer.Serialize(new
    {
        Actor,
        AmbientTenant,
        OccurredAt,
        OperationId,
        Revision,
        SettingId,
        ConcurrencyStamp,
        Bind,
        Begin,
        Activate,
        Publication = Publication is not null,
        PublicationEndpoint = PublicationSelection?.GrpcEndpoint
    });

    public override string ToString() => nameof(EventResourceProviderOriginal);
}

/// <summary>Only the original preparation issuer can supply this source.</summary>
public interface IEventResourceProviderOriginalSource
{
    HumanCommitSubject Subject { get; }
    void RequireCustody();
    Task RequireSourcesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Closed actions, never a caller supplied effect list or callback. Completion requires
/// the exact paired write, or the original command's genuine no-op.
/// </summary>
public interface IEventResourceProviderOriginalStore
{
    Task ObserveAsync(EventResourceProviderOriginal original, IEventResourceProviderOriginalSource source,
        CancellationToken cancellationToken);
    EventResourceProviderPublicationInput ReadPublicationInput(EventResourceProviderOriginal original);
    Task AcquireAndCompileAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    Task<EventResourceProviderOperation> ApplyBindAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    Task<EventResourceProviderOperation> ApplyBeginAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    Task<bool> ApplyActivateAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    Task<IEventResourceProviderPublicationSelection> ApplyPublicationAsync(EventResourceProviderOriginal original,
        CancellationToken cancellationToken);
    Task VerifyAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    Task RequireCompleteAsync(EventResourceProviderOriginal original, CancellationToken cancellationToken);
    void Close(EventResourceProviderOriginal original);
}

/// <summary>Fresh captured resolver inputs; null rows are original absences, not fallback permissions.</summary>
public sealed record EventResourceProviderPublicationInput(
    Guid TenantId, ImmutableArray<HumanAuthorizationRouteSettingFact> Settings);
