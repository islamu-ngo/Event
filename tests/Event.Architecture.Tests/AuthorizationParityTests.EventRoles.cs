namespace Event.Architecture.Tests;

using System.Text.Json;
using Explore.Application.Authorization;
using Explore.Application.Features.EventOrganizerClaims.Requests.Commands;
using Explore.Application.Features.EventOrganizerClaims.Requests.Queries;
using Explore.Domain.Constants;
using YamlDotNet.RepresentationModel;

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
        YamlMappingNode roles = RequiredMapping(ReadYaml(derivedRolesPath), "derivedRoles");
        string[] declaredRoles = RequiredSequence(roles, "definitions").Children.Cast<YamlMappingNode>()
            .Select(definition => RequiredScalar(definition, "name")).ToArray();
        var missing = ExpectedEventDerivedRoles.Except(declaredRoles, StringComparer.Ordinal).ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These event-role derived roles are missing from derived_roles.yaml: [{string.Join(", ", missing)}]");
    }

    [Test]
    [Category("PaidEventCommerceAuthorization")]
    [DisplayName("Paid event commerce uses the event action contract and finance principal attrs")]
    public async Task PaidEventCommerce_ShouldUse_EventActionContractAndFinancePrincipalAttributes()
    {
        await Assert.That(AuthorizationActions.Events.ManagePaidEventCommerce)
            .IsEqualTo("manage-paid-event-commerce");

        var principalSchemaPath = Path.Combine(CerbosSchemasPath, NamespacedPrincipalSchemaFileName);
        var principalSchema = JsonDocument.Parse(File.ReadAllText(principalSchemaPath));
        var principalProperties = principalSchema.RootElement.GetProperty("properties");

        await Assert.That(principalProperties.TryGetProperty("eventFinanceOrganizations", out _)).IsTrue();
        await Assert.That(principalProperties.TryGetProperty("eventFinanceGroups", out _)).IsTrue();

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

            YamlMappingNode policy = RequiredMapping(ReadYaml(policyFile), "resourcePolicy");
            string[] imports = RequiredSequence(policy, "importDerivedRoles").Children.Select(ScalarValue).ToArray();
            if (!imports.Contains("islamuevent_explore_admin_roles", StringComparer.Ordinal))
            {
                violations.Add($"{kind}.yaml: does not import the event derived roles set");
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
