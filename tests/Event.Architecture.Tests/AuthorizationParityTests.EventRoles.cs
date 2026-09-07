namespace Event.Architecture.Tests;

using System.Text.Json;
using System.Text.RegularExpressions;
using Explore.Application.Authorization;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using Explore.Domain.Constants;
using Explore.Infrastructure.Services;

public partial class AuthorizationParityTests
{
    private static readonly string[] ExpectedEventDerivedRoles =
    [
        "islamuevent_event_owner",
        "islamuevent_event_manager",
        "islamuevent_registration_manager",
        "islamuevent_ticket_manager",
        "islamuevent_check_in_staff"
    ];

    private static readonly string[] ExpectedEventRoleCodes =
    [
        "event.owner",
        "event.manager",
        "event.registration_manager",
        "event.check_in_staff"
    ];

    private static readonly string[] EventFamilyResourceKinds =
    [
        "islamuevent_event",
        "islamuevent_event_session",
        "islamuevent_event_session_group",
        "islamuevent_event_day",
        "islamuevent_event_agenda_item",
        "islamuevent_event_session_agenda_item",
        "islamuevent_event_organizer_claim",
        "islamuevent_registration_form"
    ];

    [Test]
    [DisplayName("All 5 event-role derived roles exist in derived_roles.yaml")]
    public async Task EventRoleDerivedRoles_ShouldExist_InDerivedRolesYaml()
    {
        var derivedRolesPath = Path.Combine(CerbosPoliciesPath, "derived_roles.yaml");
        var content = File.ReadAllText(derivedRolesPath);

        var missing = ExpectedEventDerivedRoles
            .Where(role => !content.Contains($"name: {role}", StringComparison.Ordinal))
            .ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These event-role derived roles are missing from derived_roles.yaml: [{string.Join(", ", missing)}]");
    }

    [Test]
    [DisplayName("Each event-role derived role checks the correct role code")]
    public async Task EventRoleDerivedRoles_ShouldCheck_CorrectRoleCodes()
    {
        var derivedRolesPath = Path.Combine(CerbosPoliciesPath, "derived_roles.yaml");
        var content = File.ReadAllText(derivedRolesPath);

        var missing = ExpectedEventRoleCodes
            .Where(code => !content.Contains($"\"{code}\"", StringComparison.Ordinal))
            .ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These event role codes are missing from derived_roles.yaml conditions: [{string.Join(", ", missing)}]");
    }

    [Test]
    [Category("Phase43Ticketing")]
    [DisplayName("Ticket manager derived role requires the exact ticket-management permission")]
    public async Task TicketManagerDerivedRole_ShouldRequire_ExactTicketManagementPermission()
    {
        var derivedRolesPath = Path.Combine(CerbosPoliciesPath, "derived_roles.yaml");
        var content = File.ReadAllText(derivedRolesPath);

        await Assert.That(content).Contains("name: islamuevent_ticket_manager");
        await Assert.That(content).Contains($"\"{PermissionCodes.EventManageTickets}\"");
    }

    [Test]
    [Category("PaidEventCommerceAuthorization")]
    [DisplayName("Paid event commerce uses the canonical event action catalog and finance principal attrs")]
    public async Task PaidEventCommerce_ShouldUse_CanonicalEventActionAndFinancePrincipalAttributes()
    {
        await Assert.That(AuthorizationActions.Events.ManagePaidEventCommerce)
            .IsEqualTo("manage-paid-event-commerce");

        var principalSchemaPath = Path.Combine(CerbosSchemasPath, NamespacedPrincipalSchemaFileName);
        var principalSchema = JsonDocument.Parse(File.ReadAllText(principalSchemaPath));
        var principalProperties = principalSchema.RootElement.GetProperty("properties");

        await Assert.That(principalProperties.TryGetProperty("eventFinanceOrganizations", out _)).IsTrue();
        await Assert.That(principalProperties.TryGetProperty("eventFinanceGroups", out _)).IsTrue();

        var policy = File.ReadAllText(Path.Combine(CerbosPoliciesPath, "islamuevent_event.yaml"));
        await Assert.That(policy).Contains("manage-paid-event-commerce");
        await Assert.That(policy).Contains("eventFinanceOrganizations");
        await Assert.That(policy).Contains("eventFinanceGroups");
    }

    [Test]
    [Category("RegistrationFormAuthorization")]
    [DisplayName("Event-family policies import the derived roles set")]
    public async Task EventFamilyPolicies_ShouldImport_DerivedRoles()
    {
        var violations = new List<string>();

        foreach (var kind in EventFamilyResourceKinds)
        {
            var policyFile = Path.Combine(CerbosPoliciesPath, $"{kind}.yaml");
            if (!File.Exists(policyFile))
            {
                violations.Add($"{kind}.yaml: policy file not found");
                continue;
            }

            var content = File.ReadAllText(policyFile);
            if (!content.Contains("importDerivedRoles:", StringComparison.Ordinal) &&
                !content.Contains("derivedRoles:", StringComparison.Ordinal))
            {
                violations.Add($"{kind}.yaml: does not import or reference derived roles");
            }
        }

        await Assert.That(violations)
            .IsEmpty()
            .Because($"Event-family policies must import the derived roles set: [{string.Join("; ", violations)}]");
    }

