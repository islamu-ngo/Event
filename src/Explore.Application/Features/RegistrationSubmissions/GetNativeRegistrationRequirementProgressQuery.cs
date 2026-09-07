using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Domain;
using Explore.Domain.Enums;
using MediatR;
using static Explore.Application.Features.RegistrationProviders.Commands.RegistrationProviderManagementHandlerHelpers;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record GetNativeRegistrationRequirementProgressQuery(
    Guid TenantId,
    Guid EventId,
    Guid OrderId) : IRequest<NativeRegistrationRequirementProgressCollectionDto?>;

public sealed class GetNativeRegistrationRequirementProgressQueryHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationFormAuthoringRepository authoring,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationFinalizationRepository finalization)
    : IRequestHandler<GetNativeRegistrationRequirementProgressQuery, NativeRegistrationRequirementProgressCollectionDto?>
{
    public async Task<NativeRegistrationRequirementProgressCollectionDto?> Handle(
        GetNativeRegistrationRequirementProgressQuery request,
        CancellationToken cancellationToken)
    {
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, request.TenantId, cancellationToken);
        NativeRegistrationLaunchAuthority? authority = await NativeRegistrationLaunchDescriptorResolver.ResolveAsync(
            authoring, order, request.EventId, cancellationToken);
        if (authority is null)
        {
            return null;
        }

        RegistrationWorkflow workflow = authority.Workflow;

        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken);
        var descriptors = new List<NativeRegistrationLaunchDescriptorDto>();
        var providerDescriptors = new List<NativeRegistrationProviderLaunchDescriptorDto>();
        foreach (RegistrationRequirement requirement in workflow.Requirements
                     .Where(value => !value.IsDeleted).OrderBy(value => value.Ordinal))
        {
            IReadOnlyList<RegistrationRequirementFulfillment> fulfillments = await finalization.GetFulfillmentsAsync(
                request.TenantId, order.Id, requirement.Id, cancellationToken);
            IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects = NativeRegistrationAttemptContractBuilder.Subjects(
                order, requirement, participants, assignments, fulfillments);
            NativeRegistrationRequirementProgressDto progress = NativeRegistrationAttemptContractBuilder.Progress(subjects);
            if (subjects.Count == 0 || progress.IsComplete)
            {
                continue;
            }

            foreach (RegistrationChannel channel in requirement.Channels
                         .Where(value => authority.Version is not null && !value.IsDeleted && value.IsNative)
                         .OrderBy(value => value.Ordinal))
            {
                descriptors.Add(new(
                    requirement.Id,
                    channel.Id,
                    authority.Version!.RegistrationFormId,
                    authority.Version.Id,
                    requirement.CanSkip,
                    subjects,
                    progress));
            }

            foreach (RegistrationChannel channel in requirement.Channels
                         .Where(value => !value.IsDeleted && !value.IsNative && value.RegistrationProviderBindingId.HasValue)
                         .OrderBy(value => value.Ordinal))
            {
                RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(
                    request.TenantId, channel.RegistrationProviderBindingId!.Value, cancellationToken);
                if (binding is { Connection: not null } &&
                    binding.StateId == (int)RegistrationProviderBindingStateEnum.Published &&
                    binding.CollectionModeId is (int)RegistrationProviderCollectionModeEnum.ProviderApi or
                        (int)RegistrationProviderCollectionModeEnum.MirrorOnly &&
                    binding.PublishedMappingRevisionHash is not null &&
                    binding.RegistrationFormVersionId != Guid.Empty)
                {
                    RegistrationProviderTuple tuple = TupleFromConnection(binding.Connection);
                    RegistrationProviderCapabilitySet persisted = RegistrationProviderCapabilitySet.FromCodes(
                        binding.Capabilities.Where(capability =>
                                !capability.IsDeleted && ProviderRegistrationLaunchDescriptorResolver.CapabilityBelongsToTuple(capability, tuple))
                            .Select(capability => capability.CapabilityCode));
                    IRegistrationProviderDescriptor? provider = providerRegistry.TryResolve(tuple);
                    if (provider is IRegistrationProviderSubmissionSink &&
                        persisted.Intersect(provider.ProvenCapabilities).SubmissionSink)
                    {
                        descriptors.Add(new(
                            requirement.Id,
                            channel.Id,
                            binding.RegistrationFormId,
                            binding.RegistrationFormVersionId,
                            requirement.CanSkip,
                            subjects,
                            progress,
                            binding.Id));
                    }
                    continue;
                }

                NativeRegistrationProviderLaunchDescriptorDto? descriptor = await ProviderRegistrationLaunchDescriptorResolver.ResolveAsync(
                    providerRepository,
                    providerRegistry,
                    request.TenantId,
                    request.EventId,
                    workflow.Id,
                    requirement.Id,
                    channel,
                    channel.RegistrationProviderBindingId!.Value,
                    null,
                    null,
                    subjects,
                    progress,
                    cancellationToken);
                if (descriptor is not null)
                {
                    providerDescriptors.Add(descriptor);
                }
            }
        }

        return new(order.Id, descriptors, providerDescriptors);
    }
}

