using System.Collections.Immutable;
using Explore.Application.Authorization;
using Explore.Application.DTOs.RegistrationProviders;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IHumanEventProviderSourceFactory
{
    IHumanEventProviderSourceFence Create(HumanEventProviderSourceTarget target);
}

/// <summary>
/// Subordinate business custody, not an Event authorization decision. Observation and checkpoints
/// use the same original target outside a transaction. Acquisition belongs to the original
/// Serializable owner after its genuine policy, principal, route, assignment and support leases.
/// </summary>
public interface IHumanEventProviderSourceFence
{
    Task<HumanEventProviderSourceFacts> ObserveAsync(CancellationToken cancellationToken = default);
    Task ValidateObservationAsync(HumanEventProviderSourceFacts expected, CancellationToken cancellationToken = default);
    Task<IHumanEventProviderSourceLease> AcquireAsync(HumanEventProviderSourceFacts expected,
        IPolicySourceSetLease sourceSet, IHumanPrincipalSourceLease principal,
        IHumanAuthorizationRouteLease route, IHumanEventAssignmentSourceLease assignments,
        IHumanSupportAccessSourceLease support, CancellationToken cancellationToken = default);
}

public interface IHumanEventProviderSourceLease
{
    HumanEventProviderSourceFacts Facts { get; }
    /// <summary>Fresh physical reads and closed own-transition validation, never a tracked snapshot.</summary>
    Task<HumanEventProviderSourceFacts> ValidateAsync(CancellationToken cancellationToken = default);
}

public enum HumanEventProviderOperation
{
    Poll = 1, ManualImport = 2, RetryEffect = 3, ResolveQueueItem = 4,
    CreateBinding = 5, UpdateBinding = 6, DeleteBinding = 7, PublishBinding = 8,
    ReplaceMappings = 9, ImportVersion = 10, UpsertChannel = 11, DeleteChannel = 12
}

/// <summary>
/// Closed original command selector. Construction cannot accept a resource/action tuple, a
/// resolved binding supplied by a caller, or a second command after remote work. Mutable mapping
/// collection inputs are copied at capture. The Event root must equal the original request.
/// </summary>
public sealed class HumanEventProviderSourceTarget
{
    private HumanEventProviderSourceTarget(ISecureRequest command, Guid resolvedEventId)
    {
        OriginalCommand = command;
        switch (command)
        {
            case PollRegistrationProviderReconciliationCommand value:
                (Operation, TenantId, EventId, BindingId) = (HumanEventProviderOperation.Poll, value.TenantId, value.EventId, value.BindingId);
                break;
            case QueueManualRegistrationProviderImportCommand value:
                (Operation, TenantId, EventId, BindingId) = (HumanEventProviderOperation.ManualImport, value.TenantId, value.EventId, value.BindingId);
                break;
            case RetryRegistrationProviderParkedItemCommand value:
                (Operation, TenantId, EventId, SubmissionId, EffectId) = (HumanEventProviderOperation.RetryEffect, value.TenantId, value.EventId, value.SubmissionId, value.EffectOutboxId);
                break;
            case ResolveRegistrationProviderQueueItemCommand value:
                (Operation, TenantId, EventId, SubmissionId, EffectId) = (HumanEventProviderOperation.ResolveQueueItem, value.TenantId, value.EventId, value.SubmissionId, value.EffectOutboxId);
                break;
            case CreateRegistrationProviderBindingCommand value:
                (Operation, TenantId, EventId, BindingRequest) = (HumanEventProviderOperation.CreateBinding, value.TenantId, value.EventId, value.Request);
                break;
            case UpdateRegistrationProviderBindingCommand value:
                (Operation, TenantId, EventId, BindingId, BindingRequest) = (HumanEventProviderOperation.UpdateBinding, value.TenantId, value.EventId, value.BindingId, value.Request);
                break;
            case DeleteRegistrationProviderBindingCommand value:
                (Operation, TenantId, EventId, BindingId) = (HumanEventProviderOperation.DeleteBinding, value.TenantId, value.EventId, value.BindingId);
                break;
            case PublishEventRegistrationProviderBindingCommand value:
                (Operation, TenantId, EventId, BindingId) = (HumanEventProviderOperation.PublishBinding, value.TenantId, value.EventId, value.BindingId);
                break;
            case ReplaceEventDraftRegistrationProviderMappingsCommand value:
                (Operation, TenantId, EventId, BindingId) = (HumanEventProviderOperation.ReplaceMappings, value.TenantId, value.EventId, value.BindingId);
                FieldMappings = value.Request.FieldMappings.ToImmutableArray();
                OptionMappings = value.Request.OptionMappings.ToImmutableArray();
                break;
            case ImportExternalRegistrationProviderFormVersionCommand value:
                (Operation, TenantId, EventId, ConnectionId, FormId, ImportRequest) =
                    (HumanEventProviderOperation.ImportVersion, value.TenantId, value.EventId, value.ConnectionId, value.Request.FormId, value.Request);
                break;
            case UpsertRegistrationChannelCommand value:
                (Operation, TenantId, EventId, WorkflowId, RequirementId, ChannelId, ChannelRequest) =
                    (HumanEventProviderOperation.UpsertChannel, value.TenantId, value.EventId, value.WorkflowId, value.RequirementId, value.ChannelId, value.Request);
                BindingId = value.Request.RegistrationProviderBindingId;
                break;
            case DeleteRegistrationChannelCommand value:
                (Operation, TenantId, EventId, WorkflowId, RequirementId, ChannelId) =
                    (HumanEventProviderOperation.DeleteChannel, value.TenantId, value.EventId, value.WorkflowId, value.RequirementId, value.ChannelId);
                break;
            default:
                throw new NotSupportedException("The command is not an Event-provider subordinate source operation.");
        }

        if (BindingRequest is { } binding)
            (ConnectionId, FormId, FormVersionId) = (binding.ConnectionId, binding.FormId, binding.FormVersionId);
        if (TenantId == Guid.Empty || EventId == Guid.Empty || EventId != resolvedEventId)
            throw new ArgumentException("The original tenant and resolved Event root must agree.");
    }

