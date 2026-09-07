using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationSubmissions;
using Explore.Domain;
using Explore.Domain.Enums;
using FluentValidation;
using MediatR;
using static Explore.Application.Features.RegistrationProviders.Commands.RegistrationProviderManagementHandlerHelpers;

namespace Explore.Application.Features.RegistrationSubmissions.Commands;

public sealed record LaunchNativeRegistrationAttemptCommand(
    Guid TenantId,
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    Guid? BindingId = null,
    Guid? SupersededAttemptId = null) : IRequest<NativeRegistrationAttemptResult>;

public sealed record LaunchRegistrationProviderAttemptCommand(
    Guid TenantId,
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid ChannelId,
    Guid BindingId,
    Guid FormId,
    Guid FormVersionId,
    Guid? SupersededAttemptId = null) : IRequest<RegistrationProviderAttemptResult>;

public sealed record SubmitNativeRegistrationAttemptCommand(
    Guid TenantId,
    Guid EventId,
    Guid OrderId,
    Guid RequirementId,
    Guid AttemptId,
    string? AttemptCapabilityToken,
    string? IdempotencyKey,
    IReadOnlyList<RegistrationSubmissionAnswerInput> Answers) : IRequest<NativeRegistrationSubmissionResult>;

public sealed record NativeRegistrationAttemptResult(
    bool Success,
    Guid AttemptId,
    Guid RequirementId,
    Guid ChannelId,
    Guid FormId,
    Guid FormVersionId,
    DateTime ExpiresAt,
    NativeRegistrationFormDefinitionDto? Form,
    IReadOnlyList<NativeRegistrationAnswerSubjectDto> Subjects,
    NativeRegistrationRequirementProgressDto? Progress,
    bool CanSkip,
    string? AttemptCapabilityToken,
    string? FailureCode = null);

public sealed record NativeRegistrationSubmissionResult(
    bool Success,
    Guid SubmissionId,
    IReadOnlyList<RegistrationSubmissionIssueDto> Issues,
    string? FailureCode = null);

public sealed record RegistrationProviderAttemptResult(
    bool Success,
    Guid AttemptId,
    NativeRegistrationProviderLaunchDescriptorDto? Descriptor,
    string? FailureCode = null);

public sealed class LaunchNativeRegistrationAttemptCommandValidator
    : AbstractValidator<LaunchNativeRegistrationAttemptCommand>
{
    public LaunchNativeRegistrationAttemptCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.ChannelId).NotEmpty();
        RuleFor(command => command.FormId).NotEmpty();
        RuleFor(command => command.FormVersionId).NotEmpty();
        RuleFor(command => command.SupersededAttemptId).NotEqual(Guid.Empty);
    }
}

public sealed class SubmitNativeRegistrationAttemptCommandValidator
    : AbstractValidator<SubmitNativeRegistrationAttemptCommand>
{
    public SubmitNativeRegistrationAttemptCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.AttemptId).NotEmpty();
        RuleFor(command => command.AttemptCapabilityToken).NotEmpty();
        RuleFor(command => command.IdempotencyKey).NotEmpty().MaximumLength(128);
        RuleFor(command => command.Answers).NotNull();
    }
}

public sealed class LaunchRegistrationProviderAttemptCommandValidator
    : AbstractValidator<LaunchRegistrationProviderAttemptCommand>
{
    public LaunchRegistrationProviderAttemptCommandValidator()
    {
        RuleFor(command => command.TenantId).NotEmpty();
        RuleFor(command => command.EventId).NotEmpty();
        RuleFor(command => command.OrderId).NotEmpty();
        RuleFor(command => command.RequirementId).NotEmpty();
        RuleFor(command => command.ChannelId).NotEmpty();
        RuleFor(command => command.BindingId).NotEmpty();
        RuleFor(command => command.FormId).NotEmpty();
        RuleFor(command => command.FormVersionId).NotEmpty();
        RuleFor(command => command.SupersededAttemptId).NotEqual(Guid.Empty);
    }
}

