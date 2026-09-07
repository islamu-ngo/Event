using Explore.Domain;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Services.Registration;

public sealed class EventPublicActionRulesTests
{
    [Test]
    public async Task EnsureValid_ZeroActions_IsAllowed()
    {
        EventPublicActionRules.EnsureValid([]);

        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task EnsureValid_TwoPrimaryActions_Throws()
    {
        EventPublicAction[] actions =
        [
            new() { IsPrimary = true },
            new() { IsPrimary = true }
        ];

        await Assert.That(() => EventPublicActionRules.EnsureValid(actions)).Throws<InvalidOperationException>();
    }
}