    public ISecureRequest OriginalCommand { get; }
    public HumanEventProviderOperation Operation { get; }
    public Guid TenantId { get; }
    public Guid EventId { get; }
    public Guid? BindingId { get; }
    public Guid? ConnectionId { get; }
    public Guid? FormId { get; }
    public Guid? FormVersionId { get; }
    public Guid? SubmissionId { get; }
    public Guid? EffectId { get; }
    public Guid? WorkflowId { get; }
    public Guid? RequirementId { get; }
    public Guid? ChannelId { get; }
    public RegistrationProviderBindingRequestDto? BindingRequest { get; }
    public ImportExternalRegistrationProviderFormVersionRequestDto? ImportRequest { get; }
    public RegistrationChannelRequestDto? ChannelRequest { get; }
    public ImmutableArray<RegistrationProviderFieldMappingDto> FieldMappings { get; } = [];
    public ImmutableArray<RegistrationProviderOptionMappingDto> OptionMappings { get; } = [];

    public static HumanEventProviderSourceTarget From(ISecureRequest originalCommand, Guid resolvedEventId) =>
        new(originalCommand ?? throw new ArgumentNullException(nameof(originalCommand)), resolvedEventId);

    public override string ToString() => nameof(HumanEventProviderSourceTarget);
}

/// <summary>Closed physical source vocabulary; no caller-defined table or column capability.</summary>
public enum HumanEventProviderSource
{
    Binding = 1, Connection, Capability, ApprovedOrigin, Form, Version, Section, Field, Option,
    Rule, FieldMapping, OptionMapping, Schema, Subscription, Submission, Issue, Effect, Message,
    Receipt, Workflow, Requirement, Channel, SecretBinding
}

