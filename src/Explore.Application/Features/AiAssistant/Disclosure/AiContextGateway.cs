using System;
using System.Collections.Generic;
using Explore.Domain.Enums;

namespace Explore.Application.Features.AiAssistant.Disclosure;

/// <summary>
/// Single choke point that turns raw entity field values into a sanitized
/// <see cref="AiContextSanitizedEnvelope"/> that is safe to embed in an AI prompt,
/// transcript, or MCP payload. Every AI-bound path must route through this gateway.
/// </summary>
/// <remarks>
/// <para>
/// Enforcement order (most restrictive wins, all evaluated for every field):
/// </para>
/// <list type="number">
///   <item>Unregistered field → <see cref="AiContextDisclosureRuleEnum.Deny"/> (drift control).</item>
///   <item>Special-category sensitivity → <see cref="AiContextDisclosureRuleEnum.Deny"/> at every tier.</item>
///   <item>Phase-4 gated field when PII disclosure disabled → <see cref="AiContextDisclosureRuleEnum.Deny"/>.</item>
///   <item>Instance-admin viewer scope → row-level Confidential/Restricted/Special fields denied (CTO #1).</item>
///   <item>Confidential/Restricted at non-local provider trust tiers → <see cref="AiContextDisclosureRuleEnum.Deny"/>.</item>
///   <item>Field key absent from consent grant set when sensitivity &gt; Internal → downgrade to Deny.</item>
///   <item>Otherwise apply the entry's local-model rule with appropriate redaction/aggregation.</item>
/// </list>
/// <para>
/// Any unexpected runtime error produces a per-entity <see cref="AiContextSanitizedEnvelope.Failed"/>
/// result with <c>gateway_internal_failure</c>; other entities in the same batch are unaffected.
/// </para>
/// </remarks>
public sealed class AiContextGateway : IAiContextGateway
{
    private const string InternalFailureCode = "gateway_internal_failure";
    private const string RedactedPlaceholder = "[REDACTED]";

