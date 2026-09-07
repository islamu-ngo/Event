using Explore.Application.Responses;
using Microsoft.AspNetCore.Mvc;

namespace Explore.API.ExceptionHandling;

/// <summary>
/// A per-capability table from <see cref="BaseCommandResponse{TKey}.FailureCode"/> to an RFC 7807 response.
/// <para>
/// Controllers previously each carried a private <c>switch</c> over failure codes. Those switches agreed on
/// the shape — a few codes mean not-found, a few mean conflict, a few mean a provider is unavailable, and
/// everything else is a validation problem — but every copy could drift in status, title, or detail-safety
/// independently. Declaring the table makes the mapping data, so a capability's failure contract is readable
/// in one place and cannot diverge between two actions of the same controller.
/// </para>
/// <para>
/// Rules match in declaration order and the first match wins, so a specific code can be routed ahead of a
/// broader group. An unmatched failure falls through to the validation descriptor rather than collapsing into
/// a bare 400, which is what keeps distinct failures distinguishable to clients.
/// </para>
/// </summary>
internal sealed class CommandFailurePolicy
{
    private readonly ApiValidationProblemDescriptor _validation;
    private readonly IReadOnlyList<Rule> _rules;

    private CommandFailurePolicy(ApiValidationProblemDescriptor validation, IReadOnlyList<Rule> rules)
    {
        _validation = validation;
        _rules = rules;
    }

    /// <summary>Starts a policy whose unmatched failures become this validation problem.</summary>
    public static CommandFailurePolicy ValidatedBy(ApiValidationProblemDescriptor validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        return new CommandFailurePolicy(validation, []);
    }

    /// <summary>Routes the given failure codes to a 404 using an explicit not-found descriptor.</summary>
    public CommandFailurePolicy NotFound(ApiNotFoundProblemDescriptor descriptor, params string[] failureCodes)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return With(new NotFoundRule(Codes(failureCodes), descriptor));
    }

    /// <summary>
    /// Routes the given failure codes to a 409. The detail prefers the command's own message so handlers can
    /// explain the specific conflict, falling back to the supplied text when they do not.
    /// </summary>
    public CommandFailurePolicy Conflict(string title, string fallbackDetail, params string[] failureCodes) =>
        With(new ConflictRule(Codes(failureCodes), Text(title), Text(fallbackDetail)));

    /// <summary>Routes the given failure codes to a 503 for downstream or provider outages.</summary>
    public CommandFailurePolicy Unavailable(string title, string fallbackDetail, params string[] failureCodes) =>
        With(new UnavailableRule(Codes(failureCodes), Text(title), Text(fallbackDetail)));

    /// <summary>
    /// Routes the given failure codes to a 401 with the platform's standard wording. Reserved for handlers
    /// that discover mid-flight that the caller has no usable identity; endpoint-level authentication still
    /// runs first. Restating the default text at a call site would let it drift from the shared factory.
    /// </summary>
    public CommandFailurePolicy AuthenticationRequired(params string[] failureCodes) =>
        With(new AuthenticationRequiredRule(Codes(failureCodes), Title: null, Detail: null));

    /// <summary>Routes the given failure codes to a 401 with wording specific to this capability.</summary>
    public CommandFailurePolicy AuthenticationRequired(string title, string detail, params string[] failureCodes) =>
        With(new AuthenticationRequiredRule(Codes(failureCodes), Text(title), Text(detail)));

    /// <summary>Routes the given failure codes to a 403.</summary>
    public CommandFailurePolicy Forbidden(string title, string detail, params string[] failureCodes) =>
        With(new ForbiddenRule(Codes(failureCodes), Text(title), Text(detail)));

    /// <summary>Routes the given failure codes to a 410 for resources that existed but are permanently gone.</summary>
    public CommandFailurePolicy Gone(string title, string fallbackDetail, params string[] failureCodes) =>
        With(new GoneRule(Codes(failureCodes), Text(title), Text(fallbackDetail)));

    /// <summary>Maps a failed command response to the problem response its failure code declares.</summary>
    public ActionResult Map<TKey>(ControllerBase controller, BaseCommandResponse<TKey> response)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(response);

        var failureCode = response.FailureCode;
        if (string.IsNullOrWhiteSpace(failureCode))
        {
            return controller.ToCommandValidationProblem(response, _validation);
        }

        var rule = _rules.FirstOrDefault(candidate => candidate.Matches(failureCode));

        return rule switch
        {
            NotFoundRule notFound => controller.ToNotFoundProblem(notFound.Descriptor),
            ConflictRule conflict => controller.ToCommandConflictProblem(response, conflict.Title, conflict.FallbackDetail),
            UnavailableRule unavailable => controller.ToServiceUnavailableProblem(
                unavailable.Title,
                response.Message ?? unavailable.FallbackDetail,
                failureCode),
            AuthenticationRequiredRule { Title: null } => controller.ToAuthenticationRequiredProblem(),
            AuthenticationRequiredRule authentication => controller.ToAuthenticationRequiredProblem(
                authentication.Title,
                authentication.Detail!),
            ForbiddenRule forbidden => controller.ToForbiddenProblem(forbidden.Title, forbidden.Detail),
            GoneRule gone => controller.ToGoneProblem(gone.Title, response.Message ?? gone.FallbackDetail, failureCode),
            _ => controller.ToCommandValidationProblem(response, _validation),
        };
    }

    /// <summary>Returns <paramref name="onSuccess"/> for a successful command, otherwise the declared problem.</summary>
    public ActionResult Map<TKey>(ControllerBase controller, BaseCommandResponse<TKey> response, Func<ActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(onSuccess);

        return response.IsSuccess ? onSuccess() : Map(controller, response);
    }

    private CommandFailurePolicy With(Rule rule) => new(_validation, [.. _rules, rule]);

    private static IReadOnlyCollection<string> Codes(string[] failureCodes) =>
        failureCodes is { Length: > 0 }
            ? failureCodes
            : throw new ArgumentException("A failure-mapping rule must name at least one failure code.", nameof(failureCodes));

    private static string Text(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value;
    }

    private abstract record Rule(IReadOnlyCollection<string> FailureCodes)
    {
        public bool Matches(string failureCode) => FailureCodes.Contains(failureCode, StringComparer.Ordinal);
    }

    private sealed record NotFoundRule(IReadOnlyCollection<string> FailureCodes, ApiNotFoundProblemDescriptor Descriptor)
        : Rule(FailureCodes);

    private sealed record ConflictRule(IReadOnlyCollection<string> FailureCodes, string Title, string FallbackDetail)
        : Rule(FailureCodes);

    private sealed record UnavailableRule(IReadOnlyCollection<string> FailureCodes, string Title, string FallbackDetail)
        : Rule(FailureCodes);

    /// <summary>A null title/detail pair means "use the shared factory's standard 401 wording".</summary>
    private sealed record AuthenticationRequiredRule(IReadOnlyCollection<string> FailureCodes, string? Title, string? Detail)
        : Rule(FailureCodes);

    private sealed record ForbiddenRule(IReadOnlyCollection<string> FailureCodes, string Title, string Detail)
        : Rule(FailureCodes);

    private sealed record GoneRule(IReadOnlyCollection<string> FailureCodes, string Title, string FallbackDetail)
        : Rule(FailureCodes);
}
