using ISLAMU.Moderation.Application;
using ISLAMU.Moderation.Domain;

namespace Event.Architecture.Tests;

public class ModerationModuleBoundaryTests
{
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task Shared_modules_have_no_Event_transport_storage_UI_or_secret_dependency(bool domain)
    {
        var assembly = domain ? typeof(ModerationDecision).Assembly : typeof(ModerationOperationService).Assembly;
        var forbiddenPrefixes = new[]
        {
            "Explore.", "Event.", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore",
            "System.Net.Http", "Quartz", "MudBlazor", "Infisical"
        };
        var violations = assembly.GetReferencedAssemblies()
            .Where(reference => forbiddenPrefixes.Any(prefix =>
                reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task Shared_domain_does_not_depend_on_shared_application()
    {
        var references = typeof(ModerationDecision).Assembly.GetReferencedAssemblies();
        await Assert.That(references.Any(reference =>
            reference.Name == typeof(ModerationOperationService).Assembly.GetName().Name)).IsFalse();
    }

    [Test]
    public async Task Shared_application_uses_the_same_domain_assembly()
    {
        var references = typeof(ModerationOperationService).Assembly.GetReferencedAssemblies();
        await Assert.That(references.Any(reference =>
            reference.Name == typeof(ModerationDecision).Assembly.GetName().Name)).IsTrue();
    }

    [Test]
    public async Task Session_reads_return_shared_entities_not_host_or_application_DTOs()
    {
        var reads = typeof(IModerationSession).GetMethods()
            .Where(method => method.Name != nameof(IModerationSession.SaveAsync))
            .ToArray();
        await Assert.That(reads.Length > 0).IsTrue();

        foreach (var read in reads)
        {
            var entityType = read.ReturnType.GetGenericArguments().Single();
            if (entityType.IsGenericType && entityType.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            {
                entityType = entityType.GetGenericArguments().Single();
            }

            await Assert.That(entityType.Assembly).IsEqualTo(typeof(ModerationDecision).Assembly);
        }
    }
}