internal static class ProviderRegistrationLaunchDescriptorResolver
{
    public static async Task<NativeRegistrationProviderLaunchDescriptorDto?> ResolveAsync(
        IRegistrationProviderRepository providerRepository,
        IRegistrationProviderRegistry providerRegistry,
        Guid tenantId,
        Guid eventId,
        Guid workflowId,
        Guid requirementId,
        RegistrationChannel channel,
        Guid bindingId,
        Guid? attemptId,
        string? attemptCapabilityToken,
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects,
        NativeRegistrationRequirementProgressDto progress,
        CancellationToken cancellationToken)
    {
        if (channel.EventId != eventId || channel.RegistrationWorkflowId != workflowId ||
            channel.RegistrationRequirementId != requirementId || channel.IsNative || channel.RegistrationProviderBindingId != bindingId)
        {
            return null;
        }

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(tenantId, bindingId, cancellationToken);
        if (binding is null || binding.Connection is null || binding.IsDeleted ||
            binding.RegistrationFormId == Guid.Empty || binding.RegistrationFormVersionId == Guid.Empty ||
            binding.PublishedMappingRevisionHash is null || binding.StateId != (int)RegistrationProviderBindingStateEnum.Published ||
            binding.DriftClassId >= (int)RegistrationProviderDriftClassEnum.MappingRequired ||
            !await BindingBelongsToEventAsync(providerRepository, tenantId, eventId, bindingId, cancellationToken))
        {
            return Unavailable(requirementId, channel.Id, bindingId, Guid.Empty, Guid.Empty, subjects, progress, "binding_not_launchable");
        }

        RegistrationProviderTuple tuple = TupleFromConnection(binding.Connection);
        RegistrationProviderCapabilitySet persisted = RegistrationProviderCapabilitySet.FromCodes(binding.Capabilities
            .Where(capability => !capability.IsDeleted && CapabilityBelongsToTuple(capability, tuple))
            .Select(capability => capability.CapabilityCode));
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        if (descriptor is not IRegistrationProviderPresentation presentationProvider ||
            !BindingLaunchContractMatchesCapabilities(binding, persisted.Intersect(descriptor.ProvenCapabilities)))
        {
            return Unavailable(requirementId, channel.Id, bindingId, binding.RegistrationFormId, binding.RegistrationFormVersionId, subjects, progress, "presentation_unavailable");
        }

        if (attemptId is null)
        {
            string pendingMode = (RegistrationProviderPresentationModeEnum)binding.PresentationModeId == RegistrationProviderPresentationModeEnum.Embed
                ? "embed"
                : "redirect";
            return new(null, requirementId, channel.Id, bindingId, binding.RegistrationFormId, binding.RegistrationFormVersionId,
                pendingMode, true, null, "Provider registration", pendingMode == "redirect", "manual", "ready", subjects, progress);
        }

        RegistrationProviderPresentationResult result = await presentationProvider.GetPresentationAsync(
            new RegistrationProviderPresentationRequest(tenantId, binding, binding.Connection, tuple, attemptId, attemptCapabilityToken), cancellationToken);
        bool preferEmbed = (RegistrationProviderPresentationModeEnum)binding.PresentationModeId == RegistrationProviderPresentationModeEnum.Embed;
        Uri? uri = preferEmbed && result.EmbedAvailable ? result.EmbedUri : result.RedirectUri;
        string mode = preferEmbed && result.EmbedAvailable ? "embed" : "redirect";
        if (uri is null || !binding.Connection.IsOriginApproved(uri))
        {
            return Unavailable(requirementId, channel.Id, bindingId, binding.RegistrationFormId, binding.RegistrationFormVersionId, subjects, progress, "origin_not_approved");
        }

        return new(attemptId, requirementId, channel.Id, bindingId, binding.RegistrationFormId, binding.RegistrationFormVersionId,
            mode, true, uri.ToString(), "Provider registration", mode == "redirect", "manual", "ok", subjects, progress);
    }

    public static bool CapabilityBelongsToTuple(RegistrationProviderCapability capability, RegistrationProviderTuple tuple) =>
        StringComparer.Ordinal.Equals(capability.ProviderCode, tuple.ProviderCode) &&
        StringComparer.Ordinal.Equals(capability.DeploymentKind, tuple.DeploymentKind) &&
        StringComparer.Ordinal.Equals(capability.ApiVersion, tuple.ApiVersion) &&
        StringComparer.Ordinal.Equals(capability.AdapterPolicyVersion, tuple.AdapterPolicyVersion) &&
        StringComparer.Ordinal.Equals(capability.ConformanceEvidenceRevision, tuple.ConformanceEvidenceRevision);

    private static NativeRegistrationProviderLaunchDescriptorDto Unavailable(
        Guid requirementId,
        Guid channelId,
        Guid bindingId,
        Guid formId,
        Guid formVersionId,
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects,
        NativeRegistrationRequirementProgressDto progress,
        string reason) => new(null, requirementId, channelId, bindingId, formId, formVersionId, "unavailable", false, null,
        "Provider registration", false, "manual", reason, subjects, progress);
}

internal sealed record NativeRegistrationLaunchAuthority(
    RegistrationWorkflow Workflow,
    RegistrationFormVersion? Version);

internal static class NativeRegistrationLaunchDescriptorResolver
{
    public static async Task<NativeRegistrationLaunchAuthority?> ResolveAsync(
        IRegistrationFormAuthoringRepository authoring,
        RegistrationOrder? order,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        RegistrationWorkflow? workflow = await authoring.GetWorkflowAsync(
            eventId, "registration", cancellationToken);
        if (order is null || workflow is null || order.EventId != eventId ||
            order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.AwaitingRequirements ||
            order.RegistrationWorkflowVersionId != workflow.Id)
        {
            return null;
        }

        IReadOnlyList<RegistrationFormVersion> published = await authoring.GetPublishedVersionsAsync(
            eventId, 2, cancellationToken);
        return new(workflow, published.Count == 1 ? published[0] : null);
    }
}
