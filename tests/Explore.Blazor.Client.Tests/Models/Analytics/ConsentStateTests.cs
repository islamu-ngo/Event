using Explore.Blazor.Client.Models.Analytics;

namespace Explore.Blazor.Client.Tests.Models.Analytics;

public class ConsentStateTests
{
    [Test]
    public async Task ConsentState_HasExactly7Values()
    {
        var values = Enum.GetValues<ConsentState>();

        await Assert.That(values).Count().IsEqualTo(7);
    }

    [Test]
    public async Task ConsentState_ValuesAreContiguousFromZero()
    {
        var values = Enum.GetValues<ConsentState>().Cast<int>().Order().ToList();

        await Assert.That(values).IsEquivalentTo(new[] { 0, 1, 2, 3, 4, 5, 6 });
    }

    [Test]
    public async Task ConsentState_UninitializedIsDefault()
    {
        var defaultState = default(ConsentState);

        await Assert.That(defaultState).IsEqualTo(ConsentState.Uninitialized);
        await Assert.That((int)defaultState).IsEqualTo(0);
    }

    [Test]
    public async Task ConsentState_TerminalStates_HaveExpectedValues()
    {
        await Assert.That((int)ConsentState.Accepted).IsEqualTo(4);
        await Assert.That((int)ConsentState.DeclinedCookieless).IsEqualTo(5);
        await Assert.That((int)ConsentState.DeclinedDisabled).IsEqualTo(6);
    }

    [Test]
    public async Task ConsentState_ParsesFromName()
    {
        var parsed = Enum.Parse<ConsentState>("BannerPendingCookieless");

        await Assert.That(parsed).IsEqualTo(ConsentState.BannerPendingCookieless);
    }
}