public sealed class LaunchNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationFormAuthoringRepository authoring,
    IRegistrationSubmissionRepository submissions,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationFinalizationRepository finalization,
    IGuestCapabilityTokenService capabilities,
    TimeProvider timeProvider)
    : IRequestHandler<LaunchNativeRegistrationAttemptCommand, NativeRegistrationAttemptResult>
{
    public async Task<NativeRegistrationAttemptResult> Handle(
        LaunchNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (!(await new LaunchNativeRegistrationAttemptCommandValidator()
                .ValidateAsync(request, cancellationToken)).IsValid)
        {
            return Missing(request);
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, request.TenantId, cancellationToken);
        NativeRegistrationLaunchAuthority? authority = await NativeRegistrationLaunchDescriptorResolver.ResolveAsync(
            authoring, order, request.EventId, cancellationToken);
        RegistrationRequirement? requirement = authority?.Workflow.Requirements.SingleOrDefault(candidate =>
            !candidate.IsDeleted && candidate.Id == request.RequirementId);
        RegistrationChannel? channel = requirement?.Channels.SingleOrDefault(candidate =>
            !candidate.IsDeleted && candidate.Id == request.ChannelId &&
            (request.BindingId is null
                ? candidate.IsNative
                : !candidate.IsNative && candidate.RegistrationProviderBindingId == request.BindingId));
        RegistrationProviderBinding? binding = request.BindingId is { } bindingId
            ? await providerRepository.GetBindingAsync(request.TenantId, bindingId, cancellationToken)
            : null;
        RegistrationFormVersion? version = binding is null
            ? authority?.Version
            : await authoring.GetVersionAsync(request.EventId, binding.RegistrationFormId, binding.RegistrationFormVersionId, cancellationToken);
        if (order is null || authority is null ||
            requirement is null || channel is null ||
            version is null || version.RegistrationFormId != request.FormId || version.Id != request.FormVersionId ||
            binding is not null && !IsHeadlessBinding(binding, providerRegistry))
        {
            return Missing(request);
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiresAt = order.ExpiresAt is { } orderExpiry && orderExpiry < now.AddHours(1)
            ? orderExpiry
            : now.AddHours(1);
        if (expiresAt <= now)
        {
            return Missing(request);
        }

        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationRequirementFulfillment> fulfillments = await finalization.GetFulfillmentsAsync(
            request.TenantId, order.Id, requirement.Id, cancellationToken);
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects = NativeRegistrationAttemptContractBuilder.Subjects(
            order, requirement, participants, assignments, fulfillments);
        if (subjects.Count == 0)
        {
            return Missing(request);
        }

        GuestCapabilityTokenIssue capability = capabilities.Issue();
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            request.TenantId,
            request.EventId,
            request.OrderId,
            authority.Workflow.Id,
            requirement.Id,
            channel.Id,
            version.RegistrationFormId,
            version.Id,
            capability.Hash,
            binding?.Id,
            binding?.PublishedMappingRevisionHash,
            now,
            expiresAt);
        bool persisted = request.SupersededAttemptId is { } supersededAttemptId
            ? await submissions.PersistReplacementAttemptAsync(
                attempt, supersededAttemptId, "restart-with-fallback", now, cancellationToken)
            : await PersistAttemptAsync(submissions, attempt, cancellationToken);
        if (!persisted)
        {
            return Missing(request);
        }

        return new(true, attempt.Id, requirement.Id, channel.Id, version.RegistrationFormId, version.Id,
            attempt.ExpiresAt, NativeRegistrationAttemptContractBuilder.Form(version), subjects,
            NativeRegistrationAttemptContractBuilder.Progress(subjects), requirement.CanSkip, capability.RawToken);
    }

    private static NativeRegistrationAttemptResult Missing(LaunchNativeRegistrationAttemptCommand request) =>
        new(false, Guid.Empty, request.RequirementId, request.ChannelId, request.FormId, request.FormVersionId,
            default, null, [], null, false, null, "registration_requirement_not_found");

    private static async Task<bool> PersistAttemptAsync(
        IRegistrationSubmissionRepository submissions,
        RegistrationAttempt attempt,
        CancellationToken cancellationToken)
    {
        await submissions.PersistAttemptAsync(attempt, cancellationToken);
        return true;
    }

    private static bool IsHeadlessBinding(
        RegistrationProviderBinding binding,
        IRegistrationProviderRegistry providerRegistry)
    {
        if (binding.Connection is null || binding.IsDeleted ||
            binding.StateId != (int)RegistrationProviderBindingStateEnum.Published ||
            binding.CollectionModeId is not ((int)RegistrationProviderCollectionModeEnum.ProviderApi or
                (int)RegistrationProviderCollectionModeEnum.MirrorOnly) ||
            binding.PublishedMappingRevisionHash is null)
        {
            return false;
        }

        RegistrationProviderTuple tuple = TupleFromConnection(binding.Connection);
        RegistrationProviderCapabilitySet capabilities = RegistrationProviderCapabilitySet.FromCodes(
            binding.Capabilities.Where(capability =>
                    !capability.IsDeleted && ProviderRegistrationLaunchDescriptorResolver.CapabilityBelongsToTuple(capability, tuple))
                .Select(capability => capability.CapabilityCode));
        IRegistrationProviderDescriptor? descriptor = providerRegistry.TryResolve(tuple);
        return descriptor is IRegistrationProviderSubmissionSink &&
            capabilities.Intersect(descriptor.ProvenCapabilities).SubmissionSink;
    }
}

public sealed class LaunchRegistrationProviderAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationFormAuthoringRepository authoring,
    IRegistrationSubmissionRepository submissions,
    IRegistrationProviderRepository providerRepository,
    IRegistrationProviderRegistry providerRegistry,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationFinalizationRepository finalization,
    IGuestCapabilityTokenService capabilities,
    TimeProvider timeProvider)
    : IRequestHandler<LaunchRegistrationProviderAttemptCommand, RegistrationProviderAttemptResult>
{
    public async Task<RegistrationProviderAttemptResult> Handle(
        LaunchRegistrationProviderAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (!(await new LaunchRegistrationProviderAttemptCommandValidator()
                .ValidateAsync(request, cancellationToken)).IsValid)
        {
            return Missing();
        }

        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, request.TenantId, cancellationToken);
        NativeRegistrationLaunchAuthority? authority = await NativeRegistrationLaunchDescriptorResolver.ResolveAsync(
            authoring, order, request.EventId, cancellationToken);
        RegistrationRequirement? requirement = authority?.Workflow.Requirements.SingleOrDefault(candidate =>
            !candidate.IsDeleted && candidate.Id == request.RequirementId);
        RegistrationChannel? channel = requirement?.Channels.SingleOrDefault(candidate =>
            !candidate.IsDeleted && !candidate.IsNative && candidate.Id == request.ChannelId &&
            candidate.RegistrationProviderBindingId == request.BindingId);
        RegistrationProviderBinding? binding = channel is null
            ? null
            : await providerRepository.GetBindingAsync(request.TenantId, request.BindingId, cancellationToken);
        if (order is null || authority is null || requirement is null || channel is null || binding is null ||
            binding.RegistrationFormId != request.FormId || binding.RegistrationFormVersionId != request.FormVersionId ||
            binding.PublishedMappingRevisionHash is null)
        {
            return Missing();
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        DateTime expiresAt = order.ExpiresAt is { } orderExpiry && orderExpiry < now.AddHours(1)
            ? orderExpiry
            : now.AddHours(1);
        if (expiresAt <= now)
        {
            return Missing();
        }

        IReadOnlyList<RegistrationParticipant> participants = await participantRepository.GetParticipantsByOrderAsync(
            order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationTicketAssignment> assignments = await participantRepository
            .GetAssignmentsWithParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken);
        IReadOnlyList<RegistrationRequirementFulfillment> fulfillments = await finalization.GetFulfillmentsAsync(
            request.TenantId, order.Id, requirement.Id, cancellationToken);
        IReadOnlyList<NativeRegistrationAnswerSubjectDto> subjects = NativeRegistrationAttemptContractBuilder.Subjects(
            order, requirement, participants, assignments, fulfillments);
        NativeRegistrationRequirementProgressDto progress = NativeRegistrationAttemptContractBuilder.Progress(subjects);
        if (subjects.Count == 0 || progress.IsComplete)
        {
            return Missing();
        }

        GuestCapabilityTokenIssue capability = capabilities.Issue();
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            request.TenantId,
            request.EventId,
            request.OrderId,
            authority.Workflow.Id,
            requirement.Id,
            channel.Id,
            binding.RegistrationFormId,
            binding.RegistrationFormVersionId,
            capability.Hash,
            binding.Id,
            binding.PublishedMappingRevisionHash,
            now,
            expiresAt);

        NativeRegistrationProviderLaunchDescriptorDto? descriptor = await ProviderRegistrationLaunchDescriptorResolver.ResolveAsync(
            providerRepository,
            providerRegistry,
            request.TenantId,
            request.EventId,
            authority.Workflow.Id,
            requirement.Id,
            channel,
            binding.Id,
            attempt.Id,
            capability.RawToken,
            subjects,
            progress,
            cancellationToken);
        if (descriptor is not { Available: true })
        {
            return Missing();
        }

        bool persisted = request.SupersededAttemptId is { } supersededAttemptId
            ? await submissions.PersistReplacementAttemptAsync(
                attempt, supersededAttemptId, "restart-with-fallback", now, cancellationToken)
            : await PersistAttemptAsync(submissions, attempt, cancellationToken);
        if (!persisted)
        {
            return Missing();
        }

        return new(true, attempt.Id, descriptor);
    }

    private static RegistrationProviderAttemptResult Missing() =>
        new(false, Guid.Empty, null, "registration_provider_attempt_not_found");

    private static async Task<bool> PersistAttemptAsync(
        IRegistrationSubmissionRepository submissions,
        RegistrationAttempt attempt,
        CancellationToken cancellationToken)
    {
        await submissions.PersistAttemptAsync(attempt, cancellationToken);
        return true;
    }
}

