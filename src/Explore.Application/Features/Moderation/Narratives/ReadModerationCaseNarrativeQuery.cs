using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using FluentValidation;

namespace Explore.Application.Features.Moderation.Narratives;

/// <summary>Opaque case/read-receipt intent; current caller, tenancy and read authority are resolved at disclosure.</summary>
public sealed record ReadModerationCaseNarrativeQuery : IQuery<ModerationCaseNarrativeReadResult>
{
    private byte[] token = [];
    public required Guid CaseId { get; init; }
    public required long ExpectedCaseRevision { get; init; }
    public required Guid ReceiptId { get; init; }
    public required ReadOnlyMemory<byte> PresentedToken
    {
        get => token.ToArray();
        init => token = value.ToArray();
    }
    public override string ToString() => nameof(ReadModerationCaseNarrativeQuery);
}

/// <summary>
/// A shape-validated pending read, never a permission or serialized plaintext DTO. The
/// transport invokes disclosure only at its actual output seam; the custody owner requalifies.
/// </summary>
public sealed class ModerationCaseNarrativeReadResult
{
    private readonly IModerationCaseNarrativeCustody? custody;
    private readonly ModerationCaseAccessRequest? request;

    private ModerationCaseNarrativeReadResult(
        IModerationCaseNarrativeCustody? custody, ModerationCaseAccessRequest? request, IEnumerable<string> errors)
    {
        this.custody = custody;
        this.request = request;
        ValidationErrors = errors.ToArray();
    }

    public IReadOnlyList<string> ValidationErrors { get; }
    public bool IsValid => custody is not null && request is not null;
    internal static ModerationCaseNarrativeReadResult Invalid(IEnumerable<string> errors) => new(null, null, errors);
    internal static ModerationCaseNarrativeReadResult Prepare(
        IModerationCaseNarrativeCustody custody, ReadModerationCaseNarrativeQuery query) =>
        new(custody, new(ModerationCaseAccessPurpose.ReadAllegation, query.CaseId, Guid.Empty,
            query.ExpectedCaseRevision, query.ReceiptId, query.PresentedToken), []);

    public Task<bool> DiscloseAsync(
        Func<ReadOnlyMemory<byte>, CancellationToken, Task> write, CancellationToken cancellationToken) =>
        custody is not null && request is not null
            ? custody.DiscloseAsync(request, write, cancellationToken)
            : Task.FromResult(false);

    public override string ToString() => nameof(ModerationCaseNarrativeReadResult);
}

public sealed class ReadModerationCaseNarrativeQueryHandler(IModerationCaseNarrativeCustody custody)
    : IQueryHandler<ReadModerationCaseNarrativeQuery, ModerationCaseNarrativeReadResult>
{
    public async Task<ModerationCaseNarrativeReadResult> QueryAsync(
        ReadModerationCaseNarrativeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = await new ReadModerationCaseNarrativeQueryValidator().ValidateAsync(query, cancellationToken);
        return validation.IsValid
            ? ModerationCaseNarrativeReadResult.Prepare(custody, query)
            : ModerationCaseNarrativeReadResult.Invalid(validation.Errors.Select(error => error.ErrorMessage));
    }
}

public sealed class ReadModerationCaseNarrativeQueryValidator : AbstractValidator<ReadModerationCaseNarrativeQuery>
{
    public ReadModerationCaseNarrativeQueryValidator()
    {
        RuleFor(query => query.CaseId).Must(id => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(query => query.ReceiptId).Must(id => id != Guid.Empty && id.Version == 7 && id.Variant is >= 8 and <= 11);
        RuleFor(query => query.ExpectedCaseRevision).GreaterThan(0);
        RuleFor(query => query.PresentedToken).Must(value => value.Length == 32);
    }
}
