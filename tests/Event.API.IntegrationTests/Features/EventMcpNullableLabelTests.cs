using System.Reflection;
using Explore.API.Mcp;

namespace Event.API.IntegrationTests.Features;

public sealed class EventMcpNullableLabelTests
{
    [Test]
    [Arguments(typeof(EventMcpSummaryDescriptor))]
    [Arguments(typeof(EventMcpDetailDescriptor))]
    public async Task EventLabels_DeclareTheirNullableOutputContract(Type descriptor)
    {
        string[] labels =
        [
            nameof(EventMcpSummaryDescriptor.EventType),
            nameof(EventMcpSummaryDescriptor.ActorDisplayName),
            nameof(EventMcpSummaryDescriptor.Status),
            nameof(EventMcpSummaryDescriptor.Visibility),
            nameof(EventMcpSummaryDescriptor.Format)
        ];
        var nullability = new NullabilityInfoContext();
        foreach (var label in labels)
        {
            var property = descriptor.GetProperty(label)!;
            await Assert.That(nullability.Create(property).ReadState)
                .IsEqualTo(NullabilityState.Nullable);
        }
    }
}
