using Explore.Application.Features.EventReporting.Models;
using Explore.Infrastructure.Configuration;

namespace Explore.Infrastructure.Tests.Infrastructure.Moderation;

public sealed class ModerationProviderOptionsValidatorTests
{
    private readonly ModerationProviderOptionsValidator _validator = new();

    [Test]
    public async Task Validate_WithDefaultOptions_SucceedsForLocalOnlyMode()
    {
        var result = _validator.Validate(null, new ModerationProviderOptions());

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task Validate_WithUnknownMode_Fails()
    {
        var result = _validator.Validate(null, new ModerationProviderOptions
        {
            Mode = "Unknown"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Mode must be Disabled, LocalOnly, Osprey, Coop, or Composite.");
    }

    [Test]
    public async Task Validate_WithWhitespaceMode_UsesTrimmedMode()
    {
        var options = new ModerationProviderOptions
        {
            Mode = $" {ModerationProviderOptions.ModeLocalOnly} "
        };

        var result = _validator.Validate(null, options);

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(options.IsLocalOnly).IsTrue();
    }

    [Test]
    public async Task Validate_WithReporterTextInLocalOnlyMode_Fails()
    {
        var result = _validator.Validate(null, new ModerationProviderOptions
        {
            Mode = ModerationProviderOptions.ModeLocalOnly,
            EvidenceMode = EventReportProviderEvidenceMode.ReporterText
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:EvidenceMode cannot be ReporterText when Reporting:Mode is Disabled or LocalOnly.");
    }

    [Test]
    public async Task OspreyValidate_WhenEnabledWithoutEndpoint_Fails()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Osprey:EndpointUrl is required when Reporting:Osprey:Enabled is true and Transport is HttpJson.");
    }

    [Test]
    public async Task OspreyValidate_WithUnknownTransport_Fails()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Transport = "Udp"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Osprey:Transport must be 'HttpJson' or 'Grpc'.");
    }

    [Test]
    public async Task OspreyValidate_WithGrpcTransportWithoutEndpoint_Fails()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true,
            Transport = OspreyProviderOptions.TransportGrpc
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Osprey:GrpcEndpointUrl is required when Reporting:Osprey:Enabled is true and Transport is Grpc.");
    }

    [Test]
    public async Task OspreyValidate_WithPrivateEndpointWithoutOptIn_Fails()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true,
            EndpointUrl = "http://127.0.0.1:7777"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Osprey:EndpointUrl must not target local, loopback, link-local, or private network hosts unless Reporting:Osprey:AllowLocalProviderEndpoints is true.");
    }

    [Test]
    public async Task OspreyValidate_WithPrivateEndpointOptIn_Succeeds()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true,
            EndpointUrl = "http://127.0.0.1:7777",
            AllowLocalProviderEndpoints = true
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task OspreyValidate_WithPrivateGrpcEndpointWithoutOptIn_Fails()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true,
            Transport = OspreyProviderOptions.TransportGrpc,
            GrpcEndpointUrl = "http://127.0.0.1:19951"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Osprey:GrpcEndpointUrl must not target local, loopback, link-local, or private network hosts unless Reporting:Osprey:AllowLocalProviderEndpoints is true.");
    }

    [Test]
    public async Task OspreyValidate_WithPrivateGrpcEndpointOptIn_Succeeds()
    {
        var result = new OspreyProviderOptionsValidator().Validate(null, new OspreyProviderOptions
        {
            Enabled = true,
            Transport = OspreyProviderOptions.TransportGrpc,
            GrpcEndpointUrl = "http://127.0.0.1:19951",
            AllowLocalProviderEndpoints = true
        });

        await Assert.That(result.Succeeded).IsTrue();
    }

    [Test]
    public async Task CoopValidate_WhenEnabledWithoutEndpoint_Fails()
    {
        var result = new CoopProviderOptionsValidator().Validate(null, new CoopProviderOptions
        {
            Enabled = true
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Coop:EndpointUrl is required when Reporting:Coop:Enabled is true.");
    }

    [Test]
    public async Task CoopValidate_WithPrivateEndpointWithoutOptIn_Fails()
    {
        var result = new CoopProviderOptionsValidator().Validate(null, new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "http://127.0.0.1:7777"
        });

        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(result.Failures).Contains("Reporting:Coop:EndpointUrl must not target local, loopback, link-local, or private network hosts unless Reporting:Coop:AllowLocalProviderEndpoints is true.");
    }

    [Test]
    public async Task CoopValidate_WithPrivateEndpointOptIn_Succeeds()
    {
        var result = new CoopProviderOptionsValidator().Validate(null, new CoopProviderOptions
        {
            Enabled = true,
            EndpointUrl = "http://127.0.0.1:7777",
            AllowLocalProviderEndpoints = true
        });

        await Assert.That(result.Succeeded).IsTrue();
    }
}