/// <summary>
/// Immutable physical scalar image. Values are canonical provider scalars, keyed by model
/// property, not mutable EF entities. The closed registry verifies every included column.
/// For existing rows, an added scalar remains immutable until its transition policy explicitly permits it.
/// </summary>
public sealed record HumanEventProviderSourceRow(
    HumanEventProviderSource Source, Guid Id, Guid TenantId,
    ImmutableSortedDictionary<string, string?> Values)
{
    public string? this[string property] => Values[property];
    public Guid? Identity(string property) => this[property] is { } value ? Guid.Parse(value) : null;
    public int Number(string property) => int.Parse(this[property]!, System.Globalization.CultureInfo.InvariantCulture);
    public bool Flag(string property) => this[property] == "true";
    public override string ToString() => nameof(HumanEventProviderSourceRow);
}

public sealed record HumanEventProviderSourceFacts(
    Guid PrincipalId, Guid SelectedTenantId, HumanEventProviderSourceTarget Target,
    ImmutableArray<HumanEventProviderSourceRow> Rows)
{
    public HumanEventProviderResolvedTarget Resolved { get; } = Resolve(Target, Rows);
    public IEnumerable<HumanEventProviderSourceRow> Of(HumanEventProviderSource source) =>
        Rows.Where(row => row.Source == source);
    public override string ToString() => nameof(HumanEventProviderSourceFacts);

    private static HumanEventProviderResolvedTarget Resolve(HumanEventProviderSourceTarget target,
        ImmutableArray<HumanEventProviderSourceRow> rows)
    {
        HumanEventProviderSourceRow? Find(HumanEventProviderSource source, Guid? id) =>
            rows.SingleOrDefault(row => row.Source == source && row.Id == id);
        var submission = Find(HumanEventProviderSource.Submission, target.SubmissionId);
        var effect = Find(HumanEventProviderSource.Effect, target.EffectId);
        Guid? bindingId = target.BindingId ?? submission?.Identity("RegistrationProviderBindingId");
        if (bindingId is null && Guid.TryParseExact(effect?["ProviderDecisionId"]?.Split(':', 2)[0], "N", out Guid parsed))
            bindingId = parsed;
        var binding = Find(HumanEventProviderSource.Binding, bindingId);
        Guid? formId = binding?.Identity("RegistrationFormId") ?? target.FormId;
        if (target.ImportRequest is { FormId: null } import)
        {
            var candidates = rows.Where(row => row.Source == HumanEventProviderSource.Version
                && !row.Flag("IsDeleted") && row.Identity("EventId") == target.EventId
                && row.Number("SourceKindId") == (int)RegistrationFormVersionSourceKindEnum.ExternalImported
                && row.Identity("ExternalRegistrationProviderConnectionId") == target.ConnectionId
                && row["ExternalProviderSurveyId"] == import.ProviderSurveyId.Trim()
                && rows.Any(form => form.Source == HumanEventProviderSource.Form && !form.Flag("IsDeleted")
                    && form.Id == row.Identity("RegistrationFormId") && form.Identity("EventId") == target.EventId))
                .Select(row => row.Identity("RegistrationFormId")!.Value).Distinct().ToArray();
            if (candidates.Length > 1)
                throw new InvalidOperationException("human_event_provider_import_form_ambiguous");
            formId = candidates.Length == 0 ? null : candidates[0];
        }
        return new(target.EventId, bindingId, binding?.Identity("RegistrationProviderConnectionId") ?? target.ConnectionId,
            formId, binding?.Identity("RegistrationFormVersionId") ?? target.FormVersionId,
            target.SubmissionId, target.EffectId, effect?.Identity("IncomingWebhookMessageId"),
            target.WorkflowId, target.RequirementId, target.ChannelId);
    }
}

/// <summary>
/// Persisted resolution for the original command. Null represents an observed absence or an
/// operation that creates that identity, never permission to select a different existing row.
/// Requested replacement form/connection IDs remain separately fixed on Target.
/// </summary>
public sealed record HumanEventProviderResolvedTarget(Guid EventId, Guid? BindingId, Guid? ConnectionId,
    Guid? FormId, Guid? FormVersionId, Guid? SubmissionId, Guid? EffectId, Guid? MessageId,
    Guid? WorkflowId, Guid? RequirementId, Guid? ChannelId);
