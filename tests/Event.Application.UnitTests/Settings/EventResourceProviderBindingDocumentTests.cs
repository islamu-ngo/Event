using System.Text.Json;
using Explore.Application.Settings;

namespace Event.Application.UnitTests.Settings;

public sealed class EventResourceProviderBindingDocumentTests
{
    [Test]
    public async Task BindingAndDocumentDoNotRetainCallerOwnedCollectionAuthority()
    {
        string[] endpoints = ["https://pdp.example.test"];
        var binding = new EventResourceDeploymentBinding(Guid.CreateVersion7(), endpoints, "", "default");
        EventResourceDeploymentBinding[] deployments = [binding];
        var document = new EventResourceProviderBindingDocument(Guid.CreateVersion7(), deployments);
        endpoints[0] = "https://different.example.test";
        deployments[0] = new(Guid.CreateVersion7(), ["https://other.example.test"], "", "default");

        await Assert.That(document.Deployments.Single().DeploymentId).IsEqualTo(binding.DeploymentId);
        await Assert.That(document.Deployments.Single().Endpoints.Single()).IsEqualTo("https://pdp.example.test");
    }

    [Test]
    public async Task RecordCopySnapshotsReplacementEndpointAliases()
    {
        var binding = new EventResourceDeploymentBinding(
            Guid.CreateVersion7(), ["https://pdp.example.test"], "", "default");
        string[] replacement = ["https://alias.example.test"];
        var copy = binding with { Endpoints = replacement };
        replacement[0] = "https://different.example.test";
        await Assert.That(copy.Endpoints.Single()).IsEqualTo("https://alias.example.test");
        await Assert.That(binding.Endpoints.Single()).IsEqualTo("https://pdp.example.test");
    }

    [Test]
    public async Task BindingDocumentRoundTripPreservesDeploymentAndExplicitRootScope()
    {
        var binding = new EventResourceDeploymentBinding(
            Guid.CreateVersion7(), ["https://pdp.example.test"], "", "default");
        var document = new EventResourceProviderBindingDocument(Guid.CreateVersion7(), [binding]);
        var json = JsonSerializer.Serialize(document, EventResourceProviderBindingDocument.JsonOptions);
        var restored = EventResourceProviderBindingDocument.Parse(json);
        await Assert.That(restored.Revision).IsEqualTo(document.Revision);
        await Assert.That(restored.Deployments.Single().DeploymentId).IsEqualTo(binding.DeploymentId);
        await Assert.That(restored.Deployments.Single().Scope).IsEqualTo("");
        await Assert.That(restored.Deployments.Single().Endpoints).IsEquivalentTo(binding.Endpoints);
    }
}
