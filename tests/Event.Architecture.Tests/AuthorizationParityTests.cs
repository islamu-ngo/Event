namespace Event.Architecture.Tests;

using System.Reflection;
using Explore.Application.Authorization;
using Explore.Application.Contracts.Infrastructure;
using YamlDotNet.RepresentationModel;

/// <summary>
/// Ensures authorization artifacts stay aligned across all layers:
/// <see cref="ResourceKinds"/> constants, <see cref="ResourceDescriptors"/> catalog,
/// <see cref="ResourceDescriptorRegistry"/>,
/// Cerbos YAML policies, and Cerbos JSON schemas.
/// </summary>
public partial class AuthorizationParityTests
{
    private const string ProductNamespacePrefix = "islamuevent_";
    private const string NamespacedPrincipalSchemaFileName = "islamuevent_principal.json";

    private static readonly string CerbosPoliciesPath = ContextSystemHelpers.RepoPath("cerbos", "policies");
    private static readonly string CerbosSchemasPath = Path.Combine(CerbosPoliciesPath, "_schemas");
    private static readonly IReadOnlySet<string> LegacyBareResourceKinds = new HashSet<string>
    {
        "event",
        "event_session",
        "event_session_group",
        "event_session_agenda_item",
        "event_day",
        "event_agenda_item",
        "event_registration",
        "event_contact_share_consent",
        "organization",
        "organization_member",
        "organization_review",
        "tenant",
        "tenant_setting",
        "tenant_user_role_grant",
        "category",
        "tag",
        "location",
        "location_room",
        "storage_object",
        "user",
        "atproto_record",
        "indexed_did",
        "instance_setting",
        "custom_property_definition",
        "custom_property_template",
        "custom_property_value",
        "custom_property_projection",
        "custom_property_governance",
        "platform_namespace",
        "notification",
        "actor",
        "group",
        "group_member"
    };

    // ──────────────────────────────────────────
    // Data extraction helpers
    // ──────────────────────────────────────────

    /// <summary>
    /// All constant string values declared on <see cref="ResourceKinds"/>.
    /// This is the authoritative source of truth for resource kind identifiers.
    /// </summary>
    private static IReadOnlySet<string> GetResourceKindConstants()
    {
        return typeof(ResourceKinds)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet();
    }

    /// <summary>
    /// All resource kind strings exposed by <see cref="ResourceDescriptors"/> public static fields.
    /// Extracted by reading the <c>Kind</c> property of each descriptor via reflection.
    /// </summary>
    private static IReadOnlySet<string> GetDescriptorKinds()
    {
        var kinds = new HashSet<string>();
        var descriptorFields = typeof(ResourceDescriptors)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType.IsGenericType &&
                        f.FieldType.GetGenericTypeDefinition() == typeof(ResourceDescriptor<>));

        foreach (var field in descriptorFields)
        {
            var descriptor = field.GetValue(null);
            if (descriptor is null) continue;

            var kindProp = descriptor.GetType().GetProperty("Kind");
            if (kindProp?.GetValue(descriptor) is string kind)
                kinds.Add(kind);
        }

