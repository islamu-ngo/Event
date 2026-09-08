namespace Event.Architecture.Tests;

public sealed class EventReportConsentArchitectureTests
{
    [Test]
    public async Task ConsentUpdateControllerActionShouldOnlyDispatchAndAssembleHal()
    {
        var root = ResolveRepositoryRoot();
        var controller = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Explore.API",
            "Controllers",
            "EventReportsController.cs"));
        var actionStart = controller.IndexOf(
            "public async Task<ActionResult<HalResource<MyEventReportDto>>> UpdateCommunicationConsent",
            StringComparison.Ordinal);
        var actionEnd = controller.IndexOf("private string? ComputeReporterFingerprintHash", StringComparison.Ordinal);
        var action = controller[actionStart..actionEnd];

        await Assert.That(action).Contains("_mediator.Send(");
        await Assert.That(action).Contains("_myReportResourceAssembler.ToResource(");
        await Assert.That(action).DoesNotContain("IEventReportRepository");
        await Assert.That(action).DoesNotContain("IUnitOfWork");
        await Assert.That(action).DoesNotContain("IEmailService");
        await Assert.That(action).DoesNotContain("Smtp");
    }

    [Test]
    public async Task ConsentAffordanceShouldExistOnlyOnMyReportsPolicies()
    {
        var root = ResolveRepositoryRoot();
        var reporterPolicy = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Explore.API",
            "Hateoas",
            "Policies",
            "EventReportLinkPolicy.cs"));
        var moderationPolicy = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Explore.API",
            "Hateoas",
            "Policies",
            "ModerationReportLinkPolicy.cs"));
        var handler = await File.ReadAllTextAsync(Path.Combine(
            root,
            "src",
            "Explore.Application",
            "Features",
            "EventReporting",
            "Handlers",
            "Commands",
            "UpdateMyReportCommunicationConsentCommandHandler.cs"));

        await Assert.That(CountOccurrences(reporterPolicy, "LinkRelations.UpdateCommunicationConsent"))
            .IsEqualTo(2);
        await Assert.That(reporterPolicy).Contains("RouteNames.UpdateMyEventReportCommunicationConsent");
        await Assert.That(reporterPolicy).Contains("ResourceKinds.User");
        await Assert.That(reporterPolicy).Contains("AuthorizationActions.Users.Update");
        await Assert.That(handler).Contains("authorizationProvider.AuthorizeAsync(");
        await Assert.That(handler).Contains("ResourceKinds.User");
        await Assert.That(handler).Contains("resolvedReporterUserId.ToString()");
        await Assert.That(handler).Contains("AuthorizationActions.Users.Update");
        await Assert.That(handler).Contains("throw new AuthorizationException(");
        await Assert.That(moderationPolicy).DoesNotContain("UpdateCommunicationConsent");
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate repository root containing Explore.slnx.");
    }
}
