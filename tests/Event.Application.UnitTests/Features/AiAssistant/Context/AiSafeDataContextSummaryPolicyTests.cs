using Explore.Application.Features.AiAssistant.Context;

namespace Event.Application.UnitTests.Features.AiAssistant.Context;

public sealed class AiSafeDataContextSummaryPolicyTests
{
    [Test]
    public async Task ValidateRequestWhenNoFieldsSpecifiedReturnsDefaultAllowList()
    {
        var result = new AiSafeDataContextSummaryPolicy().ValidateRequest(
            AiSafeDataContextRegistry.EventReferenceSummaryContextKind,
            Array.Empty<string>());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Fields).Contains("referenceId");
        await Assert.That(result.Fields).Contains("summary");
    }

    [Test]
    public async Task ValidateRequestWhenKnownFieldsRequestedReturnsNormalizedSuccess()
    {
        var result = new AiSafeDataContextSummaryPolicy().ValidateRequest(
            AiSafeDataContextRegistry.EventReferenceSummaryContextKind,
            new[] { " displayName ", "summary", "DISPLAYNAME" });

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.Fields).IsEquivalentTo(new[] { "displayName", "summary" });
    }

    [Test]
    public async Task ValidateRequestWhenPrivateFieldRequestedFailsClosedWithoutEchoingField()
    {
        var result = new AiSafeDataContextSummaryPolicy().ValidateRequest(
            AiSafeDataContextRegistry.EventReferenceSummaryContextKind,
            new[] { "privateAttendeeNotes" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiSafeDataContextFailureCodes.ContextFieldNotAllowed);
        await Assert.That(result.FailureMessage).DoesNotContain("privateAttendeeNotes");
    }

    [Test]
    public async Task ValidateRequestWhenArbitraryContextKindRequestedFailsClosed()
    {
        var result = new AiSafeDataContextSummaryPolicy().ValidateRequest("ef-dbcontext-events", new[] { "title" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiSafeDataContextFailureCodes.ContextKindNotAllowed);
    }

    [Test]
    public async Task ValidateRequestWhenContextKindIsBlankFailsClosed()
    {
        var result = new AiSafeDataContextSummaryPolicy().ValidateRequest(" ", new[] { "summary" });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(AiSafeDataContextFailureCodes.ContextKindNotAllowed);
    }

    [Test]
    public async Task RegistryWhenDuplicateFieldsAreConfiguredThrows()
    {
        var act = () => new AiSafeDataContextDefinition(
            "duplicate-context",
            "Projection",
            [
                new AiSafeDataContextField("summary", "Summary."),
                new AiSafeDataContextField("SUMMARY", "Duplicate summary.")
            ]);

        await Assert.That(act).Throws<ArgumentException>();
    }

    [Test]
    public async Task DefaultRegistryDoesNotExposeSqlOrEntityAccessFields()
    {
        var fields = AiSafeDataContextRegistry.CreateDefault()
            .Definitions
            .SelectMany(definition => definition.Fields)
            .Select(field => field.Name)
            .ToList();

        await Assert.That(fields).DoesNotContain("sql");
        await Assert.That(fields).DoesNotContain("linq");
        await Assert.That(fields).DoesNotContain("dbContext");
        await Assert.That(fields).DoesNotContain("content");
    }
}