    private readonly AiContextDisclosureRegistry _registry;
    private readonly IAiProviderTrustResolver _providerTrustResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="AiContextGateway"/> class.
    /// </summary>
    /// <param name="providerTrustResolver">Resolver that maps endpoint evidence to a provider trust tier.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="providerTrustResolver"/> is <see langword="null"/>.
    /// </exception>
    public AiContextGateway(IAiProviderTrustResolver providerTrustResolver)
        : this(AiContextDisclosureRegistry.CreateDefault(), providerTrustResolver)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AiContextGateway"/> class with an explicit registry.
    /// Primarily used by tests to swap in a narrower registry.
    /// </summary>
    /// <param name="registry">The field classification registry.</param>
    /// <param name="providerTrustResolver">Resolver that maps endpoint evidence to a provider trust tier.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when either argument is <see langword="null"/>.
    /// </exception>
    public AiContextGateway(
        AiContextDisclosureRegistry registry,
        IAiProviderTrustResolver providerTrustResolver)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _providerTrustResolver = providerTrustResolver ?? throw new ArgumentNullException(nameof(providerTrustResolver));
    }

    /// <inheritdoc/>
    public AiContextSanitizedEnvelope Sanitize(AiContextSanitizationInput request)
    {
        if (request is null)
        {
            return AiContextSanitizedEnvelope.Failed(
                string.Empty,
                InternalFailureCode,
                "Sanitization request was null.");
        }

        try
        {
            return SanitizeCore(request);
        }
        catch (Exception ex)
        {
            return AiContextSanitizedEnvelope.Failed(
                request.EntityName,
                InternalFailureCode,
                $"Gateway threw while sanitizing entity '{request.EntityName}': {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<AiContextSanitizedEnvelope> SanitizeMany(IReadOnlyList<AiContextSanitizationInput> requests)
    {
        if (requests is null || requests.Count == 0)
        {
            return Array.Empty<AiContextSanitizedEnvelope>();
        }

        var envelopes = new List<AiContextSanitizedEnvelope>(capacity: requests.Count);
        foreach (var request in requests)
        {
            envelopes.Add(Sanitize(request));
        }

        return envelopes;
    }

    private AiContextSanitizedEnvelope SanitizeCore(AiContextSanitizationInput request)
    {
        var disclosed = new List<AiContextDisclosedField>();
        var redactedNames = new List<string>();
        var deniedNames = new List<string>();

        foreach (var (fieldName, fieldValue) in request.Fields)
        {
            if (string.IsNullOrWhiteSpace(fieldName))
            {
                deniedNames.Add(fieldName ?? string.Empty);
                continue;
            }

            if (!_registry.TryGetEntry(request.EntityName, fieldName, out var entry))
            {
                deniedNames.Add(fieldName);
                continue;
            }

            var appliedRule = ResolveAppliedRule(entry, request);

            switch (appliedRule)
            {
                case AiContextDisclosureRuleEnum.Allow:
                    disclosed.Add(new AiContextDisclosedField(fieldName, fieldValue, appliedRule));
                    break;

                case AiContextDisclosureRuleEnum.Redact:
                    var redactedValue = RedactValue(fieldName, fieldValue);
                    disclosed.Add(new AiContextDisclosedField(fieldName, redactedValue, appliedRule));
                    redactedNames.Add(fieldName);
                    break;

                case AiContextDisclosureRuleEnum.Aggregate:
                    var aggregateValue = AggregateValue(fieldValue);
                    disclosed.Add(new AiContextDisclosedField(fieldName, aggregateValue, appliedRule));
                    redactedNames.Add(fieldName);
                    break;

                case AiContextDisclosureRuleEnum.Deny:
                default:
                    deniedNames.Add(fieldName);
                    break;
            }
        }

        return AiContextSanitizedEnvelope.Success(
            request.EntityName,
            disclosed,
            redactedNames,
            deniedNames);
    }

    private AiContextDisclosureRuleEnum ResolveAppliedRule(
        AiContextDisclosureEntry entry,
        AiContextSanitizationInput request)
    {
        if (entry.Sensitivity == AiContextSensitivityEnum.Special)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        if (entry.Phase4Gated && !request.PiiDisclosureEnabled)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        if ((int)entry.Sensitivity > (int)request.MaxSensitivity)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        if (request.ViewerScope == AiViewerScopeEnum.InstanceAdmin &&
            entry.Sensitivity is AiContextSensitivityEnum.Confidential
                or AiContextSensitivityEnum.Restricted)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        var tierRule = _registry.ResolveEffectiveRule(
            entry.EntityName,
            entry.FieldName,
            request.ProviderTrustTier,
            request.PiiDisclosureEnabled);

        if (tierRule == AiContextDisclosureRuleEnum.Deny)
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        var key = AiContextDisclosureEntry.BuildKey(entry.EntityName, entry.FieldName);
        var sensitivityIsAboveInternal =
            entry.Sensitivity is AiContextSensitivityEnum.Confidential
                or AiContextSensitivityEnum.Restricted
                or AiContextSensitivityEnum.Special;
        if (sensitivityIsAboveInternal && !request.GrantedFieldKeys.Contains(key))
        {
            return AiContextDisclosureRuleEnum.Deny;
        }

        return tierRule;
    }

    private static object? RedactValue(string fieldName, object? value)
    {
        if (value is null)
        {
            return null;
        }

        var fieldLower = fieldName.AsSpan();
        if (fieldLower.EndsWith("Email", StringComparison.OrdinalIgnoreCase) && value is string email)
        {
            return RedactEmail(email);
        }

        return value switch
        {
            string => RedactedPlaceholder,
            _ => RedactedPlaceholder,
        };
    }

    private static object? AggregateValue(object? value)
    {
        return null;
    }

    private static string RedactEmail(string email)
    {
        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
        {
            return RedactedPlaceholder;
        }

        var local = email.AsSpan(0, atIndex);
        var domain = email.AsSpan(atIndex);
        var prefix = local.Length > 0 ? local[0].ToString() : string.Empty;
        return $"{prefix}***{domain.ToString()}";
    }
}
