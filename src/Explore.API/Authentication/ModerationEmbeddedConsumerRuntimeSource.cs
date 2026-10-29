using System.Security.Cryptography;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Explore.API.Models;
using Explore.API.Services.Calendar;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services;
using Explore.Application.Services.Moderation;
using Explore.Persistence.Services;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Features.EventResources.Handlers.Queries;
using Explore.Application.Features.EventResources.Requests.Queries;
using Explore.Application.Features.Events.Handlers.Queries;
using Explore.Application.Features.Federation.Atproto.Handlers.Queries;
using Explore.Application.Features.PublicExperience.Handlers.Queries;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Persistence.Repositories;
using Explore.Infrastructure;
using Explore.Infrastructure.Messaging;
using Explore.Infrastructure.Webhooks;
using ISLAMU.Moderation.Application;

namespace Explore.API.Authentication;

/// <summary>
/// Observes explicitly composed native owners, not inventory support states or conformance.
/// Owner resolution is deferred until observation so lifecycle repositories can acquire these
/// sources without recursively constructing their own authority graph.
/// </summary>
public sealed class ModerationEmbeddedConsumerRuntimeSource : IModerationConsumerRuntimeSource
{
    private readonly IServiceProvider services;
    private readonly ModerationCapabilityRegistry inventory;
    private readonly (Type Service, Type Implementation)[] owners;
    private readonly Type[] artifacts;

    private ModerationEmbeddedConsumerRuntimeSource(IServiceProvider services,
        ModerationCapabilityRegistry inventory, string consumerId,
        (Type Service, Type Implementation)[] owners, Type[] artifacts)
        => (this.services, this.inventory, ConsumerId, this.owners, this.artifacts) =
            (services, inventory, consumerId, owners, artifacts);

    public string ConsumerId { get; }
    public ModerationNativeMode Mode => ModerationNativeMode.Embedded;

