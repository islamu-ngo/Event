using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Features.RegistrationProviders.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Domain.Services.Registration;

namespace Explore.Persistence.Security;

/// <summary>
/// Closed own-write admission. Initial rows outside the exact operation remain byte-for-byte
/// equivalent, including identity, creation provenance and schema source. New rows must belong
/// to that operation's original lineage; observing them never changes the original target.
/// </summary>
internal static class HumanEventProviderSourceTransitions
{
    private static InvalidOperationException Drift() => new("human_event_provider_subordinate_source_changed");
    private static bool Same(HumanEventProviderSourceRow left, HumanEventProviderSourceRow right) =>
        left.Source == right.Source && left.Id == right.Id && left.TenantId == right.TenantId
        && left.Values.SequenceEqual(right.Values);
    private static HumanEventProviderSourceRow? Find(HumanEventProviderSourceFacts facts, HumanEventProviderSource source, Guid? id) =>
        facts.Of(source).SingleOrDefault(row => row.Id == id);
    private static bool EqualExcept(HumanEventProviderSourceRow before, HumanEventProviderSourceRow after, params string[] columns) =>
        before.Id == after.Id && before.TenantId == after.TenantId && before.Source == after.Source
        && before.Values.Count == after.Values.Count
        && before.Values.All(pair => columns.Contains(pair.Key, StringComparer.Ordinal)
            || after.Values.TryGetValue(pair.Key, out string? value) && pair.Value == value);
    private static bool ChangedOnly(HumanEventProviderSourceRow before, HumanEventProviderSourceRow after, params string[] columns) =>
        EqualExcept(before, after, [.. columns, "UpdatedAt", "UpdatedBy", "ConcurrencyStamp"]);
    private static bool EventOwned(HumanEventProviderSourceRow row, HumanEventProviderSourceTarget target) =>
        row.TenantId == target.TenantId && row.Identity("EventId") == target.EventId;
    private static bool Live(HumanEventProviderSourceRow row) =>
        !row.Values.ContainsKey("IsDeleted") || !row.Flag("IsDeleted");

    internal static void RequireUnchanged(HumanEventProviderSourceFacts expected, HumanEventProviderSourceFacts current)
    {
        if (!ReferenceEquals(expected.Target, current.Target) || expected.PrincipalId != current.PrincipalId
            || expected.SelectedTenantId != current.SelectedTenantId || expected.Rows.Length != current.Rows.Length
            || expected.Rows.Zip(current.Rows).Any(pair => !Same(pair.First, pair.Second)))
            throw Drift();
    }

    internal static void RequireTarget(HumanEventProviderSourceFacts facts)
    {
        var target = facts.Target;
        if (facts.Rows.Any(row => row.TenantId != target.TenantId)) throw Drift();
        void Binding(Guid? id)
        {
            if (Find(facts, HumanEventProviderSource.Binding, id) is not { } binding) return;
            var form = Find(facts, HumanEventProviderSource.Form, binding.Identity("RegistrationFormId"));
            var version = Find(facts, HumanEventProviderSource.Version, binding.Identity("RegistrationFormVersionId"));
            if (form is null || version is null || !EventOwned(form, target) || !EventOwned(version, target)
                || version.Identity("RegistrationFormId") != form.Id
                || Find(facts, HumanEventProviderSource.Connection, binding.Identity("RegistrationProviderConnectionId")) is null)
                throw Drift();
        }
        Binding(target.BindingId);
        if (Find(facts, HumanEventProviderSource.Form, target.FormId) is { } form && !EventOwned(form, target))
            throw Drift();
        if (Find(facts, HumanEventProviderSource.Version, target.FormVersionId) is { } version
            && (!EventOwned(version, target) || version.Identity("RegistrationFormId") != target.FormId))
            throw Drift();
        if (Find(facts, HumanEventProviderSource.Submission, target.SubmissionId) is { } submission)
        {
            if (!EventOwned(submission, target)) throw Drift();
            Binding(submission.Identity("RegistrationProviderBindingId"));
        }
        if (Find(facts, HumanEventProviderSource.Effect, target.EffectId) is { } effect)
        {
            if (!Guid.TryParseExact(effect["ProviderDecisionId"]?.Split(':', 2)[0], "N", out Guid bindingId)
                || Find(facts, HumanEventProviderSource.Binding, bindingId) is null
                || effect["Provider"] != "registration-provider"
                || effect["EffectKind"] is not ("registration.provider_submission" or "registration.provider_manual_import"))
                throw Drift();
            Binding(bindingId);
            var message = Find(facts, HumanEventProviderSource.Message, effect.Identity("IncomingWebhookMessageId"));
            if (message is null || message["Provider"] != effect["Provider"]
                || message["ProviderMessageId"] != effect["ProviderDecisionId"]
                || message["PayloadHash"] != effect["PayloadSha256"])
                throw Drift();
            foreach (var receipt in facts.Of(HumanEventProviderSource.Receipt).Where(row =>
                row.Identity("IncomingWebhookMessageId") == message.Id && row["EffectKind"] == effect["EffectKind"]))
                if (receipt["PayloadHash"] != effect["PayloadSha256"]
                    || receipt.Number("ProcessingGeneration") != effect.Number("ProcessingGeneration"))
                    throw Drift();
        }
    }