        return kinds;
    }

    /// <summary>
    /// All resource kind strings registered in <see cref="ResourceDescriptorRegistry"/>.
    /// Extracted via reflection from the private ResourceKinds dictionary.
    /// </summary>
    private static IReadOnlySet<string> GetRegisteredResourceKinds()
    {
        var field = typeof(ResourceDescriptorRegistry)
            .GetField("ResourceKinds", BindingFlags.NonPublic | BindingFlags.Static);

        if (field?.GetValue(null) is not IReadOnlyDictionary<Type, string> registry)
            throw new InvalidOperationException("Could not read ResourceDescriptorRegistry.ResourceKinds via reflection.");

        return registry.Values.ToHashSet();
    }

    /// <summary>
    /// All resource kind strings that have a Cerbos YAML policy file.
    /// Extracted from the cerbos/policies/ directory by reading the resource field.
    /// </summary>
    private static IReadOnlySet<string> GetCerbosPolicyResourceKinds(string? policyDirectory = null)
    {
        return GetCerbosResourcePolicies(policyDirectory)
            .Select(document => RequiredScalar(document.Policy, "resource"))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// All resource kinds that have a JSON schema file in _schemas/.
    /// Schema files are named {resource_kind}.json.
    /// </summary>
    private static IReadOnlySet<string> GetCerbosSchemaResourceKinds(string? schemaDirectory = null)
    {
        schemaDirectory ??= CerbosSchemasPath;
        return Directory.GetFiles(schemaDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name != Path.GetFileNameWithoutExtension(NamespacedPrincipalSchemaFileName)) // principal schema is shared, not a resource kind
            .ToHashSet()!;
    }

    [Test]
    public async Task PolicyCatalogRejectsMissingArtifactRoot()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"authorization-missing-{Guid.NewGuid():N}");

        await Assert.That(() => GetCerbosPolicyResourceKinds(missing))
            .Throws<DirectoryNotFoundException>();
    }

    [Test]
    public async Task SchemaCatalogRejectsMissingArtifactRoot()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"authorization-missing-{Guid.NewGuid():N}");

        await Assert.That(() => GetCerbosSchemaResourceKinds(missing))
            .Throws<DirectoryNotFoundException>();
    }

    [Test]
    public async Task PolicyCatalogReadsResourceScalarInsteadOfComments()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"authorization-policy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(directory, "event.yaml"), """
                # resource: islamuevent_comment_only
                apiVersion: api.cerbos.dev/v1
                resourcePolicy:
                  resource: 'islamuevent_event'
                  version: default
                """);

            await Assert.That(GetCerbosPolicyResourceKinds(directory))
                .IsEquivalentTo(["islamuevent_event"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // ──────────────────────────────────────────
    // Provider parity tests (original)
    // ──────────────────────────────────────────

    [Test]
    [DisplayName("Every registered resource kind has a Cerbos YAML policy file")]
    public async Task RegisteredResourceKinds_ShouldHave_CerbosPolicy()
    {
        var registered = GetRegisteredResourceKinds();
        var cerbosPolicies = GetCerbosPolicyResourceKinds();

        var missing = registered.Except(cerbosPolicies).ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These resource kinds are in ResourceDescriptorRegistry but missing from " +
                     $"cerbos/policies/: [{string.Join(", ", missing)}]");
    }

    // ──────────────────────────────────────────
    // ResourceKinds catalog parity tests (Phase 6)
    // ──────────────────────────────────────────

    [Test]
    [DisplayName("Every ResourceKinds constant has a Cerbos YAML policy file")]
    public async Task ResourceKindConstants_ShouldHave_CerbosPolicy()
    {
        var constants = GetResourceKindConstants();
        var cerbosPolicies = GetCerbosPolicyResourceKinds();

        var missing = constants.Except(cerbosPolicies).ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These ResourceKinds constants have no Cerbos policy: [{string.Join(", ", missing)}]");
    }

    // ──────────────────────────────────────────
    // Descriptor catalog parity tests (Phase 6)
    // ──────────────────────────────────────────

    [Test]
    [DisplayName("Every ResourceDescriptors entry uses a valid ResourceKinds constant")]
    public async Task DescriptorKinds_ShouldBe_InResourceKinds()
    {
        var descriptorKinds = GetDescriptorKinds();
        var constants = GetResourceKindConstants();

        var unknown = descriptorKinds.Except(constants).ToList();

        await Assert.That(unknown)
            .IsEmpty()
            .Because($"These descriptor kinds are not in ResourceKinds: [{string.Join(", ", unknown)}]");
    }

    [Test]
    [DisplayName("ResourceDescriptorRegistry values are a subset of ResourceKinds constants")]
    public async Task RegistryValues_ShouldBe_InResourceKinds()
    {
        var registered = GetRegisteredResourceKinds();
        var constants = GetResourceKindConstants();

        var orphaned = registered.Except(constants).ToList();

        await Assert.That(orphaned)
            .IsEmpty()
            .Because($"These registry resource kinds have no ResourceKinds constant: [{string.Join(", ", orphaned)}]");
    }

    // ──────────────────────────────────────────
    // Schema coverage tests (Phase 6)
    // ──────────────────────────────────────────

    [Test]
    [DisplayName("Every Cerbos resource policy has a matching JSON schema file")]
    public async Task CerbosPolicies_ShouldHave_SchemaFile()
    {
        var cerbosPolicies = GetCerbosPolicyResourceKinds();
        var schemas = GetCerbosSchemaResourceKinds();

        var missing = cerbosPolicies.Except(schemas).ToList();

        await Assert.That(missing)
            .IsEmpty()
            .Because($"These Cerbos policies have no JSON schema in _schemas/: [{string.Join(", ", missing)}]");
    }

    [Test]
    [DisplayName("Principal JSON schema file exists")]
    public async Task PrincipalSchema_ShouldExist()
    {
        var principalSchemaPath = Path.Combine(CerbosSchemasPath, NamespacedPrincipalSchemaFileName);

        await Assert.That(File.Exists(principalSchemaPath))
            .IsTrue()
            .Because($"cerbos/policies/_schemas/{NamespacedPrincipalSchemaFileName} must exist for Cerbos schema validation.");
    }

    [Test]
    [DisplayName("Product-owned Cerbos resource kinds use the islamuevent namespace")]
    public async Task ProductOwnedResourceKinds_ShouldUse_IslamuEventNamespace()
    {
        var constants = GetResourceKindConstants();
        var cerbosPolicies = GetCerbosPolicyResourceKinds();
        var schemas = GetCerbosSchemaResourceKinds();

        var violations = constants
            .Concat(cerbosPolicies)
            .Concat(schemas)
            .Distinct()
            .Where(kind => !kind.StartsWith(ProductNamespacePrefix, StringComparison.Ordinal))
            .ToList();

        await Assert.That(violations)
            .IsEmpty()
            .Because("Product-owned Cerbos resource identifiers must be namespaced to avoid collisions with tenant/BYO policy packages.");
    }

    [Test]
    [DisplayName("Legacy bare Cerbos resource kinds are absent from namespaced contracts")]
    public async Task LegacyBareResourceKinds_ShouldBeAbsent_FromNamespacedContracts()
    {
        var constants = GetResourceKindConstants();
        var cerbosPolicies = GetCerbosPolicyResourceKinds();
        var schemas = GetCerbosSchemaResourceKinds();

        var violations = constants
            .Concat(cerbosPolicies)
            .Concat(schemas)
            .Distinct()
            .Intersect(LegacyBareResourceKinds)
            .ToList();

        await Assert.That(violations)
            .IsEmpty()
            .Because("The Phase 1 hard cut intentionally removes all old bare product-owned Cerbos resource identifiers.");
    }

    [Test]
    [DisplayName("Derived roles and principal role use islamuevent names")]
    public async Task DerivedRoles_ShouldUse_IslamuEventNamespace()
    {
        YamlMappingNode roles = RequiredMapping(ReadYaml(Path.Combine(CerbosPoliciesPath, "derived_roles.yaml")), "derivedRoles");
        await Assert.That(RequiredScalar(roles, "name")).IsEqualTo("islamuevent_explore_admin_roles");
        string[] violations = RequiredSequence(roles, "definitions").Children.Cast<YamlMappingNode>()
            .SelectMany(definition => new[] { RequiredScalar(definition, "name") }
                .Concat(RequiredSequence(definition, "parentRoles").Children.Select(ScalarValue)))
            .Where(identifier => !identifier.StartsWith(ProductNamespacePrefix, StringComparison.Ordinal))
            .ToArray();

        await Assert.That(violations)
            .IsEmpty()
            .Because("Static Cerbos principal and derived role identifiers are product-owned and must be namespaced.");
    }

    [Test]
    [DisplayName("Cerbos policies reference the namespaced principal schema")]
    public async Task CerbosPolicies_ShouldReference_NamespacedPrincipalSchema()
    {
        var violations = new List<string>();

        foreach (var (fileName, policy) in GetCerbosResourcePolicies())
        {
            var expectedSchema = RequiredScalar(policy, "resource") == ResourceKinds.EventResource
                ? "islamuevent_event_resource_principal.json"
                : NamespacedPrincipalSchemaFileName;
            string reference = RequiredScalar(RequiredMapping(RequiredMapping(policy, "schemas"), "principalSchema"), "ref");
            if (reference != $"cerbos:///{expectedSchema}")
                violations.Add($"{fileName}: missing namespaced principal schema reference");
        }

        await Assert.That(violations)
            .IsEmpty()
            .Because("Every resource policy must use its exact namespaced principal contract; resource snapshots require the closed enrichment-free schema.");
    }

    [Test]
    [DisplayName("Every Cerbos policy YAML references its schema")]
    public async Task CerbosPolicies_ShouldReference_Schema()
    {
        var missingRef = new List<string>();

        foreach (var (fileName, policy) in GetCerbosResourcePolicies())
        {
            string resource = RequiredScalar(policy, "resource");
            YamlMappingNode schemas = RequiredMapping(policy, "schemas");
            _ = RequiredScalar(RequiredMapping(schemas, "principalSchema"), "ref");
            if (RequiredScalar(RequiredMapping(schemas, "resourceSchema"), "ref") != $"cerbos:///{resource}.json")
            {
                missingRef.Add(fileName);
            }
        }

        await Assert.That(missingRef)
            .IsEmpty()
            .Because($"These Cerbos policy files do not reference their schema: [{string.Join(", ", missingRef)}]");
    }

    // ──────────────────────────────────────────
    // Utility methods
    // ──────────────────────────────────────────

    private static IEnumerable<(string FileName, YamlMappingNode Policy)> GetCerbosResourcePolicies(string? policyDirectory = null)
    {
        foreach (string file in Directory.GetFiles(policyDirectory ?? CerbosPoliciesPath, "*.yaml"))
        {
            YamlMappingNode document = ReadYaml(file);
            if (document.Children.ContainsKey(new YamlScalarNode("resourcePolicy")))
                yield return (Path.GetFileName(file), RequiredMapping(document, "resourcePolicy"));
            else
                _ = RequiredMapping(document, "derivedRoles");
        }
    }

    private static YamlMappingNode ReadYaml(string path)
    {
        using var reader = File.OpenText(path);
        var yaml = new YamlStream();
        yaml.Load(reader);
        return yaml.Documents.Single().RootNode as YamlMappingNode
            ?? throw new InvalidDataException("Expected a YAML mapping document.");
    }

    private static YamlMappingNode RequiredMapping(YamlMappingNode parent, string key) =>
        parent.Children[new YamlScalarNode(key)] as YamlMappingNode
        ?? throw new InvalidDataException($"Expected YAML mapping '{key}'.");

    private static YamlSequenceNode RequiredSequence(YamlMappingNode parent, string key) =>
        parent.Children[new YamlScalarNode(key)] as YamlSequenceNode
        ?? throw new InvalidDataException($"Expected YAML sequence '{key}'.");

    private static string RequiredScalar(YamlMappingNode parent, string key) =>
        ScalarValue(parent.Children[new YamlScalarNode(key)]);

    private static string ScalarValue(YamlNode node) =>
        node is YamlScalarNode { Value: { } value } && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidDataException("Expected a nonempty YAML scalar.");

}
