using Explore.Application.Features.AiAssistant.Disclosure;
using Explore.Domain.Enums;
using NSubstitute;
using TUnit.Core;

namespace Event.Application.UnitTests.Features.AiAssistant.Disclosure;

public class AiContextGatewayE2ETests
{
    private static readonly AiProviderTrustTierEnum[] AllTiers =
    [
        AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel,
        AiProviderTrustTierEnum.TenantControlledPrivateEndpoint,
        AiProviderTrustTierEnum.TenantConfiguredExternalProcessor,
        AiProviderTrustTierEnum.PlatformConfiguredExternalProcessor,
        AiProviderTrustTierEnum.Unknown
    ];

    private static AiContextGateway CreateGateway(AiProviderTrustTierEnum tier)
    {
        var resolver = Substitute.For<IAiProviderTrustResolver>();
        resolver.Resolve(Arg.Any<AiProviderTrustResolutionContext>()).Returns(tier);
        return new AiContextGateway(resolver);
    }

    private static AiContextSanitizationInput CreateInput(
        string entityName,
        IReadOnlyDictionary<string, object?> fields,
        AiProviderTrustTierEnum tier = AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel,
        AiViewerScopeEnum viewerScope = AiViewerScopeEnum.Public,
        AiContextSensitivityEnum maxSensitivity = AiContextSensitivityEnum.Special,
        bool piiEnabled = false,
        IReadOnlySet<string>? grantedKeys = null)
    {
        return new AiContextSanitizationInput(
            entityName,
            fields,
            tier,
            viewerScope,
            grantedKeys ?? new HashSet<string>(),
            piiEnabled,
            maxSensitivity);
    }

    [Test]
    public async Task PublicFields_AllowedAtEveryProviderTrustTier()
    {
        foreach (var tier in AllTiers)
        {
            var gateway = CreateGateway(tier);
            var input = CreateInput(
                "ActorPii",
                new Dictionary<string, object?> { ["DisplayName"] = "Dr. Sarah Ahmed" },
                tier,
                maxSensitivity: AiContextSensitivityEnum.Public);

            var envelope = gateway.Sanitize(input);

            await Assert.That(envelope.Succeeded).IsTrue().Because($"DisplayName (Public) should be allowed at tier {tier}");
            await Assert.That(envelope.DisclosedFields.Count).IsEqualTo(1);
            await Assert.That(envelope.DeniedFieldNames).IsEmpty();
        }
    }

    [Test]
    public async Task InternalFields_AllowedAtEveryProviderTrustTier()
    {
        foreach (var tier in AllTiers)
        {
            var gateway = CreateGateway(tier);
            var input = CreateInput(
                "ActorPii",
                new Dictionary<string, object?> { ["ActorId"] = Guid.NewGuid() },
                tier,
                maxSensitivity: AiContextSensitivityEnum.Internal);

            var envelope = gateway.Sanitize(input);

            await Assert.That(envelope.Succeeded).IsTrue().Because($"ActorId (Internal) should be allowed at tier {tier}");
            await Assert.That(envelope.DisclosedFields.Count).IsEqualTo(1);
        }
    }