    [Test]
    [DisplayName("Cerbos event-role test file exists")]
    public async Task EventRoleCerbosTestFile_ShouldExist()
    {
        var testFile = Path.Combine(CerbosPoliciesPath, "..", "tests", "islamuevent_event_role_test.yaml");
        var fullPath = Path.GetFullPath(testFile);

        await Assert.That(File.Exists(fullPath))
            .IsTrue()
            .Because("cerbos/tests/islamuevent_event_role_test.yaml must exist for event-role policy verification.");
    }

    [Test]
    [DisplayName("Principal schema includes eventAssignments property")]
    public async Task PrincipalSchema_ShouldInclude_EventAssignments()
    {
        var principalSchemaPath = Path.Combine(CerbosSchemasPath, NamespacedPrincipalSchemaFileName);
        var content = File.ReadAllText(principalSchemaPath);
        var json = JsonDocument.Parse(content);

        var hasEventAssignments = json.RootElement
            .GetProperty("properties")
            .TryGetProperty("eventAssignments", out _);

        await Assert.That(hasEventAssignments)
            .IsTrue()
            .Because("Principal schema must include eventAssignments for event-role derived role evaluation.");
    }

    [Test]
    [Category("RegistrationFormAuthorization")]
    [DisplayName("Event-family resource schemas include eventId property")]
    public async Task EventFamilySchemas_ShouldInclude_EventId()
    {
        var violations = new List<string>();

        foreach (var kind in EventFamilyResourceKinds)
        {
            var schemaFile = Path.Combine(CerbosSchemasPath, $"{kind}.json");
            if (!File.Exists(schemaFile))
            {
                violations.Add($"{kind}.json: schema file not found");
                continue;
            }

            var content = File.ReadAllText(schemaFile);
            var json = JsonDocument.Parse(content);

            if (!json.RootElement.GetProperty("properties").TryGetProperty("eventId", out _))
                violations.Add($"{kind}.json: missing eventId property");
        }

        await Assert.That(violations)
            .IsEmpty()
            .Because($"Event-family schemas must include eventId for event-role derived role evaluation: [{string.Join("; ", violations)}]");
    }

    [Test]
    [DisplayName("CerbosPrincipalBuilder has EnrichWithEventAssignmentsAsync method")]
    public async Task CerbosPrincipalBuilder_ShouldHave_EventAssignmentEnrichment()
    {
        var builderType = typeof(CerbosPrincipalBuilder);
        var method = builderType.GetMethod("EnrichWithEventAssignmentsAsync");

        await Assert.That(method)
            .IsNotNull()
            .Because("CerbosPrincipalBuilder must expose EnrichWithEventAssignmentsAsync for event-role hydration.");
    }

    [Test]
    [Category("RegistrationFormAuthorization")]
    [DisplayName("Event-scoped fallback evaluator routes to event-scoped method for all event-family resources")]
    public async Task FallbackEvaluator_ShouldRoute_EventFamilyResources()
    {
        var sourceFile = FindSourceFile("FallbackAuthorizationService.cs", "Explore.Infrastructure");
        var source = File.ReadAllText(sourceFile);

        var eventScopedKinds = EventFamilyResourceKinds
            .Where(k => k != "islamuevent_event_contact_share_consent")
            .ToList();

        var missing = eventScopedKinds
            .Where(kind => !source.Contains($"\"{kind}\"", StringComparison.Ordinal))
            .ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These event-family resource kinds are missing from FallbackAuthorizationService: [{string.Join(", ", missing)}]");
    }

    [Test]
    [DisplayName("Event-bound organizer claim requests use the organizer-claim resource")]
    public async Task EventBoundOrganizerClaimRequests_ShouldUse_OrganizerClaimResource()
    {
        var requestTypes = new[]
        {
            typeof(SubmitEventOrganizerClaimCommand),
            typeof(WithdrawEventOrganizerClaimCommand),
            typeof(ReviewEventOrganizerClaimCommand),
            typeof(GetEventOrganizerClaimsRequest),
            typeof(GetEventOrganizerClaimRequest)
        };

        foreach (var requestType in requestTypes)
        {
            var attribute = requestType.GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
                .Cast<AuthorizeResourceAttribute>()
                .Single();
            await Assert.That(attribute.Resource)
                .IsEqualTo(ResourceKinds.EventOrganizerClaim)
                .Because($"{requestType.Name} must authorize against the claim policy while its event metadata is enriched server-side.");
        }

        var withdrawAttribute = typeof(WithdrawEventOrganizerClaimCommand)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
            .Cast<AuthorizeResourceAttribute>()
            .Single();
        await Assert.That(withdrawAttribute.Action)
            .IsEqualTo(AuthorizationActions.Events.WithdrawOrganizerClaim);
        var submitAttribute = typeof(SubmitEventOrganizerClaimCommand)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true)
            .Cast<AuthorizeResourceAttribute>()
            .Single();
        await Assert.That(submitAttribute.Action)
            .IsEqualTo(AuthorizationActions.Events.ClaimOrganizer);

        await Assert.That(typeof(GetClaimantOrganizerClaimsRequest)
            .GetCustomAttributes(typeof(AuthorizeResourceAttribute), inherit: true))
            .IsEmpty()
            .Because("Cross-event claimant listing remains handler-authorized by current claimant control.");
    }
}
