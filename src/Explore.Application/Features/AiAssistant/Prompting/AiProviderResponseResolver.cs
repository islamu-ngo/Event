using Explore.Application.Contracts.Infrastructure.Ai;
using Explore.Application.Features.AiAssistant.Tools;
using Explore.Domain.Ai;

namespace Explore.Application.Features.AiAssistant.Prompting;

public sealed class AiProviderResponseResolver
{
    private const string DefaultFailureCode = "invalid_tool_arguments";
    private const string DefaultFailureMessage = "AI provider returned invalid action payload JSON.";
    private const string InvalidResponseFailureCode = "invalid_response";

    private readonly IAiChatProvider _chatProvider;
    private readonly AiStructuredActionParser _structuredActionParser;

    public AiProviderResponseResolver(
        IAiChatProvider chatProvider,
        AiStructuredActionParser structuredActionParser)
    {
        _chatProvider = chatProvider;
        _structuredActionParser = structuredActionParser;
    }

    public async Task<AiProviderResponseResolution> ResolveAsync(
        AiChatPayload providerPayload,
        CancellationToken cancellationToken)
    {
        var firstAttempt = await SendAndParseAsync(providerPayload, cancellationToken);
        if (firstAttempt.Succeeded)
        {
            return firstAttempt;
        }

        if (firstAttempt.ParseResult is not null)
        {
            var correctionPayload = providerPayload with
            {
                Messages = providerPayload.Messages
                    .Append(new AiChatMessage(AiMessageRole.User, BuildSelfCorrectionMessage(firstAttempt.ParseResult)))
                    .ToList()
            };

            return await SendAndParseAsync(correctionPayload, cancellationToken);
        }

        if (!ShouldRetryEmptyProviderResponse(firstAttempt))
        {
            return firstAttempt;
        }

        var emptyResponseCorrectionPayload = providerPayload with
        {
            Messages = providerPayload.Messages
                .Append(new AiChatMessage(AiMessageRole.User, BuildEmptyResponseCorrectionMessage(providerPayload)))
                .ToList()
        };

        return await SendAndParseAsync(emptyResponseCorrectionPayload, cancellationToken);
    }

    private async Task<AiProviderResponseResolution> SendAndParseAsync(
        AiChatPayload providerPayload,
        CancellationToken cancellationToken)
    {
        var providerResult = await _chatProvider.SendAsync(providerPayload, cancellationToken);
        if (!providerResult.Succeeded || providerResult.Response is null)
        {
            return AiProviderResponseResolution.Failure(
                providerResult.Error?.Code ?? "provider_failure",
                providerResult.Error?.Message ?? "AI provider failed.");
        }

        var parseResult = _structuredActionParser.Parse(providerResult.Response.ProposedActions);
        if (!parseResult.Succeeded)
        {
            return AiProviderResponseResolution.ParseFailure(providerResult.Response, parseResult);
        }

        return AiProviderResponseResolution.Success(providerResult.Response, parseResult);
    }

    private static string BuildSelfCorrectionMessage(AiStructuredActionParseResult parseResult)
    {
        var failureCode = string.IsNullOrWhiteSpace(parseResult.FailureCode)
            ? DefaultFailureCode
            : parseResult.FailureCode.Trim();
        var safeReason = string.IsNullOrWhiteSpace(parseResult.FailureMessage)
            ? DefaultFailureMessage
            : parseResult.FailureMessage.Trim();
        var correction = string.IsNullOrWhiteSpace(parseResult.CorrectionMessage)
            ? AiToolCorrectionMessages.SchemaExactRetry
            : parseResult.CorrectionMessage.Trim();

        return $"""
            The previous proposed tool call was rejected by platform validation.
            Failure code: {failureCode}
            Safe reason: {safeReason}

            {correction}
            Return either no tool call, or retry with corrected tool call arguments only. Do not reveal validation internals or include raw rejected arguments.
            """;
    }

    private static bool ShouldRetryEmptyProviderResponse(AiProviderResponseResolution resolution) =>
        string.Equals(resolution.FailureCode, InvalidResponseFailureCode, StringComparison.Ordinal)
        && !string.IsNullOrWhiteSpace(resolution.FailureMessage)
        && resolution.FailureMessage.Contains("empty", StringComparison.OrdinalIgnoreCase);

    private static string BuildEmptyResponseCorrectionMessage(AiChatPayload providerPayload)
    {
        var actionInstruction = providerPayload.Options.ToolProposalsEnabled && providerPayload.ActionSchema is not null
            ? "If the poster contains enough event details, return one valid platform tool call using the provided schema. If required details are missing, answer in plain text with the missing details instead."
            : "Answer in plain text only because no platform tool schema is available for this request.";

        return $"""
            The previous provider response did not include usable assistant text or a valid platform tool call.

            {actionInstruction}
            Do not return empty content, thinking-only content, or non-text content without a valid platform tool call.
            """;
    }
}

public sealed record AiProviderResponseResolution(
    bool Succeeded,
    AiChatResponse? Response,
    AiStructuredActionParseResult? ParseResult,
    string? FailureCode,
    string? FailureMessage)
{
    private const string DefaultFailureCode = "invalid_tool_arguments";
    private const string DefaultFailureMessage = "AI provider returned invalid action payload JSON.";

    public static AiProviderResponseResolution Success(
        AiChatResponse response,
        AiStructuredActionParseResult parseResult)
        => new(true, response, parseResult, null, null);

    public static AiProviderResponseResolution Failure(string failureCode, string failureMessage)
        => new(false, null, null, failureCode, failureMessage);

    public static AiProviderResponseResolution ParseFailure(
        AiChatResponse response,
        AiStructuredActionParseResult parseResult)
        => new(
            false,
            response,
            parseResult,
            parseResult.FailureCode ?? DefaultFailureCode,
            parseResult.FailureMessage ?? DefaultFailureMessage);
}