public sealed class SubmitNativeRegistrationAttemptCommandHandler(
    IRegistrationInventoryRepository inventory,
    IRegistrationSubmissionRepository submissions,
    IRegistrationProviderRepository providerRepository,
    IRegistrationFormAuthoringRepository forms,
    IRegistrationParticipantRepository participantRepository,
    IRegistrationSensitiveValueProtector protector,
    IGuestCapabilityTokenService capabilities,
    IRegistrationProviderRegistry providerRegistry,
    TimeProvider timeProvider)
    : IRequestHandler<SubmitNativeRegistrationAttemptCommand, NativeRegistrationSubmissionResult>
{
    public async Task<NativeRegistrationSubmissionResult> Handle(
        SubmitNativeRegistrationAttemptCommand request,
        CancellationToken cancellationToken)
    {
        if (!(await new SubmitNativeRegistrationAttemptCommandValidator()
                .ValidateAsync(request, cancellationToken)).IsValid)
        {
            return Missing();
        }

        RegistrationAttempt? attempt = await submissions.GetAttemptAsync(
            request.TenantId, request.AttemptId, cancellationToken);
        RegistrationOrder? order = await inventory.GetOrderWithLinesAsync(
            request.OrderId, request.TenantId, cancellationToken);
        if (attempt is null || order is null ||
            order.RegistrationOrderStatusId != (int)RegistrationOrderStatusEnum.AwaitingRequirements ||
            order.RegistrationWorkflowVersionId != attempt.RegistrationWorkflowId ||
            attempt.EventId != request.EventId ||
            attempt.RegistrationOrderId != request.OrderId ||
            attempt.RegistrationRequirementId != request.RequirementId ||
            !capabilities.Matches(request.AttemptCapabilityToken, attempt.CapabilityTokenHash))
        {
            return Missing();
        }

        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        Guid expectedStamp = attempt.ConcurrencyStamp;
        RegistrationSubmission submission;
        try
        {
            RegistrationEvidenceHash evidence = RegistrationEvidenceHash.Create(
                Hash(JsonSerializer.SerializeToUtf8Bytes(request.Answers)));
            RegistrationTransportIdempotencyHash transport = RegistrationTransportIdempotencyHash.Create(
                Hash(Encoding.UTF8.GetBytes(request.IdempotencyKey!)));
            submission = attempt.RegistrationProviderBindingId is null
                ? attempt.SubmitNative(evidence, now, transport)
                : attempt.SubmitHeadlessProvider(evidence, now, transport);
        }
        catch (InvalidOperationException)
        {
            return Missing();
        }

        RegistrationSubmissionNormalizationDraft draft = await NormalizeRegistrationSubmissionCommandHandler.PrepareAsync(
            submission,
            request.Answers,
            order,
            await participantRepository.GetParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken),
            await participantRepository.GetAssignmentsWithParticipantsByOrderAsync(order.Id, request.TenantId, cancellationToken),
            submissions,
            forms,
            protector,
            timeProvider,
            cancellationToken);
        RegistrationProviderSubmissionWriteEffect? providerWriteEffect = await BuildProviderWriteEffectAsync(
            attempt, submission, now, cancellationToken);
        RegistrationSubmissionPersistenceResult persisted = await submissions.PersistAcceptedWithNormalizationAsync(
            attempt,
            submission,
            expectedStamp,
            draft.Answers,
            draft.ConsentRecords,
            draft.Issues,
            draft.Fulfillments,
            cancellationToken,
            providerWriteEffect);
        if (persisted.Submission is null || persisted.Outcome == RegistrationSubmissionPersistenceOutcome.AttemptUnavailable)
        {
            return Missing();
        }

        return new(draft.IsValid, persisted.Submission.Id, draft.SafeIssues,
            draft.IsValid ? null : "registration_submission_invalid");
    }

    private async Task<RegistrationProviderSubmissionWriteEffect?> BuildProviderWriteEffectAsync(
        RegistrationAttempt attempt,
        RegistrationSubmission submission,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (attempt.RegistrationProviderBindingId is not { } bindingId || attempt.ProviderMappingRevisionHash is null)
        {
            return null;
        }

        RegistrationProviderBinding? binding = await providerRepository.GetBindingAsync(attempt.TenantId, bindingId, cancellationToken);
        if (binding?.Connection is null ||
            binding.StateId != (int)RegistrationProviderBindingStateEnum.Published ||
            binding.CollectionModeId is not ((int)RegistrationProviderCollectionModeEnum.ProviderApi or
                (int)RegistrationProviderCollectionModeEnum.MirrorOnly) ||
            binding.PublishedMappingRevisionHash?.Value != attempt.ProviderMappingRevisionHash.Value ||
            !HasCapability(binding, RegistrationProviderCapabilityCodes.SubmissionSink))
        {
            return null;
        }

        RegistrationProviderTuple tuple = new(
            binding.Connection.ProviderCode,
            binding.Connection.ProviderDeploymentCode,
            binding.Connection.ApiVersion,
            binding.Connection.AdapterPolicyVersion,
            binding.Connection.ConformanceEvidenceRevision);
        return providerRegistry.TryResolve(tuple) is IRegistrationProviderSubmissionSink
            ? RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, now)
            : null;
    }

    private static bool HasCapability(RegistrationProviderBinding binding, string capabilityCode) =>
        binding.Capabilities.Any(capability => !capability.IsDeleted &&
            string.Equals(capability.CapabilityCode, capabilityCode, StringComparison.OrdinalIgnoreCase));

    private static string Hash(byte[] value) => Convert.ToBase64String(SHA256.HashData(value));

    private static NativeRegistrationSubmissionResult Missing() =>
        new(false, Guid.Empty, [], "registration_attempt_not_found");
}
