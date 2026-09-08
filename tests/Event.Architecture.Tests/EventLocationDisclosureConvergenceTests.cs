using System.Reflection;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Location;
using Explore.Application.Services;

namespace Event.Architecture.Tests;

public sealed class EventLocationDisclosureConvergenceTests
{
    /// <summary>
    /// The complete, closed set of components allowed to reach the pure evaluator directly. Everything
    /// else must go through <see cref="IEventLocationDisclosureService"/> so query and authorization
    /// budgets stay bounded and no surface can invent its own visibility rule.
    /// </summary>
    /// <remarks>
    /// Two authorities sit beside the request-scoped service because they run without an HTTP requester:
    /// the fanout authorization service resolves one explicit background recipient, and the federation
    /// evaluator supplies fixed public-only authority to snapshot projections. Both feed the same pure
    /// evaluator rather than reimplementing disclosure. Adding a name here is an architectural decision.
    /// </remarks>
    private static readonly string[] EvaluatorOwners =
    [
        Path.Combine("Explore.Application", "Services", "EventLocationDisclosureService.cs"),
        Path.Combine("Explore.Application", "Services", "EventLocationDisclosureEvaluator.cs"),
        Path.Combine("Explore.Application", "Services", "PublicEventLocationDisclosureEvaluator.cs"),
        Path.Combine("Explore.Application", "Services", "PublicEventLocationProjection.cs"),
        Path.Combine("Explore.Application", "Services", "FanoutAttendeeLocationAuthorizationService.cs"),
        Path.Combine("Explore.Application", "ApplicationServicesRegistration.cs")
    ];

    /// <summary>
    /// Consumers of the federation-only public evaluator. They never see the general evaluator and cannot
    /// widen beyond public authority.
    /// </summary>
    private static readonly string[] PublicEvaluatorConsumers =
    [
        Path.Combine("Features", "Federation", "Atproto", "Services", "AtprotoEventPublicationSnapshotFactory.cs")
    ];

    [Test]
    public async Task PurposeSpecificDtos_AreOnlyMaterializedFromAValidatedDisclosureResult()
    {
        Type[] purposeDtos =
        [
            typeof(EventLocationPublicDto),
            typeof(EventLocationAttendeeDto),
            typeof(EventLocationManagementDto)
        ];

        foreach (Type dto in purposeDtos)
        {
            // No public constructor: the only way in is FromDisclosureResult, which rejects a result whose
            // purpose does not match the contract being produced.
            await Assert.That(dto.GetConstructors(BindingFlags.Public | BindingFlags.Instance)).IsEmpty();
            await Assert.That(dto.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Any(method => method.Name == "FromDisclosureResult"))
                .IsTrue();
        }
    }

    [Test]
    public async Task NoApplicationCodeOutsideTheDisclosureServiceInvokesTheEvaluator()
    {
        string[] violations = ApplicationSourceFiles()
            .Where(file => !IsEvaluatorOwner(file))
            .Where(file => File.ReadAllText(file).Contains("EventLocationDisclosureEvaluator", StringComparison.Ordinal))
            .Select(Relative)
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task EveryHandlerProducingAPurposeDtoDependsOnTheDisclosureAuthority()
    {
        string[] purposeDtoNames =
        [
            nameof(EventLocationPublicDto),
            nameof(EventLocationAttendeeDto),
            nameof(EventLocationManagementDto)
        ];

        var violations = new List<string>();
        foreach (string file in ApplicationSourceFiles().Where(IsHandlerFile))
        {
            string source = File.ReadAllText(file);
            if (!purposeDtoNames.Any(name => source.Contains(name, StringComparison.Ordinal)))
            {
                continue;
            }

            // A handler may either resolve disclosures itself or delegate to a sibling handler in the same
            // file that does; either way the authority name must appear.
            if (!source.Contains(nameof(IEventLocationDisclosureService), StringComparison.Ordinal))
            {
                violations.Add(Relative(file));
            }
        }

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task TheDisclosureServiceIsTheOnlyImplementationOfTheAuthority()
    {
        Type[] implementations = typeof(IEventLocationDisclosureService).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IEventLocationDisclosureService).IsAssignableFrom(type))
            .ToArray();

        await Assert.That(implementations).Count().IsEqualTo(1);
        await Assert.That(implementations[0]).IsEqualTo(typeof(EventLocationDisclosureService));
    }

    [Test]
    public async Task DisclosureBatchesStayWithinTheDeclaredCeiling()
    {
        // A ceiling is what keeps "one query per surface" honest as event programmes grow.
        await Assert.That(IEventLocationDisclosureService.MaximumBatchSize).IsGreaterThan(0);
        await Assert.That(IEventLocationDisclosureService.MaximumBatchSize).IsLessThanOrEqualTo(256);
    }

    [Test]
    public async Task PublicAndAttendeeFieldContractsExcludeManagementOnlyData()
    {
        string[] publicFields = typeof(EventLocationPublicFieldsDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();
        string[] attendeeFields = typeof(EventLocationAttendeeFieldsDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToArray();

        foreach (string forbidden in new[] { "RoomDescription", "AccessInstructions", "EntryDetails", "DoorCode" })
        {
            await Assert.That(publicFields).DoesNotContain(forbidden);
            await Assert.That(attendeeFields).DoesNotContain(forbidden);
        }
    }

    [Test]
    public async Task OperationalSecretsHaveNoRoutePurposeAtAll()
    {
        EventLocationDisclosureField[] operationalSecrets =
        [
            EventLocationDisclosureField.AccessInstructions,
            EventLocationDisclosureField.EntryDetails,
            EventLocationDisclosureField.DoorCode
        ];

        foreach (EventLocationDisclosureField field in operationalSecrets)
        {
            foreach (EventLocationDisclosurePurpose purpose in Enum.GetValues<EventLocationDisclosurePurpose>())
            {
                await Assert.That(EventLocationDisclosureContract.IsWithinPurposeCeiling(purpose, field))
                    .IsFalse();
            }
        }
    }

    private static IEnumerable<string> ApplicationSourceFiles() =>
        Directory.EnumerateFiles(
                ContextSystemHelpers.RepoPath("Explore.Application"),
                "*.cs",
                SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static bool IsHandlerFile(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}Handlers{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static bool IsEvaluatorOwner(string file) =>
        EvaluatorOwners.Any(owner => file.EndsWith(owner, StringComparison.Ordinal))
        || PublicEvaluatorConsumers.Any(consumer => file.EndsWith(consumer, StringComparison.Ordinal));

    private static string Relative(string file) =>
        Path.GetRelativePath(ContextSystemHelpers.RepoRoot, file);
}
