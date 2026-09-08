using System.Globalization;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;
using FluentValidation;
using MediatR;

namespace Explore.Application.Services.Registration.Commands;

public sealed record DrainRegistrationProviderSubmissionWriteEffectsCommand(
    string LeaseOwner,
    int BatchSize = 100,
    int LeaseSeconds = 60) : IRequest<int>;

public sealed class DrainRegistrationProviderSubmissionWriteEffectsCommandValidator
    : AbstractValidator<DrainRegistrationProviderSubmissionWriteEffectsCommand>
{
    public DrainRegistrationProviderSubmissionWriteEffectsCommandValidator()
    {
        RuleFor(command => command.LeaseOwner).NotEmpty().MaximumLength(RegistrationProviderSubmissionWriteEffect.MaxLeaseOwnerLength);
        RuleFor(command => command.BatchSize).InclusiveBetween(1, 1000);
        RuleFor(command => command.LeaseSeconds).InclusiveBetween(1, 3600);
    }
}

public sealed class DrainRegistrationProviderSubmissionWriteEffectsCommandHandler(
    IRegistrationProviderSubmissionWriteEffectRepository effects,
    IRegistrationProviderRegistry providerRegistry,
    ITenantContextAccessor tenantContextAccessor,
    IRegistrationSensitiveValueProtector sensitiveValueProtector,
    TimeProvider timeProvider)
    : IRequestHandler<DrainRegistrationProviderSubmissionWriteEffectsCommand, int>
{
    private const int MaxAttempts = 5;

    public async Task<int> Handle(
        DrainRegistrationProviderSubmissionWriteEffectsCommand request,
        CancellationToken cancellationToken)
    {
        await new DrainRegistrationProviderSubmissionWriteEffectsCommandValidator().ValidateAndThrowAsync(request, cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        IReadOnlyList<RegistrationProviderSubmissionWriteClaim> claims = await effects.ClaimDueAsync(
            request.LeaseOwner, request.BatchSize, now, TimeSpan.FromSeconds(request.LeaseSeconds), cancellationToken);
        int completed = 0;
        foreach (RegistrationProviderSubmissionWriteClaim claim in claims)
        {
            tenantContextAccessor.SetTenant(claim.TenantId);
            try
            {
                completed += await DrainOneAsync(claim, cancellationToken) ? 1 : 0;
            }
            finally
            {
                tenantContextAccessor.Clear();
            }
        }

        return completed;
    }

    private async Task<bool> DrainOneAsync(RegistrationProviderSubmissionWriteClaim claim, CancellationToken cancellationToken)
    {
        RegistrationProviderSubmissionWriteDelivery? delivery = await effects.GetDeliveryAsync(claim, cancellationToken);
        DateTime now = timeProvider.GetUtcNow().UtcDateTime;
        if (delivery is null)
        {
            await SettleFailureAsync(claim, "provider_delivery_graph_missing", retryable: false, ambiguous: false, now, cancellationToken);
            return false;
        }

        if (!AnonymousRegistrationRetentionPolicy.CanDisclose(delivery.Order, null, now))
        {
            await SettleFailureAsync(claim, "registration_data_retention_expired", retryable: false, ambiguous: false,
                now, cancellationToken);
            return false;
        }

        if (!TryResolveSink(delivery.Binding, out RegistrationProviderTuple tuple, out IRegistrationProviderSubmissionSink? sink))
        {
            await SettleFailureAsync(claim, "provider_submission_sink_unavailable", retryable: false, ambiguous: false, now, cancellationToken);
            return false;
        }

        IReadOnlyDictionary<string, string> answers = BuildProviderAnswers(delivery, out DateTime? disclosureUntilUtc, out bool retentionExpired);
        if (answers.Count == 0)
        {
            if (retentionExpired)
            {
                await SettleFailureAsync(claim, "registration_data_retention_expired", retryable: false, ambiguous: false,
                    timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
                return false;
            }

            if (HasTransferableMappings(delivery))
            {
                await SettleFailureAsync(claim, "provider_submission_mapped_answers_empty", retryable: false, ambiguous: true, now, cancellationToken);
                return false;
            }

            return await effects.CompleteAsync(claim, now, cancellationToken);
        }

        try
        {
            RegistrationProviderSubmissionSinkResult result = await sink.AcceptAsync(
                new RegistrationProviderSubmissionSinkRequest(
                    claim.TenantId,
                    delivery.Binding,
                    delivery.Binding.Connection!,
                    tuple,
                    claim.RegistrationAttemptId,
                    claim.RegistrationSubmissionId,
                    answers,
                    null) { DisclosureUntilUtc = disclosureUntilUtc },
                cancellationToken);
            if (result.Accepted)
            {
                return await effects.CompleteAsync(claim, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            }

            await SettleFailureAsync(claim, "provider_submission_rejected", retryable: false, ambiguous: false,
                timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
            return false;
        }
        catch (RegistrationProviderSubmissionDeliveryException exception)
        {
            await SettleFailureAsync(
                claim,
                exception.FailureCode,
                exception.FailureKind == RegistrationProviderSubmissionDeliveryFailureKind.RetryableBeforeHandoff,
                exception.FailureKind == RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff,
                timeProvider.GetUtcNow().UtcDateTime,
                cancellationToken);
            return false;
        }
    }

    private bool TryResolveSink(
        RegistrationProviderBinding binding,
        out RegistrationProviderTuple tuple,
        out IRegistrationProviderSubmissionSink? sink)
    {
        sink = null;
        tuple = binding.Connection is null
            ? RegistrationProviderTuple.Empty
            : new RegistrationProviderTuple(binding.Connection.ProviderCode, binding.Connection.ProviderDeploymentCode,
                binding.Connection.ApiVersion, binding.Connection.AdapterPolicyVersion,
                binding.Connection.ConformanceEvidenceRevision);
        if (tuple == RegistrationProviderTuple.Empty || providerRegistry.TryResolve(tuple) is not IRegistrationProviderSubmissionSink resolved ||
            !binding.Capabilities.Any(capability => !capability.IsDeleted &&
                string.Equals(capability.CapabilityCode, RegistrationProviderCapabilityCodes.SubmissionSink, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        sink = resolved;
        return true;
    }

    private IReadOnlyDictionary<string, string> BuildProviderAnswers(
        RegistrationProviderSubmissionWriteDelivery delivery,
        out DateTime? disclosureUntilUtc,
        out bool retentionExpired)
    {
        disclosureUntilUtc = null;
        retentionExpired = false;
        Dictionary<Guid, RegistrationFormField> fields = delivery.Fields.ToDictionary(field => field.Id);
        Dictionary<Guid, string> platformFieldKeys = fields.ToDictionary(pair => pair.Key, pair => $"{pair.Value.Namespace}.{pair.Value.Key}");
        Dictionary<string, RegistrationProviderFieldMapping> providerFieldMappings = delivery.Binding.FieldMappings
            .Where(mapping => !mapping.IsDeleted)
            .ToDictionary(mapping => mapping.PlatformFieldKey, StringComparer.Ordinal);
        Dictionary<(Guid FieldMappingId, string OptionKey), string> providerOptionKeys = delivery.Binding.OptionMappings
            .Where(mapping => !mapping.IsDeleted)
            .ToDictionary(mapping => (mapping.RegistrationProviderFieldMappingId, mapping.PlatformOptionKey), mapping => mapping.ProviderOptionKey);
        Dictionary<string, List<string>> values = new(StringComparer.Ordinal);

        foreach (RegistrationAnswer answer in delivery.Answers.OrderBy(answer => answer.Ordinal).ThenBy(answer => answer.Id))
        {
            if (!fields.TryGetValue(answer.RegistrationFormFieldId, out RegistrationFormField? field) ||
                !field.IsProviderTransferAllowed ||
                !platformFieldKeys.TryGetValue(answer.RegistrationFormFieldId, out string? platformFieldKey) ||
                !providerFieldMappings.TryGetValue(platformFieldKey, out RegistrationProviderFieldMapping? fieldMapping))
            {
                continue;
            }

            DateTime? rowDeadline = answer.RetentionUntil;
            if (answer.SensitiveAnswerValue?.RetentionUntil is { } sensitiveDeadline &&
                (rowDeadline is null || sensitiveDeadline < rowDeadline))
            {
                rowDeadline = sensitiveDeadline;
            }

            if (!AnonymousRegistrationRetentionPolicy.CanDisclose(delivery.Order, rowDeadline, timeProvider.GetUtcNow().UtcDateTime))
            {
                retentionExpired = true;
                continue;
            }

            if (ToProviderValue(answer, field, fieldMapping.Id, providerOptionKeys) is not { } value)
            {
                continue;
            }

            DateTime? answerDeadline = AnonymousRegistrationRetentionPolicy.GetDisclosureDeadline(delivery.Order, rowDeadline);
            if (answerDeadline is { } deadline && (disclosureUntilUtc is null || deadline < disclosureUntilUtc))
            {
                disclosureUntilUtc = deadline;
            }

            if (!values.TryGetValue(fieldMapping.ProviderFieldKey, out List<string>? fieldValues))
            {
                fieldValues = [];
                values[fieldMapping.ProviderFieldKey] = fieldValues;
            }

            fieldValues.Add(value);
        }

        return values.ToDictionary(pair => pair.Key, pair => string.Join(", ", pair.Value), StringComparer.Ordinal);
    }

    private static bool HasTransferableMappings(RegistrationProviderSubmissionWriteDelivery delivery)
    {
        HashSet<string> transferableFieldKeys = [.. delivery.Fields
            .Where(field => field.IsProviderTransferAllowed)
            .Select(field => $"{field.Namespace}.{field.Key}")];
        return delivery.Binding.FieldMappings.Any(mapping =>
            !mapping.IsDeleted && transferableFieldKeys.Contains(mapping.PlatformFieldKey));
    }

    private string? ToProviderValue(
        RegistrationAnswer answer,
        RegistrationFormField field,
        Guid fieldMappingId,
        IReadOnlyDictionary<(Guid FieldMappingId, string OptionKey), string> providerOptionKeys)
    {
        if (answer.TextValue is { } text) return NeutralizeFormula(text);
        if (answer.IntegerValue is { } integer) return integer.ToString(CultureInfo.InvariantCulture);
        if (answer.DecimalValue is { } decimalValue) return decimalValue.ToString(CultureInfo.InvariantCulture);
        if (answer.BooleanValue is { } boolean) return boolean ? "true" : "false";
        if (answer.DateValue is { } date) return date.ToString("O", CultureInfo.InvariantCulture);
        if (answer.TimeValue is { } time) return time.ToString("O", CultureInfo.InvariantCulture);
        if (answer.InstantValue is { } instant) return instant.ToString("O", CultureInfo.InvariantCulture);
        if (answer.SelectedOptionId is { } optionId && field.Options.FirstOrDefault(option => option.Id == optionId) is { } option)
        {
            return providerOptionKeys.TryGetValue((fieldMappingId, option.Key), out string? providerOptionKey)
                ? providerOptionKey
                : option.Key;
        }

        if (answer.SensitiveAnswerValue is { } sensitive)
        {
            return NeutralizeFormula(sensitiveValueProtector.Unprotect(sensitive.Ciphertext, sensitive.KeyVersion));
        }

        return null;
    }

    private static string NeutralizeFormula(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            ? "'" + value
            : value;

    private async Task SettleFailureAsync(
        RegistrationProviderSubmissionWriteClaim claim,
        string failureCode,
        bool retryable,
        bool ambiguous,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (ambiguous)
        {
            await effects.ParkAmbiguousAsync(claim, failureCode, now, cancellationToken);
        }
        else if (!retryable || claim.AttemptCount >= MaxAttempts)
        {
            await effects.DeadLetterAsync(claim, failureCode, now, cancellationToken);
        }
        else
        {
            await effects.RetryAsync(claim, failureCode, now.Add(Backoff(claim.AttemptCount)), now, cancellationToken);
        }
    }

    private static TimeSpan Backoff(int attemptCount) => TimeSpan.FromMinutes(Math.Min(30, Math.Pow(2, Math.Max(0, attemptCount - 1))));
}
