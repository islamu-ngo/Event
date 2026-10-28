using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationOperationalBudgetQualificationTests
{
    private static readonly AuthorityScope Instance = AuthorityScope.Instance(
        AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
        Guid.CreateVersion7());
    private static readonly AuthorityScope Tenant = AuthorityScope.Tenant(Instance, Guid.CreateVersion7());
    private static readonly DateTimeOffset Until = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Test]
    [Arguments("service")]
    [Arguments("content")]
    [Arguments("action")]
    [Arguments("limit")]
    [Arguments("window")]
    [Arguments("scope")]
    [Arguments("operator")]
    [Arguments("generation")]
    public async Task Unqualified_budget_terms_cannot_reach_native_source_mutation(string change)
    {
        var terms = new ModerationOperationalBudgetTerms(
            ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1));
        var command = Record(terms);
        command = change switch
        {
            "service" => command with { Terms = terms with { SubjectKind = ModerationSubjectKind.Service } },
            "content" => command with { Terms = terms with { SubjectKind = ModerationSubjectKind.Content } },
            "action" => command with { Terms = terms with { Action = (ModerationOperationalAction)0 } },
            "limit" => command with { Terms = terms with { MaximumOccurrences = 0 } },
            "window" => command with { Terms = terms with { Window = TimeSpan.MaxValue } },
            "scope" => command with { Target = Instance },
            "operator" => command with { OperatorHumanId = Guid.Empty },
            _ => command with { ExpectedQualificationRevision = long.MaxValue - 1 }
        };
        var result = await new ModerationOperationalBudgetCommandValidator<RecordModerationOperationalBudgetCommand>()
            .ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    [Test]
    public async Task Exact_independent_budget_request_does_not_invent_a_caller_identity()
    {
        var command = Record(new(ModerationSubjectKind.Human,
            ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1)));
        var result = await new ModerationOperationalBudgetCommandValidator<RecordModerationOperationalBudgetCommand>()
            .ValidateAsync(command);
        await Assert.That(result.IsValid).IsTrue();
    }

    [Test]
    [Arguments("identity")]
    [Arguments("source-generation")]
    [Arguments("source-revision")]
    public async Task Adoption_requires_distinct_identity_and_exact_original_generation(string change)
    {
        var command = new AdoptModerationOperationalBudgetCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = Tenant,
            ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(),
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = Until
        };
        command = change switch
        {
            "identity" => command with { AdoptionId = command.SourceId },
            "source-generation" => command with { ExpectedSourceWriterGeneration = 0 },
            _ => command with { ExpectedSourceRevision = 2 }
        };
        var result = await new ModerationOperationalBudgetCommandValidator<AdoptModerationOperationalBudgetCommand>()
            .ValidateAsync(command);
        await Assert.That(result.IsValid).IsFalse();
    }

    private static RecordModerationOperationalBudgetCommand Record(ModerationOperationalBudgetTerms terms) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = Tenant,
        ExpectedQualificationRevision = 1,
        SourceId = Guid.CreateVersion7(),
        Terms = terms,
        OperatorHumanId = Guid.CreateVersion7(),
        ValidUntil = Until
    };
}