    /// <summary>Fixed owner mappings; adding an inventory entry never installs a runtime source.</summary>
    public static IServiceCollection AddSources(IServiceCollection services)
    {
        var application = typeof(RequestAuthorization<>).Assembly;
        var queryDecorator = application.GetType(
            "Explore.Application.Operations.Decorators.AuthorizationQueryHandlerDecorator`2", throwOnError: true)!
            .MakeGenericType(typeof(GetEventResourceAudienceDetailQuery), typeof(EventResourceAudienceDetailResult));
        Add("native.operations",
            [(typeof(RequestAuthorization<GetEventResourceAudienceDetailQuery>), typeof(RequestAuthorization<GetEventResourceAudienceDetailQuery>)),
             (typeof(IQueryHandler<GetEventResourceAudienceDetailQuery, EventResourceAudienceDetailResult>), queryDecorator),
             Self<GetEventResourceAudienceDetailQueryHandler>()],
            application.GetType("Explore.Application.Operations.Decorators.AuthorizationCommandHandlerDecorator`1", true)!,
            application.GetType("Explore.Application.Operations.Decorators.AuthorizationCommandHandlerDecorator`2", true)!);
        Add("native.machine",
            [Self<ApiKeyAuthenticationHandler>(),
             (typeof(IConfiguredApiKeyResourceAuthorityReader), typeof(ConfiguredApiKeyResourceAuthorityReader)),
             (typeof(IExternalApiKeyRepository), typeof(ExternalApiKeyRepository)),
             (typeof(IModerationMachineSubjectBindingRepository), typeof(ModerationMachineSubjectBindingRepository)),
             Snapshot()], typeof(MachineScopeMapping));
        Add("control.lifecycle",
            [Self<ModerationOperationService>(),
             (typeof(IModerationStore), typeof(ModerationStore)),
             (typeof(IModerationAuthorityParticipant), typeof(ModerationAuthorityParticipant))],
            typeof(Explore.Application.Features.Moderation.Handlers.Commands.ApplyModerationLifecycleCommandHandler));
        Add("queued.moderation",
            [(typeof(IOutboxMessageDispatcher), typeof(CompositeOutboxMessageDispatcher)),
             (typeof(IModerationEffectDispatcher), typeof(ModerationEffectOutboxDispatcher)),
             (typeof(IModerationEffectLeaseRepository), typeof(ModerationEffectLeaseRepository)),
             (typeof(IModerationResourceQuarantineRepository), typeof(ModerationResourceQuarantineRepository)),
             (typeof(IModerationOriginalEffectCompletionRepository), typeof(ModerationOriginalEffectCompletionRepository))]);
        Add("resource.audience.detail", [Self<EventResourceAudienceWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.audience.page", [Self<EventResourceAudienceWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.audience.actions",
            [Self<EventResourceAudienceWorkflow>(),
             (typeof(IEventResourceCapabilityAuthorizer), typeof(EventResourceCapabilityAuthorizer)), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.management.detail", [Self<EventResourceManagementWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceManagementReadResult));
        Add("resource.management.list", [Self<EventResourceManagementWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceManagementReadResult));
        Add("resource.management.audit", [Self<EventResourceManagementWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceManagementReadResult));
        Add("resource.management.export", [Self<EventResourceManagementWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.payload", [Self<EventResourceContentService>(), Authority(), Snapshot()],
            typeof(EventResourceFileResult));
        Add("resource.redirect", [Self<EventResourceAccessService>(), Authority(), Snapshot()]);
        Add("resource.upload.reserve", [Self<EventResourceFileUploadWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.upload.finalize", [Self<EventResourceFileUploadWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.upload.cancel", [Self<EventResourceFileUploadWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("resource.upload.replay", [Self<EventResourceFileUploadWorkflow>(), Authority(), Snapshot()],
            typeof(EventResourceReadProjection<>));
        Add("storage.generic", [(typeof(IStorageObjectContentReader), typeof(StorageObjectContentReader))],
            typeof(StoragePresentationUrlResolver));
        Add("discovery.public",
            [Self<GetPublicEventDiscoveryRequestHandler>(), Self<GetHomeDiscoveryQueryHandler>(),
             (typeof(IEventDetailsProjectionService), typeof(EventDetailsProjectionService))]);
        Add("calendar.export",
            [Self<GetEventCalendarExportRequestHandler>(), Self<GetAttendeeEventCalendarExportRequestHandler>(),
             (typeof(IModerationPublicEventCalendarRepository), typeof(ModerationPublicEventCalendarRepository))],
            typeof(PublicEventCalendarProjection), typeof(PublicEventCalendarHttpResult),
            typeof(IcalNetEventCalendarFileBuilder));
        Add("queued.notification",
            [(typeof(INotificationFanoutRecipientMaterializationService), typeof(NotificationFanoutRecipientMaterializationService)),
             (typeof(INotificationRefreshStreamService), typeof(NotificationRefreshStreamService)),
             (typeof(IEmailDispatchDrainService), typeof(EmailDispatchDrainService))]);
        Add("queued.federation",
            [Self<AtprotoPdsDeliveryProcessor>(),
             (typeof(IAtprotoDeliveryGate), typeof(AtprotoEventPublicationPlanner)),
             (typeof(IAtprotoPdsDeliveryGateway), typeof(Explore.Infrastructure.Services.Federation.AtprotoPdsDeliveryGateway))]);
        Add("queued.webhook", [Self<WebhookProviderPublicationDispatcher>()],
            typeof(Explore.Application.Webhooks.DefaultWebhookPayloadBuilder));
        return services;

        void Add(string id, (Type Service, Type Implementation)[] owners, params Type[] artifacts) =>
            services.AddScoped<IModerationConsumerRuntimeSource>(provider =>
                new ModerationEmbeddedConsumerRuntimeSource(provider,
                    provider.GetRequiredService<ModerationCapabilityRegistry>(), id, owners, artifacts));
    }

    private static (Type, Type) Self<T>() => (typeof(T), typeof(T));
    private static (Type, Type) Authority() => Self<EventResourceAuthorityOrchestrator>();
    private static (Type, Type) Snapshot() =>
        (typeof(IEventResourceModerationSnapshotReader), typeof(EventResourceModerationSnapshotReader));

    public ModerationConsumerRuntimeObservation? ReadCurrent()
    {
        if (!inventory.Consumers.ContainsKey(ConsumerId)) return null;
        foreach (var owner in owners)
        {
            object? current;
            try { current = services.GetService(owner.Service); }
            catch (InvalidOperationException) { return null; }
            if (current?.GetType() != owner.Implementation) return null;
        }
        var products = new List<ModerationMeasurementProductArtifact>();
        var assemblies = new[]
        {
            typeof(EventResourceFileResult).Assembly, typeof(EventResourceContentService).Assembly,
            typeof(Explore.Domain.ModerationEffectIntent).Assembly, typeof(EventResourceModerationSnapshotReader).Assembly,
            typeof(Explore.Infrastructure.Services.Moderation.ModerationMeasurementReceiptVerifier).Assembly,
            typeof(Explore.ServiceDefaults.Secrets.SecretAuthorityAcquisition).Assembly,
            typeof(ISLAMU.Moderation.Application.IModerationEffectPlanner).Assembly,
            typeof(ISLAMU.Moderation.Domain.ModerationDecision).Assembly
        };
        if (owners.Any(owner => !assemblies.Contains(owner.Implementation.Assembly))
            || artifacts.Prepend(typeof(ModerationEmbeddedConsumerRuntimeSource))
                .Any(artifact => !assemblies.Contains(artifact.Assembly)))
            return null;
        try
        {
            for (var index = 0; index < assemblies.Length; index++)
            {
                var assembly = assemblies[index];
                var name = ModerationMeasurementProductManifest.AssemblyNames[index];
                if (assembly.GetName().Name != name || string.IsNullOrEmpty(assembly.Location))
                    return null;
                var path = Path.GetFullPath(assembly.Location);
                for (string? component = path; component is not null; component = Path.GetDirectoryName(component))
                    if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0) return null;
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length is 0 or > 512L * 1024 * 1024) return null;
                using (var pe = new PEReader(stream, PEStreamOptions.LeaveOpen))
                {
                    var metadata = pe.GetMetadataReader();
                    if (metadata.GetGuid(metadata.GetModuleDefinition().Mvid) != assembly.ManifestModule.ModuleVersionId)
                        return null;
                }
                stream.Position = 0;
                products.Add(new(name, Convert.ToHexStringLower(SHA256.HashData(stream))));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            return null;
        }
        var productDigest = ModerationMeasurementProductManifest.Digest([.. products]);
        return new(ConsumerId, Mode, ModerationMeasurementProductManifest.RuntimeGeneration(
            ConsumerId, Mode, inventory.Fingerprint, productDigest), inventory.Fingerprint, productDigest);
    }
}
