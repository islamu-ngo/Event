using Explore.Application.Features.AiAssistant.Rag;

namespace Event.Application.UnitTests.Features.AiAssistant.Rag;

public sealed class AiRagIngestionPolicyTests
{
    [Test]
    public async Task ValidateWhenDocumentIsTenantPublicEventSummaryReturnsSuccess()
    {
        var result = AiRagIngestionPolicy.Validate(CreateDocument());

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.FailureCode).IsNull();
    }

    [Test]
    public async Task ValidateWhenTenantIsMissingRejectsBeforeIndexing()
    {
        var result = AiRagIngestionPolicy.Validate(CreateDocument(tenantId: Guid.Empty));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("rag_tenant_required");
    }

    [Test]
    public async Task ValidateWhenScopeIsUnknownRejectsPrivateOrUnsupportedContent()
    {
        var result = AiRagIngestionPolicy.Validate(CreateDocument(scope: (AiRagContentScope)999));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("rag_scope_not_allowed");
    }

    [Test]
    public async Task ValidateWhenSummaryIsTooLongReturnsSafeFailureWithoutEchoingContent()
    {
        var sensitiveLookingSummary = "private attendee note " + new string('a', AiRagIngestionPolicy.MaxSummaryLength + 1);

        var result = AiRagIngestionPolicy.Validate(CreateDocument(summary: sensitiveLookingSummary));

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("rag_summary_invalid");
        await Assert.That(result.FailureMessage).DoesNotContain("private attendee note");
    }

    [Test]
    public async Task ForTenantCreatesApprovedPublicSummaryFilterOnly()
    {
        var tenantId = Guid.CreateVersion7();
        var filter = AiRagSearchFilter.ForTenant(tenantId);

        await Assert.That(filter.Validate().Succeeded).IsTrue();
        await Assert.That(filter.TenantId).IsEqualTo(tenantId);
        await Assert.That(filter.AllowedScopes).IsEquivalentTo([
            AiRagContentScope.TenantPublicEventSummary,
            AiRagContentScope.GlobalPublicEventSummary
        ]);
    }

    [Test]
    public async Task ValidateWhenSearchFilterTenantIsMissingRejectsFilter()
    {
        var filter = AiRagSearchFilter.ForTenant(Guid.Empty);

        var result = filter.Validate();

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo("rag_tenant_required");
    }

    private static AiRagIndexDocument CreateDocument(
        Guid? tenantId = null,
        AiRagContentScope scope = AiRagContentScope.TenantPublicEventSummary,
        string summary = "Public event summary")
        => new(
            tenantId ?? Guid.CreateVersion7(),
            "event",
            Guid.CreateVersion7(),
            scope,
            "Community dinner",
            summary,
            DateTimeOffset.UtcNow,
            new AiRagCitation("Community dinner", "GetEventById", "/events/community-dinner"));
}