    internal static void RequirePermittedTransition(HumanEventProviderSourceFacts before,
        HumanEventProviderSourceFacts after, IRegistrationProviderRegistry registry)
    {
        if (!ReferenceEquals(before.Target, after.Target) || before.PrincipalId != after.PrincipalId
            || before.SelectedTenantId != after.SelectedTenantId) throw Drift();
        if (before.Rows.Length == after.Rows.Length && before.Rows.Zip(after.Rows).All(pair => Same(pair.First, pair.Second)))
            return;
        var target = before.Target;
        if (target.Operation == HumanEventProviderOperation.ResolveQueueItem
            && (target.SubmissionId is { } submissionId && submissionId != Guid.Empty)
                == (target.EffectId is { } effectId && effectId != Guid.Empty))
            throw Drift();
        var original = before.Rows.ToDictionary(row => (row.Source, row.Id));
        var current = after.Rows.ToDictionary(row => (row.Source, row.Id));
        var inserted = after.Rows.Where(row => !original.ContainsKey((row.Source, row.Id))).ToArray();
        var deleted = before.Rows.Where(row => !current.ContainsKey((row.Source, row.Id))).ToArray();
        var binding = Find(before, HumanEventProviderSource.Binding, target.BindingId);
        var finalBinding = Find(after, HumanEventProviderSource.Binding, target.BindingId);
        if (target.EffectId.HasValue && target.Operation is HumanEventProviderOperation.RetryEffect or HumanEventProviderOperation.ResolveQueueItem)
        {
            var retained = Find(before, HumanEventProviderSource.Binding, before.Resolved.BindingId);
            if (retained is null || !Live(retained)
                || Find(before, HumanEventProviderSource.Form, retained.Identity("RegistrationFormId")) is not { } retainedForm
                || !Live(retainedForm) || !EventOwned(retainedForm, target))
                throw Drift();
        }
        Guid? createdBinding = inserted.SingleOrDefault(row => row.Source == HumanEventProviderSource.Binding)?.Id;
        Guid? ownBinding = target.Operation == HumanEventProviderOperation.CreateBinding ? createdBinding : target.BindingId;
        var addedVersions = inserted.Where(row => row.Source == HumanEventProviderSource.Version).ToArray();
        var addedForms = inserted.Where(row => row.Source == HumanEventProviderSource.Form).ToArray();
        if (addedVersions.Length > 1 || addedForms.Length > 1
            || inserted.Count(row => row.Source == HumanEventProviderSource.Channel) > 1
            || inserted.Count(row => row.Source == HumanEventProviderSource.Issue) > 1
            || inserted.Count(row => row.Source == HumanEventProviderSource.Schema) > 1
            || inserted.Count(row => row.Source == HumanEventProviderSource.Subscription) > 1)
            throw Drift();
        if (target.Operation == HumanEventProviderOperation.ManualImport)
        {
            if (binding is null || !Live(binding)
                || Find(before, HumanEventProviderSource.Connection, binding.Identity("RegistrationProviderConnectionId")) is not { } connection
                || !Live(connection) || string.IsNullOrWhiteSpace(binding["ProviderSurveyId"])
                || !before.Of(HumanEventProviderSource.Capability).Any(row => Live(row)
                    && row.Identity("RegistrationProviderBindingId") == binding.Id
                    && string.Equals(row["CapabilityCode"], RegistrationProviderCapabilityCodes.Manual, StringComparison.OrdinalIgnoreCase)
                    && row["ProviderCode"] == connection["ProviderCode"]
                    && row["DeploymentKind"] == connection["ProviderDeploymentCode"]
                    && row["ApiVersion"] == connection["ApiVersion"]
                    && row["AdapterPolicyVersion"] == connection["AdapterPolicyVersion"]
                    && row["ConformanceEvidenceRevision"] == connection["ConformanceEvidenceRevision"]))
                throw Drift();
            var messages = inserted.Where(row => row.Source == HumanEventProviderSource.Message).ToArray();
            var effects = inserted.Where(row => row.Source == HumanEventProviderSource.Effect).ToArray();
            if (messages.Length > 500 || messages.Length != effects.Length
                || messages.Any(message => effects.Count(effect => effect.Identity("IncomingWebhookMessageId") == message.Id) != 1))
                throw Drift();
        }

        bool OwnChild(HumanEventProviderSourceRow row) =>
            row.TenantId == target.TenantId && row.Identity("RegistrationProviderBindingId") == ownBinding && ownBinding.HasValue;
        bool Replaceable(HumanEventProviderSourceRow row) => OwnChild(row) && (
            target.Operation == HumanEventProviderOperation.ReplaceMappings
                && row.Source is HumanEventProviderSource.FieldMapping or HumanEventProviderSource.OptionMapping
                && binding?.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft
                && !before.Of(HumanEventProviderSource.Submission).Any(value => Live(value)
                    && value.Identity("RegistrationProviderBindingId") == ownBinding)
            || target.Operation == HumanEventProviderOperation.UpdateBinding
                && row.Source == HumanEventProviderSource.Capability
                && binding?.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft);
        if (deleted.Any(row => !Replaceable(row))) throw Drift();

        foreach (var row in after.Rows)
        {
            if (row.TenantId != target.TenantId) throw Drift();
            if (!original.TryGetValue((row.Source, row.Id), out var old))
            {
                if (!NewRow(row)) throw Drift();
                continue;
            }
            if (Same(old, row)) continue;
            bool permitted = row.Source switch
            {
                HumanEventProviderSource.Binding when row.Id == target.BindingId => BindingChange(old, row),
                HumanEventProviderSource.Capability or HumanEventProviderSource.FieldMapping or HumanEventProviderSource.OptionMapping =>
                    Replaceable(old) && row.Flag("IsDeleted") && EqualExcept(old, row, "IsDeleted"),
                HumanEventProviderSource.Effect when row.Id == target.EffectId => EffectChange(old, row, target),
                HumanEventProviderSource.Channel => ChannelChange(old, row, target, before),
                HumanEventProviderSource.Connection when
                    (target.Operation is HumanEventProviderOperation.PublishBinding or HumanEventProviderOperation.Poll
                        && row.Id == binding?.Identity("RegistrationProviderConnectionId")
                    || target.Operation == HumanEventProviderOperation.ImportVersion && row.Id == target.ConnectionId) =>
                    ChangedOnly(old, row, "LastCredentialRefreshAt", "LastAccessValidatedAt"),
                _ => false
            };
            if (!permitted) throw Drift();
        }

        if (target.Operation == HumanEventProviderOperation.ReplaceMappings)
            RequireMappings(after, target);
        if (target.Operation is HumanEventProviderOperation.CreateBinding or HumanEventProviderOperation.UpdateBinding
            && ownBinding.HasValue)
            RequireCapabilities(after, ownBinding.Value, registry);
        if (target.Operation == HumanEventProviderOperation.ImportVersion && addedVersions.Length == 1)
            RequireImportedVersion(before, after, addedVersions[0], addedForms);
        if (target.Operation == HumanEventProviderOperation.UpsertChannel
            && target.ChannelRequest is { IsNative: false, RegistrationProviderBindingId: { } channelBinding })
        {
            var selected = Find(before, HumanEventProviderSource.Binding, channelBinding);
            if (selected is null || !Live(selected)
                || selected.Number("StateId") != (int)RegistrationProviderBindingStateEnum.Published) throw Drift();
        }
        return;

        bool BindingChange(HumanEventProviderSourceRow old, HumanEventProviderSourceRow row)
        {
            switch (target.Operation)
            {
                case HumanEventProviderOperation.UpdateBinding:
                    return old.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft
                        && BindingRequestMatches(row, target, old)
                        && ChangedOnly(old, row, "RegistrationProviderConnectionId", "RegistrationFormId", "RegistrationFormVersionId",
                            "PresentationModeId", "CollectionModeId", "CompletionModeId", "TrustLevelId",
                            "WebhookSecretBindingId", "ProviderSurveyId", "ProviderSurveyRevisionId", "ProviderWebhookId");
                case HumanEventProviderOperation.DeleteBinding:
                    return old.Number("StateId") != (int)RegistrationProviderBindingStateEnum.Published
                        && row.Flag("IsDeleted") && row["DeletedAt"] is not null
                        && ChangedOnly(old, row, "IsDeleted", "DeletedAt", "DeletedBy");
                case HumanEventProviderOperation.PublishBinding:
                    if (old.Number("StateId") != (int)RegistrationProviderBindingStateEnum.Draft
                        || old["ProviderSurveyId"] is not null && (row["ProviderSurveyId"] != old["ProviderSurveyId"]
                            || row["ProviderSurveyRevisionId"] != old["ProviderSurveyRevisionId"])
                        || old["ProviderWebhookId"] is not null && row["ProviderWebhookId"] != old["ProviderWebhookId"]
                        || row["WebhookSecretBindingId"] != old["WebhookSecretBindingId"]
                            && !(old["ProviderWebhookId"] is null && row["WebhookSecretBindingId"] is null))
                        return false;
                    // Acknowledged remote provisioning may be retained as draft evidence when
                    // the publication preflight rejects. It cannot alter publication state,
                    // mapping hashes, source form/version, connection, or existing identifiers.
                    if (row.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft)
                        return ChangedOnly(old, row, "ProviderSurveyId", "ProviderSurveyRevisionId",
                            "ProviderWebhookId", "WebhookSecretBindingId");
                    return row.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Published
                        && row.Number("DriftClassId") is >= 1 and <= 3 && row["PublishedAt"] is not null
                        && row["PublishedMappingRevisionHash"] == MappingHash(after, row)
                        && row["PublishedMappingRevisionHashKey"] == row["PublishedMappingRevisionHash"]
                        && ChangedOnly(old, row, "StateId", "DriftClassId", "PublishedAt", "PublishedMappingRevisionHash",
                            "PublishedMappingRevisionHashKey", "ProviderSurveyId", "ProviderSurveyRevisionId", "ProviderWebhookId",
                            "WebhookSecretBindingId");
                case HumanEventProviderOperation.ReplaceMappings:
                    return old.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft
                        && !before.Of(HumanEventProviderSource.Submission).Any(value => Live(value)
                            && value.Identity("RegistrationProviderBindingId") == row.Id)
                        && ChangedOnly(old, row);
                default:
                    return false;
            }
        }

        bool NewRow(HumanEventProviderSourceRow row)
        {
            switch (row.Source)
            {
                case HumanEventProviderSource.Binding:
                    return target.Operation == HumanEventProviderOperation.CreateBinding && BindingRequestMatches(row, target, null)
                        && Find(before, HumanEventProviderSource.Form, target.FormId) is { } selectedForm
                        && EventOwned(selectedForm, target) && Live(selectedForm)
                        && Find(before, HumanEventProviderSource.Version, target.FormVersionId) is { } selectedVersion
                        && EventOwned(selectedVersion, target) && Live(selectedVersion)
                        && selectedVersion.Identity("RegistrationFormId") == selectedForm.Id
                        && row.Number("StateId") == (int)RegistrationProviderBindingStateEnum.Draft
                        && row.Number("DriftClassId") == (int)RegistrationProviderDriftClassEnum.NoDrift
                        && row["PublishedAt"] is null && row["PublishedMappingRevisionHash"] is null && Live(row);
                case HumanEventProviderSource.Capability:
                    return target.Operation is HumanEventProviderOperation.CreateBinding or HumanEventProviderOperation.UpdateBinding
                        && OwnChild(row) && Live(row);
                case HumanEventProviderSource.FieldMapping:
                case HumanEventProviderSource.OptionMapping:
                    return target.Operation == HumanEventProviderOperation.ReplaceMappings && Replaceable(row) && Live(row);
                case HumanEventProviderSource.Subscription:
                    return target.Operation == HumanEventProviderOperation.PublishBinding && OwnChild(row)
                        && row["ProviderEventType"] == "RESPONSES" && row["WatchId"] == finalBinding?["ProviderWebhookId"]
                        && Live(row) && row.Number("ProcessingGeneration") == 0 && row["LeaseToken"] is null
                        && row["LeaseExpiresAt"] is null && row.Number("RenewalFailureCount") == 0 && row.Number("SweepFailureCount") == 0
                        && !before.Of(HumanEventProviderSource.Subscription).Any(value => Live(value)
                            && value.Identity("RegistrationProviderBindingId") == ownBinding && value["ProviderEventType"] == "RESPONSES");
                case HumanEventProviderSource.Issue:
                    if (target.Operation != HumanEventProviderOperation.ResolveQueueItem
                        || target.OriginalCommand is not ResolveRegistrationProviderQueueItemCommand resolution
                        || Find(before, HumanEventProviderSource.Submission, target.SubmissionId) is not { } submission) return false;
                    return EventOwned(row, target) && row.Identity("RegistrationSubmissionId") == submission.Id
                        && row.Identity("RegistrationAttemptId") == submission.Identity("RegistrationAttemptId")
                        && row.Identity("RegistrationFormVersionId") == submission.Identity("RegistrationFormVersionId")
                        && row["RegistrationFormFieldId"] is null
                        && row["Code"] == ("RESOLVED_" + SafeCategory(resolution.DecisionCode)).ToUpperInvariant()
                        && before.Of(HumanEventProviderSource.Issue).Any(value => value.Identity("RegistrationSubmissionId") == submission.Id)
                        && !before.Of(HumanEventProviderSource.Issue).Any(value => value.Identity("RegistrationSubmissionId") == submission.Id
                            && value["Code"]!.StartsWith("RESOLVED_", StringComparison.Ordinal));
                case HumanEventProviderSource.Channel:
                    return target.Operation == HumanEventProviderOperation.UpsertChannel && target.ChannelId is null
                        && ChannelRequestMatches(row, target) && EventOwned(row, target)
                        && row.Identity("RegistrationWorkflowId") == target.WorkflowId
                        && row.Identity("RegistrationRequirementId") == target.RequirementId && Live(row)
                        && Find(before, HumanEventProviderSource.Requirement, target.RequirementId) is { } requirement
                        && EventOwned(requirement, target) && requirement.Identity("RegistrationWorkflowId") == target.WorkflowId
                        && !before.Of(HumanEventProviderSource.Channel).Any(value =>
                            value.Identity("RegistrationRequirementId") == target.RequirementId && value.Number("Ordinal") == row.Number("Ordinal"));
                case HumanEventProviderSource.Schema:
                    if (target.Operation != HumanEventProviderOperation.ImportVersion || target.ImportRequest is not { } import) return false;
                    byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(row["ProviderSnapshotJson"]!));
                    return row.Identity("RegistrationProviderConnectionId") == target.ConnectionId
                        && row["ProviderSurveyId"] == import.ProviderSurveyId.Trim()
                        && (import.ProviderSurveyRevisionId is null || row["ProviderSurveyRevisionId"] == import.ProviderSurveyRevisionId.Trim())
                        && row.Number("SchemaAuthorityId") == (int)RegistrationProviderSchemaAuthorityEnum.ProviderDiscovered
                        && row["ProviderSnapshotSha256Hash"] == Convert.ToHexStringLower(hash)
                        && row["RevisionHash"] == Convert.ToBase64String(hash) && Live(row);
                case HumanEventProviderSource.Form:
                    return target.Operation == HumanEventProviderOperation.ImportVersion && target.ImportRequest is { FormId: null } formRequest
                        && before.Resolved.FormId is null
                        && EventOwned(row, target) && Live(row) && row["Namespace"] == FormVersionRules.NormalizeNamespace(formRequest.Namespace)
                        && row["Key"] == FormVersionRules.NormalizeKey(formRequest.Key) && row["Name"] == formRequest.Name.Trim();
                case HumanEventProviderSource.Version:
                    return target.Operation == HumanEventProviderOperation.ImportVersion && EventOwned(row, target);
                case HumanEventProviderSource.Section:
                case HumanEventProviderSource.Field:
                case HumanEventProviderSource.Option:
                    return target.Operation == HumanEventProviderOperation.ImportVersion && EventOwned(row, target)
                        && addedVersions.Length == 1 && row.Identity("RegistrationFormVersionId") == addedVersions[0].Id
                        && row.Identity("RegistrationFormId") == addedVersions[0].Identity("RegistrationFormId") && Live(row);
                case HumanEventProviderSource.Message:
                    return target.Operation == HumanEventProviderOperation.ManualImport && binding is not null
                        && row["Provider"] == "registration-provider" && row["EventType"] == "registration.provider_manual_import"
                        && row["ProviderMessageId"]!.StartsWith(binding.Id.ToString("N") + ":", StringComparison.Ordinal)
                        && row["PayloadClearedAt"] is null && row["PayloadRetentionUntil"] is not null
                        && row.Values.TryGetValue("_payloadBytes", out string? bytes) && bytes is not null
                        && row["PayloadHash"] == "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(bytes)))
                        && ManualEnvelopeMatches(row, binding, before);
                case HumanEventProviderSource.Effect:
                    var message = Find(after, HumanEventProviderSource.Message, row.Identity("IncomingWebhookMessageId"));
                    return target.Operation == HumanEventProviderOperation.ManualImport && binding is not null && message is not null
                        && row["Provider"] == "registration-provider" && row["EffectKind"] == "registration.provider_submission"
                        && row["ProviderDecisionId"]!.StartsWith(binding.Id.ToString("N") + ":", StringComparison.Ordinal)
                        && row["ProviderDecisionId"] == message["ProviderMessageId"] && row["PayloadSha256"] == message["PayloadHash"]
                        && row.Number("Status") == (int)OutboxMessageStatus.Pending && row.Number("ProcessingGeneration") == 1
                        && row.Number("ProcessingFence") == 0 && row.Number("AttemptCount") == 0
                        && new[] { "ProcessingLeaseOwner", "ProcessingLeaseToken", "ProcessingLeaseExpiresAt",
                            "ProcessingStartedAt", "NextAttemptAt", "CompletedAt", "DeadLetteredAt", "FailureCategory", "SafeDetail" }
                            .All(property => row[property] is null);
                default:
                    return false;
            }
        }
    }

    private static bool BindingRequestMatches(HumanEventProviderSourceRow row, HumanEventProviderSourceTarget target,
        HumanEventProviderSourceRow? old)
    {
        var request = target.BindingRequest!;
        return row.Identity("RegistrationProviderConnectionId") == request.ConnectionId
            && row.Identity("RegistrationFormId") == request.FormId && row.Identity("RegistrationFormVersionId") == request.FormVersionId
            && row.Number("PresentationModeId") == request.PresentationModeId && row.Number("CollectionModeId") == request.CollectionModeId
            && row.Number("CompletionModeId") == request.CompletionModeId && row.Number("TrustLevelId") == request.TrustLevelId
            && row.Identity("WebhookSecretBindingId") == request.WebhookSecretBindingId
            && row["ProviderSurveyId"] == (string.IsNullOrWhiteSpace(request.ProviderSurveyId) ? old?["ProviderSurveyId"] : request.ProviderSurveyId.Trim())
            && row["ProviderSurveyRevisionId"] == (string.IsNullOrWhiteSpace(request.ProviderSurveyId) ? old?["ProviderSurveyRevisionId"]
                : string.IsNullOrWhiteSpace(request.ProviderSurveyRevisionId) ? null : request.ProviderSurveyRevisionId.Trim())
            && row["ProviderWebhookId"] == (string.IsNullOrWhiteSpace(request.ProviderWebhookId) ? old?["ProviderWebhookId"] : request.ProviderWebhookId.Trim());
    }

    private static bool ManualEnvelopeMatches(HumanEventProviderSourceRow row, HumanEventProviderSourceRow binding,
        HumanEventProviderSourceFacts before)
    {
        var connection = Find(before, HumanEventProviderSource.Connection, binding.Identity("RegistrationProviderConnectionId")) ?? throw Drift();
        try
        {
            byte[] bytes = Convert.FromHexString(row["_payloadBytes"]!);
            using var payload = JsonDocument.Parse(bytes);
            using var headers = JsonDocument.Parse(row["HeadersJson"]!);
            var body = payload.RootElement;
            string response = body.GetProperty("responseId").GetString()!;
            DateTimeOffset At(string property) => DateTimeOffset.Parse(row[property]!, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            var received = At("ReceivedAt");
            return body.GetProperty("bindingId").GetGuid() == binding.Id
                && body.GetProperty("formId").GetString() == binding["ProviderSurveyId"]
                && body.GetProperty("providerCode").GetString() == connection["ProviderCode"]
                && body.GetProperty("contractVersion").GetString() == connection["ApiVersion"]
                && body.GetProperty("idempotencyKey").GetString() == binding["ProviderSurveyId"] + ":" + response
                && row["ProviderMessageId"] == binding.Id.ToString("N") + ":" + response
                && row["IdempotencyKey"] == row["ProviderMessageId"]
                && row["ContentType"] == "application/json" && row["ContentEncoding"] == "utf-8"
                && row["RetentionPolicyVersion"] == "registration-provider-manual-import-v1"
                && row.Number("PayloadProvenanceId") == (int)WebhookPayloadProvenance.ExactBytes
                && long.Parse(row["PayloadByteLength"]!, CultureInfo.InvariantCulture) == bytes.Length
                && At("VerifiedAt") == received && At("PayloadRetentionUntil") == received.AddDays(7)
                && At("ReplayWindowUntil") == received.AddDays(7)
                && At("ProcessingAttemptRetentionUntil") == received.AddDays(30)
                && At("DeadLetterEvidenceRetentionUntil") == received.AddDays(30)
                && At("OperationalLogRetentionUntil") == received.AddDays(30)
                && row.Number("StatusId") == (int)IncomingWebhookMessageStatus.Verified
                && row.Number("ProcessingGeneration") == 1 && row.Number("ProcessingFence") == 0
                && row.Number("AttemptCount") == 0
                && new[] { "WebhookConsumerProviderBindingId", "ProcessingLeaseOwner", "ProcessingLeaseToken",
                    "ProcessingLeaseExpiresAt", "ProcessingStartedAt", "NextAttemptAt", "ProcessedAt", "IgnoredAt",
                    "RejectedAt", "DeadLetteredAt", "PayloadConflictAt", "FailureCategory", "SafeDetail",
                    "SettledByEffectReceiptId", "SettledEffectKind", "SettlementSourceId" }.All(property => row[property] is null)
                && headers.RootElement.GetProperty("X-Registration-Callback-Provider").GetString() == connection["ProviderCode"]
                && !string.IsNullOrWhiteSpace(headers.RootElement.GetProperty("X-Registration-Verification-Receipt").GetString());
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            throw Drift();
        }
    }

    private static bool EffectChange(HumanEventProviderSourceRow old, HumanEventProviderSourceRow row, HumanEventProviderSourceTarget target)
    {
        string[] leaseColumns = ["ProcessingLeaseOwner", "ProcessingLeaseToken", "ProcessingLeaseExpiresAt", "ProcessingStartedAt"];
        if (leaseColumns.Any(column => row[column] is not null)) return false;
        if (target.Operation == HumanEventProviderOperation.RetryEffect
            && target.OriginalCommand is RetryRegistrationProviderParkedItemCommand retry)
            return old.Number("Status") == (int)OutboxMessageStatus.DeadLettered
                && old.Number("ProcessingGeneration") == retry.ExpectedProcessingGeneration
                && row.Number("Status") == (int)OutboxMessageStatus.Pending
                && row.Number("ProcessingGeneration") == checked(old.Number("ProcessingGeneration") + 1)
                && row.Number("AttemptCount") == 0 && row["NextAttemptAt"] is not null && row["DeadLetteredAt"] is null
                && row["FailureCategory"] is null && row["SafeDetail"] is null
                && ChangedOnly(old, row, [.. leaseColumns, "Status", "ProcessingGeneration", "AttemptCount",
                    "NextAttemptAt", "DeadLetteredAt", "FailureCategory", "SafeDetail"]);
        return target.Operation == HumanEventProviderOperation.ResolveQueueItem
            && target.OriginalCommand is ResolveRegistrationProviderQueueItemCommand resolution
            && row.Number("Status") == (int)OutboxMessageStatus.Completed && row["CompletedAt"] is not null
            && row["NextAttemptAt"] is null && row["FailureCategory"] == "organizer_" + SafeCategory(resolution.DecisionCode)
            && row["SafeDetail"] == "Organizer acknowledged registration provider reconciliation item."
            && ChangedOnly(old, row, [.. leaseColumns, "Status", "CompletedAt", "NextAttemptAt", "FailureCategory", "SafeDetail"]);
    }

    private static bool ChannelRequestMatches(HumanEventProviderSourceRow row, HumanEventProviderSourceTarget target) =>
        target.ChannelRequest is { } request && row.Number("Ordinal") == request.Ordinal
        && row.Flag("IsNative") == request.IsNative && row.Identity("RegistrationProviderBindingId") == request.RegistrationProviderBindingId
        && row.Identity("RegistrationProviderBindingKey") == (request.RegistrationProviderBindingId ?? Guid.Empty);

    private static bool ChannelChange(HumanEventProviderSourceRow old, HumanEventProviderSourceRow row,
        HumanEventProviderSourceTarget target, HumanEventProviderSourceFacts before)
    {
        if (!EventOwned(old, target) || old.Identity("RegistrationWorkflowId") != target.WorkflowId
            || old.Identity("RegistrationRequirementId") != target.RequirementId) return false;
        if (target.Operation == HumanEventProviderOperation.DeleteChannel)
            return row.Id == target.ChannelId && row.Flag("IsDeleted") && row["DeletedAt"] is not null
                && ChangedOnly(old, row, "IsDeleted", "DeletedAt", "DeletedBy");
        if (target.Operation != HumanEventProviderOperation.UpsertChannel || !ChannelRequestMatches(row, target)) return false;
        bool revival = target.ChannelId is null && old.Flag("IsDeleted")
            && old.Number("Ordinal") == target.ChannelRequest!.Ordinal
            && before.Of(HumanEventProviderSource.Channel).Count(value =>
                value.Identity("RegistrationRequirementId") == target.RequirementId
                && value.Number("Ordinal") == old.Number("Ordinal") && value.Flag("IsDeleted")) == 1;
        return (row.Id == target.ChannelId || revival) && !row.Flag("IsDeleted")
            && (!revival || row["DeletedAt"] is null && row["DeletedBy"] is null)
            && ChangedOnly(old, row, ["Ordinal", "IsNative", "RegistrationProviderBindingId", "RegistrationProviderBindingKey",
                .. revival ? new[] { "IsDeleted", "DeletedAt", "DeletedBy" } : Array.Empty<string>()]);
    }

    private static void RequireCapabilities(HumanEventProviderSourceFacts facts, Guid bindingId, IRegistrationProviderRegistry registry)
    {
        var binding = Find(facts, HumanEventProviderSource.Binding, bindingId) ?? throw Drift();
        var connection = Find(facts, HumanEventProviderSource.Connection, binding.Identity("RegistrationProviderConnectionId")) ?? throw Drift();
        if (!Live(connection)) throw Drift();
        if (binding.Identity("WebhookSecretBindingId") is { } secretId)
        {
            var secret = Find(facts, HumanEventProviderSource.SecretBinding, secretId);
            if (secret is null || secret["SettingKey"] != SecretDefinitionRegistry.Keys.RegistrationProviders.WebhookSecret
                || secret["Qualifier"] != bindingId.ToString("N")) throw Drift();
        }
        var tuple = new RegistrationProviderTuple(connection["ProviderCode"]!, connection["ProviderDeploymentCode"]!,
            connection["ApiVersion"]!, connection["AdapterPolicyVersion"]!, connection["ConformanceEvidenceRevision"]!);
        var capabilities = facts.Of(HumanEventProviderSource.Capability)
            .Where(row => row.Identity("RegistrationProviderBindingId") == bindingId && Live(row)).ToArray();
        string[] expected = registry.TryResolve(tuple)?.ProvenCapabilities.ToCodes().Order(StringComparer.Ordinal).ToArray() ?? [];
        if (!capabilities.Select(row => row["CapabilityCode"]!).Order(StringComparer.Ordinal).SequenceEqual(expected)
            || capabilities.Any(row => row["ProviderCode"] != tuple.ProviderCode || row["DeploymentKind"] != tuple.DeploymentKind
                || row["ApiVersion"] != tuple.ApiVersion || row["AdapterPolicyVersion"] != tuple.AdapterPolicyVersion
                || row["ConformanceEvidenceRevision"] != tuple.ConformanceEvidenceRevision)) throw Drift();
    }

    private static void RequireMappings(HumanEventProviderSourceFacts facts, HumanEventProviderSourceTarget target)
    {
        var fields = facts.Of(HumanEventProviderSource.FieldMapping).Where(row =>
            row.Identity("RegistrationProviderBindingId") == target.BindingId && Live(row)).ToArray();
        var options = facts.Of(HumanEventProviderSource.OptionMapping).Where(row =>
            row.Identity("RegistrationProviderBindingId") == target.BindingId && Live(row)).ToArray();
        if (fields.Length != target.FieldMappings.Length || options.Length != target.OptionMappings.Length
            || fields.Any(row => target.FieldMappings.Count(value => value.PlatformFieldKey.Trim() == row["PlatformFieldKey"]
                && value.ProviderFieldKey.Trim() == row["ProviderFieldKey"] && value.IsRequired == row.Flag("IsRequired")) != 1)
            || options.Any(row => !fields.Any(field => field.Id == row.Identity("RegistrationProviderFieldMappingId")
                && target.OptionMappings.Count(value => value.PlatformFieldKey.Trim() == field["PlatformFieldKey"]
                    && value.PlatformOptionKey.Trim() == row["PlatformOptionKey"] && value.ProviderOptionKey.Trim() == row["ProviderOptionKey"]) == 1)))
            throw Drift();
    }

    private static void RequireImportedVersion(HumanEventProviderSourceFacts before, HumanEventProviderSourceFacts after,
        HumanEventProviderSourceRow version, HumanEventProviderSourceRow[] addedForms)
    {
        var target = before.Target;
        var request = target.ImportRequest!;
        Guid formId = version.Identity("RegistrationFormId") ?? throw Drift();
        var form = Find(after, HumanEventProviderSource.Form, formId) ?? throw Drift();
        var schema = Find(after, HumanEventProviderSource.Schema, version.Identity("ExternalRegistrationProviderSchemaRevisionId")) ?? throw Drift();
        bool selectedForm = before.Resolved.FormId == formId
            || before.Resolved.FormId is null && request.FormId is null && addedForms.SingleOrDefault()?.Id == formId;
        int next = before.Of(HumanEventProviderSource.Version).Where(row => row.Identity("RegistrationFormId") == formId && Live(row))
            .Select(row => row.Number("Version")).DefaultIfEmpty().Max() + 1;
        if (!selectedForm || !EventOwned(form, target) || !Live(form) || !Live(version)
            || version.Number("Version") != next || version.Number("StatusId") != (int)RegistrationFormStatusEnum.Published
            || version["LanguageTag"] != FormVersionRules.NormalizeLanguageTag(request.LanguageTag)
            || version.Number("SourceKindId") != (int)RegistrationFormVersionSourceKindEnum.ExternalImported
            || version.Identity("ExternalRegistrationProviderConnectionId") != target.ConnectionId
            || schema.Identity("RegistrationProviderConnectionId") != target.ConnectionId
            || version["ExternalProviderSurveyId"] != request.ProviderSurveyId.Trim()
            || schema["ProviderSurveyId"] != version["ExternalProviderSurveyId"]
            || schema["ProviderSurveyRevisionId"] != version["ExternalProviderSurveyRevisionId"]
            || version["SourceTemplateFormId"] is not null || version["SourceTemplateVersionId"] is not null
            || version["ExternalImportMappingRevisionHash"] != ExternalMappingHash(after, version, schema)
            || version["PublishedAt"] is null
            || version["RetiredAt"] is not null || version["SchemaHash"] is null
            || version["DataSchemaArtifact"] is null || version["UiSchemaArtifact"] is null
            || version["LogicSchemaArtifact"] is null || version["MappingArtifact"] is null
            || schema.Number("DriftClassId") is < 1 or > 3)
            throw Drift();
        var sections = after.Of(HumanEventProviderSource.Section).Where(row => row.Identity("RegistrationFormVersionId") == version.Id).ToArray();
        var fields = after.Of(HumanEventProviderSource.Field).Where(row => row.Identity("RegistrationFormVersionId") == version.Id)
            .OrderBy(row => row.Number("Ordinal")).ToArray();
        var options = after.Of(HumanEventProviderSource.Option).Where(row => row.Identity("RegistrationFormVersionId") == version.Id).ToArray();
        using var document = JsonDocument.Parse(schema["ProviderSnapshotJson"]!);
        var schemaFields = document.RootElement.GetProperty("fields").EnumerateArray().ToArray();
        if (document.RootElement.GetProperty("schema").GetString() != "islamu.registration-provider-schema-snapshot.v1"
            || sections.Length != 1 || sections[0].Number("Ordinal") != 1 || sections[0]["Title"] != "Imported questions"
            || fields.Length == 0 || fields.Length != schemaFields.Length
            || after.Of(HumanEventProviderSource.Rule).Any(row => row.Identity("RegistrationFormVersionId") == version.Id))
            throw Drift();
        for (int index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var source = schemaFields[index];
            var type = Enum.TryParse<RegistrationFieldTypeEnum>(source.GetProperty("type").GetString(), true, out var parsed)
                ? parsed : RegistrationFieldTypeEnum.ShortText;
            type = type switch
            {
                RegistrationFieldTypeEnum.Consent => RegistrationFieldTypeEnum.Boolean,
                RegistrationFieldTypeEnum.Rating => RegistrationFieldTypeEnum.Integer,
                _ => type
            };
            if (field.Identity("RegistrationFormSectionId") != sections[0].Id || field.Number("Ordinal") != index + 1
                || field["Namespace"] != "external" || field["Key"] != FormVersionRules.NormalizeKey(source.GetProperty("key").GetString()!)
                || field["Label"] != source.GetProperty("label").GetString() || field.Number("FieldTypeId") != (int)type
                || field.Flag("IsRequired") != source.GetProperty("isRequired").GetBoolean()
                || field.Flag("IsMulti") != (type == RegistrationFieldTypeEnum.MultipleChoice)
                || field.Number("RetentionPolicyId") != 1 || field.Number("OrganizerVisibilityId") != (int)RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers
                || field.Flag("RequiresExplicitConsent") || !field.Flag("IsProviderTransferAllowed") || field.Flag("IsExportable")
                || field.Flag("IsAnalyticsRelevant") || field.Flag("IsOperationallyFilterable")
                || new[] { "ExportPurposeCode", "ConsentPurposeCode", "ConsentText", "ConsentTextVersion",
                    "MinLength", "MaxLength", "RegexPattern", "MinNumber", "MaxNumber", "MinDateTime", "MaxDateTime",
                    "AllowedUrlSchemes", "DeletedAt", "DeletedBy" }.Any(property => field[property] is not null))
                throw Drift();
            var fieldOptions = options.Where(row => row.Identity("RegistrationFormFieldId") == field.Id)
                .OrderBy(row => row.Number("Ordinal")).ToArray();
            var sourceOptions = source.GetProperty("options").EnumerateArray().ToArray();
            if (fieldOptions.Length != sourceOptions.Length) throw Drift();
            for (int optionIndex = 0; optionIndex < fieldOptions.Length; optionIndex++)
                if (fieldOptions[optionIndex].Identity("RegistrationFormSectionId") != sections[0].Id
                    || fieldOptions[optionIndex].Number("Ordinal") != optionIndex + 1
                    || fieldOptions[optionIndex]["Key"] != FormVersionRules.NormalizeKey(sourceOptions[optionIndex].GetProperty("key").GetString()!)
                    || fieldOptions[optionIndex]["Label"] != sourceOptions[optionIndex].GetProperty("label").GetString())
                    throw Drift();
        }
        if (options.Any(row => !fields.Any(field => row.Identity("RegistrationFormFieldId") == field.Id)))
            throw Drift();
    }

    private static string ExternalMappingHash(HumanEventProviderSourceFacts facts,
        HumanEventProviderSourceRow version, HumanEventProviderSourceRow schema)
    {
        var connection = Find(facts, HumanEventProviderSource.Connection,
            version.Identity("ExternalRegistrationProviderConnectionId")) ?? throw Drift();
        using var snapshot = JsonDocument.Parse(schema["ProviderSnapshotJson"]!);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", "islamu.external-import-mapping-revision.v1");
            writer.WriteString("importerPolicyVersion", connection["AdapterPolicyVersion"]);
            writer.WriteString("providerCode", connection["ProviderCode"]);
            writer.WriteString("providerDeploymentCode", connection["ProviderDeploymentCode"]);
            writer.WriteString("apiVersion", connection["ApiVersion"]);
            writer.WriteString("conformanceEvidenceRevision", connection["ConformanceEvidenceRevision"]);
            writer.WriteString("providerSurveyId", version["ExternalProviderSurveyId"]!.Trim());
            writer.WriteString("providerSurveyRevisionId", version["ExternalProviderSurveyRevisionId"]?.Trim() ?? string.Empty);
            writer.WriteStartArray("fields");
            foreach (var field in snapshot.RootElement.GetProperty("fields").EnumerateArray()
                .OrderBy(field => field.GetProperty("key").GetString(), StringComparer.Ordinal))
            {
                string key = field.GetProperty("key").GetString()!;
                writer.WriteStartObject();
                writer.WriteString("providerKey", key);
                writer.WriteString("providerType", field.GetProperty("type").GetString());
                writer.WriteBoolean("isRequired", field.GetProperty("isRequired").GetBoolean());
                writer.WriteString("mappingKey", $"external/{key}");
                writer.WriteStartArray("options");
                foreach (var option in field.GetProperty("options").EnumerateArray()
                    .OrderBy(option => option.GetProperty("key").GetString(), StringComparer.Ordinal))
                {
                    writer.WriteStartObject();
                    writer.WriteString("providerKey", option.GetProperty("key").GetString());
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Convert.ToBase64String(SHA256.HashData(stream.ToArray()));
    }

    private static string MappingHash(HumanEventProviderSourceFacts facts, HumanEventProviderSourceRow binding)
    {
        var connection = Find(facts, HumanEventProviderSource.Connection, binding.Identity("RegistrationProviderConnectionId")) ?? throw Drift();
        var normalized = new StringBuilder("registration-provider-mapping-v2\n");
        normalized.Append("form-version:").Append(binding.Identity("RegistrationFormVersionId")).Append('\n');
        string tuple = string.Join('|', connection["ProviderCode"], connection["ProviderDeploymentCode"], connection["ApiVersion"],
            connection["AdapterPolicyVersion"], connection["ConformanceEvidenceRevision"]);
        normalized.Append("tuple:").Append(tuple.Length).Append(':').Append(tuple).Append('\n');
        normalized.Append("provider-survey:").Append(binding["ProviderSurveyId"]?.Length ?? 0).Append(':').Append(binding["ProviderSurveyId"]).Append('\n');
        normalized.Append("provider-survey-revision:").Append(binding["ProviderSurveyRevisionId"]?.Length ?? 0).Append(':').Append(binding["ProviderSurveyRevisionId"]).Append('\n');
        var fields = facts.Of(HumanEventProviderSource.FieldMapping).Where(row =>
            row.Identity("RegistrationProviderBindingId") == binding.Id && Live(row)).ToArray();
        foreach (var field in fields.OrderBy(row => row["PlatformFieldKey"], StringComparer.Ordinal))
            normalized.Append("field:").Append(field["PlatformFieldKey"]!.Length).Append(':').Append(field["PlatformFieldKey"])
                .Append(':').Append(field["ProviderFieldKey"]!.Length).Append(':').Append(field["ProviderFieldKey"])
                .Append(':').Append(field.Flag("IsRequired") ? '1' : '0').Append('\n');
        var keys = facts.Of(HumanEventProviderSource.FieldMapping).Where(row =>
            row.Identity("RegistrationProviderBindingId") == binding.Id).ToDictionary(row => row.Id, row => row["PlatformFieldKey"]!);
        foreach (var option in facts.Of(HumanEventProviderSource.OptionMapping).Where(row =>
            row.Identity("RegistrationProviderBindingId") == binding.Id && Live(row))
            .OrderBy(row => keys[row.Identity("RegistrationProviderFieldMappingId")!.Value], StringComparer.Ordinal)
            .ThenBy(row => row["PlatformOptionKey"], StringComparer.Ordinal))
        {
            string field = keys[option.Identity("RegistrationProviderFieldMappingId")!.Value];
            normalized.Append("option:").Append(field.Length).Append(':').Append(field)
                .Append(':').Append(option["PlatformOptionKey"]!.Length).Append(':').Append(option["PlatformOptionKey"])
                .Append(':').Append(option["ProviderOptionKey"]!.Length).Append(':').Append(option["ProviderOptionKey"]).Append('\n');
        }
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(normalized.ToString())));
    }

    private static string SafeCategory(string value) => string.IsNullOrWhiteSpace(value) ? "none"
        : new string(value.Trim().ToLowerInvariant().Where(character => char.IsAsciiLetterOrDigit(character) || character == '_').Take(100).ToArray());
}