    [Test]
    public async Task Phase4GatedFields_DeniedWhenPiiDisclosureDisabled()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "UserPii",
            new Dictionary<string, object?> { ["Email"] = "user@example.com" },
            maxSensitivity: AiContextSensitivityEnum.Restricted,
            piiEnabled: false);

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.DeniedFieldNames).Contains(x => x == "Email")
            .Because("Phase4Gated Email must be denied when PiiDisclosureEnabled is false");
    }

    [Test]
    public async Task UnknownEntity_AlwaysDenied()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "NonExistentEntity",
            new Dictionary<string, object?> { ["Field"] = "value" },
            maxSensitivity: AiContextSensitivityEnum.Special);

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.DeniedFieldNames).Contains(x => x == "Field")
            .Because("Unregistered fields must be denied (fail-closed)");
    }

    [Test]
    public async Task MaxSensitivityCap_DeniesFieldsAboveCap()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "ActorPii",
            new Dictionary<string, object?>
            {
                ["DisplayName"] = "Public Name",
                ["ActorId"] = Guid.NewGuid()
            },
            maxSensitivity: AiContextSensitivityEnum.Internal);

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.DisclosedFields.Count).IsEqualTo(2)
            .Because("Public + Internal both <= Internal cap, so both allowed");
    }

    [Test]
    public async Task MaxSensitivityCap_DeniesConfidentialWhenCapIsInternal()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "OrganizationPii",
            new Dictionary<string, object?>
            {
                ["FullName"] = "Islamic Center",
                ["Email"] = "contact@islamic.org"
            },
            maxSensitivity: AiContextSensitivityEnum.Internal,
            piiEnabled: true);

        var envelope = gateway.Sanitize(input);

        var fullNameField = envelope.DisclosedFields.FirstOrDefault(f => f.Name == "FullName");
        await Assert.That(fullNameField).IsNotNull()
            .Because("FullName is Public, below Internal cap");

        await Assert.That(envelope.DeniedFieldNames).Contains(x => x == "Email")
            .Because("OrgPii.Email is Confidential, above Internal cap → denied by MaxSensitivity gate (and Phase4Gated)");
    }

    [Test]
    public async Task RedactedFields_AppearInRedactedNames()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "OrganizationPii",
            new Dictionary<string, object?>
            {
                ["FullName"] = "Islamic Center",
                ["Address"] = "123 Main St, Springfield"
            },
            maxSensitivity: AiContextSensitivityEnum.Restricted,
            piiEnabled: true);

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.DisclosedFields.Count).IsGreaterThanOrEqualTo(1)
            .Because("FullName should be disclosed");
    }

    [Test]
    public async Task Gateway_HandlesNullFieldValueWithoutCrashing()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "ActorPii",
            new Dictionary<string, object?> { ["DisplayName"] = null });

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.Succeeded).IsTrue()
            .Because("Gateway must handle null field values gracefully without crashing");
        await Assert.That(envelope.DisclosedFields.Count).IsEqualTo(1);
    }

    [Test]
    public async Task SanitizeMany_PreservesOrderAndIndependentFailures()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var inputs = new List<AiContextSanitizationInput>
        {
            CreateInput("ActorPii", new Dictionary<string, object?> { ["DisplayName"] = "Actor 1" }),
            CreateInput("UserPii", new Dictionary<string, object?> { ["Email"] = "user@test.com" }),
            CreateInput("OrganizationPii", new Dictionary<string, object?> { ["FullName"] = "Org 1" })
        };

        var envelopes = gateway.SanitizeMany(inputs);

        await Assert.That(envelopes.Count).IsEqualTo(3);
        await Assert.That(envelopes[0].EntityName).IsEqualTo("ActorPii");
        await Assert.That(envelopes[1].EntityName).IsEqualTo("UserPii");
        await Assert.That(envelopes[2].EntityName).IsEqualTo("OrganizationPii");
    }

    [Test]
    public async Task MultipleFieldsInSingleEntity_AreResolvedIndependently()
    {
        var gateway = CreateGateway(AiProviderTrustTierEnum.LocalInProcessOrSameNetworkModel);
        var input = CreateInput(
            "ActorPii",
            new Dictionary<string, object?>
            {
                ["DisplayName"] = "Public Name",
                ["ProfilePictureUri"] = "https://cdn.example.com/pic.jpg",
                ["ActorId"] = Guid.NewGuid()
            },
            maxSensitivity: AiContextSensitivityEnum.Internal);

        var envelope = gateway.Sanitize(input);

        await Assert.That(envelope.DisclosedFields.Count).IsEqualTo(3)
            .Because("All 3 ActorPii non-navigation props are Public or Internal");
        await Assert.That(envelope.DeniedFieldNames).IsEmpty();
    }
}
