using Explore.Application.Specifications.Events;

namespace Event.Application.UnitTests.Specifications;

public sealed class EventDiscoveryOperandTests
{
    [Test]
    public async Task CollectionOperandsAndPredicatesShareAnImmutableSnapshot()
    {
        var ids = new List<int> { 2, 7 };
        var filter = EventFilter.Formats(ids);
        ids.Clear();
        ids.Add(9);
        var entity = new Explore.Domain.Event
        {
            Title = "Witness",
            EventFormatId = 7,
            Actor = null!,
            Tenant = null!,
            EventFormat = null!,
            EventStatus = null!,
            VisibilityType = null!
        };
        await Assert.That(filter.Predicate.Compile()(entity)).IsTrue();
        await Assert.That(filter.Operand.Integers.SequenceEqual(new[] { 2, 7 })).IsTrue();
    }

    [Test]
    public async Task RepeatedAspectClausesRetainTheirIndependentOperands()
    {
        var query = new EventQuerySpecification()
            .And(TechAspectFilter.TechStack("C#"))
            .And(TechAspectFilter.TechStack("PostgreSQL"))
            .And(IslamicAspectFilter.GenderModes([1, 2]));
        var clauses = query.Filters.OfType<TechAspectFilter>().ToArray();
        await Assert.That(clauses.Length).IsEqualTo(2);
        await Assert.That(clauses[0].Operand.Text).IsEqualTo("C#");
        await Assert.That(clauses[1].Operand.Text).IsEqualTo("PostgreSQL");
        await Assert.That(query.Filters.OfType<IslamicAspectFilter>().Single().Operand.Integers.Length).IsEqualTo(2);
    }
}
