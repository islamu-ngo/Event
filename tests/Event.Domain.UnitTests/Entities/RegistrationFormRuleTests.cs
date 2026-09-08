using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.Services.Registration;

namespace Event.Domain.UnitTests.Entities;

public sealed class RegistrationFormRuleTests
{
    private static readonly DateTime Now = new(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FormFieldReference Country = new("platform.registration", "country");
    private static readonly FormFieldReference Tags = new("platform.registration", "tags");
    private static readonly FormFieldReference Age = new("platform.registration", "age");
    private static readonly FormFieldReference BirthDate = new("platform.registration", "birth-date");

    [Test]
    public async Task Evaluator_ImplementsAllNineOperatorsAndNestedComposition()
    {
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers = new Dictionary<FormFieldReference, FormAnswerValue>
        {
            [Country] = FormAnswerValue.From("BE"),
            [Tags] = FormAnswerValue.From([FormScalarValue.From("student"), FormScalarValue.From("volunteer")]),
            [Age] = FormAnswerValue.From(21m)
        };

        FormCondition[] conditions =
        [
            new FormCondition.EqualsCondition(Country, FormScalarValue.From("BE")),
            new FormCondition.NotEqualsCondition(Country, FormScalarValue.From("NL")),
            new FormCondition.InCondition(Country, [FormScalarValue.From("BE"), FormScalarValue.From("DE")]),
            new FormCondition.ContainsCondition(Tags, FormScalarValue.From("student")),
            new FormCondition.ExistsCondition(Country),
            new FormCondition.CompareCondition(Age, FormComparisonKind.GreaterThanOrEqual, FormScalarValue.From(18m)),
            new FormCondition.AllCondition([
                new FormCondition.ExistsCondition(Country),
                new FormCondition.EqualsCondition(Country, FormScalarValue.From("BE"))]),
            new FormCondition.AnyCondition([
                new FormCondition.EqualsCondition(Country, FormScalarValue.From("NL")),
                new FormCondition.EqualsCondition(Country, FormScalarValue.From("BE"))]),
            new FormCondition.NotCondition(new FormCondition.EqualsCondition(Country, FormScalarValue.From("NL")))
        ];

        foreach (FormCondition condition in conditions)
        {
            await Assert.That(FormConditionEvaluator.Evaluate(condition, answers)).IsTrue();
        }
    }

    [Test]
    public async Task Evaluator_DistinguishesMissingNullScalarAndListValues()
    {
        FormFieldReference missing = new("platform.registration", "missing");
        FormFieldReference explicitNull = new("platform.registration", "null");
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers = new Dictionary<FormFieldReference, FormAnswerValue>
        {
            [explicitNull] = FormAnswerValue.From(FormScalarValue.NullValue),
            [Country] = FormAnswerValue.From("BE"),
            [Tags] = FormAnswerValue.From([FormScalarValue.From("BE"), FormScalarValue.From("NL")])
        };

        await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.ExistsCondition(missing), answers)).IsFalse();
        await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.ExistsCondition(explicitNull), answers)).IsTrue();
        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.EqualsCondition(explicitNull, FormScalarValue.NullValue), answers)).IsTrue();
        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.NotEqualsCondition(missing, FormScalarValue.From("anything")), answers)).IsFalse();
        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.ContainsCondition(Tags, FormScalarValue.From("BE")), answers)).IsTrue();
        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.ContainsCondition(Country, FormScalarValue.From("B")), answers)).IsFalse();
    }

    [Test]
    public async Task Compare_IsBoundedToInvariantNumericAndDateValues()
    {
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers = new Dictionary<FormFieldReference, FormAnswerValue>
        {
            [Age] = FormAnswerValue.From(10.5m),
            [BirthDate] = FormAnswerValue.From(new DateOnly(2000, 1, 2))
        };

        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.CompareCondition(Age, FormComparisonKind.LessThan, FormScalarValue.From(11m)), answers)).IsTrue();
        await Assert.That(FormConditionEvaluator.Evaluate(
            new FormCondition.CompareCondition(BirthDate, FormComparisonKind.GreaterThan,
                FormScalarValue.From(new DateOnly(1999, 12, 31))), answers)).IsTrue();
        await Assert.That(() => new FormCondition.CompareCondition(
            Country, FormComparisonKind.Equal, FormScalarValue.From("BE"))).Throws<ArgumentException>();
    }

    [Test]
    public async Task Evaluator_GeneratedProperties_PreserveTypedOrderingCompositionAndSnapshots()
    {
        Random random = new(7303);
        FormFieldReference number = new("platform.registration", "generated-number");
        FormFieldReference date = new("platform.registration", "generated-date");
        FormFieldReference flag = new("platform.registration", "generated-flag");
        DateOnly epoch = new(2000, 1, 1);

        for (int sample = 0; sample < 64; sample++)
        {
            decimal numericActual = random.Next(-1_000, 1_001) / 10m;
            decimal numericExpected = random.Next(-1_000, 1_001) / 10m;
            DateOnly dateActual = epoch.AddDays(random.Next(0, 3_661));
            DateOnly dateExpected = epoch.AddDays(random.Next(0, 3_661));
            bool flagValue = random.Next(2) == 0;
            Dictionary<FormFieldReference, FormAnswerValue> answers = new()
            {
                [number] = FormAnswerValue.From(numericActual),
                [date] = FormAnswerValue.From(dateActual),
                [flag] = FormAnswerValue.From(flagValue)
            };

            foreach (FormComparisonKind comparison in Enum.GetValues<FormComparisonKind>())
            {
                bool numericResult = FormConditionEvaluator.Evaluate(
                    new FormCondition.CompareCondition(number, comparison, FormScalarValue.From(numericExpected)), answers);
                bool dateResult = FormConditionEvaluator.Evaluate(
                    new FormCondition.CompareCondition(date, comparison, FormScalarValue.From(dateExpected)), answers);

                await Assert.That(numericResult).IsEqualTo(Matches(numericActual.CompareTo(numericExpected), comparison));
                await Assert.That(dateResult).IsEqualTo(Matches(dateActual.CompareTo(dateExpected), comparison));
            }

            FormCondition value = new FormCondition.CompareCondition(number, FormComparisonKind.LessThan, FormScalarValue.From(numericExpected));
            FormCondition truth = new FormCondition.EqualsCondition(flag, FormScalarValue.From(flagValue));
            FormCondition falsity = new FormCondition.NotEqualsCondition(flag, FormScalarValue.From(flagValue));
            bool expectedValue = FormConditionEvaluator.Evaluate(value, answers);
            KeyValuePair<FormFieldReference, FormAnswerValue>[] snapshot = [.. answers];

            await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.AllCondition([value, truth]), answers)).IsEqualTo(expectedValue);
            await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.AnyCondition([value, falsity]), answers)).IsEqualTo(expectedValue);
            await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.AllCondition([value, falsity]), answers)).IsFalse();
            await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.AnyCondition([value, truth]), answers)).IsTrue();
            await Assert.That(FormConditionEvaluator.Evaluate(new FormCondition.NotCondition(new FormCondition.NotCondition(value)), answers)).IsEqualTo(expectedValue);
            await Assert.That(FormConditionEvaluator.Evaluate(value, answers)).IsEqualTo(expectedValue);
            await Assert.That(answers.SequenceEqual(snapshot)).IsTrue();
            await Assert.That(FormConditionEvaluator.Evaluate(
                new FormCondition.CompareCondition(number, FormComparisonKind.Equal, FormScalarValue.From(dateExpected)), answers)).IsFalse();
        }

        await Assert.That(() => new FormCondition.CompareCondition(
            number, (FormComparisonKind)int.MaxValue, FormScalarValue.From(1m))).Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task ConditionConstruction_RejectsEmptyCombinatorsAndEmptyInValues()
    {
        await Assert.That(() => new FormCondition.AllCondition([])).Throws<ArgumentException>();
        await Assert.That(() => new FormCondition.AnyCondition([])).Throws<ArgumentException>();
        await Assert.That(() => new FormCondition.InCondition(Country, [])).Throws<ArgumentException>();
    }

    [Test]
    public async Task ConditionAst_IsClosedToNineOperators_AndEvaluatorHasNoStateOrIoDependencies()
    {
        Type[] operators = typeof(FormCondition).GetNestedTypes()
            .Where(type => type.IsAssignableTo(typeof(FormCondition)))
            .ToArray();
        string[] operatorNames =
        [
            "EqualsCondition", "NotEqualsCondition", "InCondition", "ContainsCondition", "ExistsCondition",
            "CompareCondition", "AllCondition", "AnyCondition", "NotCondition"
        ];

        await Assert.That(typeof(FormCondition).IsAbstract).IsTrue();
        await Assert.That(operators.Select(type => type.Name)).IsEquivalentTo(operatorNames);
        await Assert.That(operators.All(type => type.IsSealed)).IsTrue();
        await Assert.That(typeof(FormConditionEvaluator).GetFields().Length).IsEqualTo(0);
        await Assert.That(typeof(FormConditionEvaluator).GetConstructors().Length).IsEqualTo(0);
        await Assert.That(operators.SelectMany(type => type.GetProperties())
            .Any(property => typeof(Delegate).IsAssignableFrom(property.PropertyType))).IsFalse();
    }

    [Test]
    public async Task AddRule_RejectsUnknownForwardAndSelfReferences()
    {
        FormGraph graph = Graph();
        FormFieldReference target = Reference(graph.Target);

        await Assert.That(() => graph.Version.AddRule(Rule(graph.Version, target,
            new FormCondition.ExistsCondition(new FormFieldReference("platform.registration", "unknown")), 1)))
            .Throws<ArgumentException>();
        await Assert.That(() => graph.Version.AddRule(Rule(graph.Version, Reference(graph.Earlier),
            new FormCondition.ExistsCondition(target), 1))).Throws<ArgumentException>();
        await Assert.That(() => graph.Version.AddRule(Rule(graph.Version, target,
            new FormCondition.ExistsCondition(target), 1))).Throws<ArgumentException>();
    }

    [Test]
    public async Task RuleOrdinal_MustBePositiveAndUniqueWithinVersion()
    {
        FormGraph graph = Graph();
        FormFieldReference target = Reference(graph.Target);
        FormCondition condition = new FormCondition.ExistsCondition(Reference(graph.Earlier));

        await Assert.That(() => Rule(graph.Version, target, condition, 0)).Throws<ArgumentOutOfRangeException>();
        graph.Version.AddRule(Rule(graph.Version, target, condition, 1));
        await Assert.That(() => graph.Version.AddRule(Rule(graph.Version, target, condition, 1)))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task DraftVersion_ReordersAndSoftDeletesRulesThroughAggregateOnly()
    {
        FormGraph graph = Graph();
        FormCondition condition = new FormCondition.ExistsCondition(Reference(graph.Earlier));
        RegistrationFormRule first = Rule(graph.Version, Reference(graph.Target), condition, 1);
        RegistrationFormRule second = Rule(graph.Version, Reference(graph.Target), condition, 2);
        graph.Version.AddRule(first);
        graph.Version.AddRule(second);

        graph.Version.ReorderRules([second.Id, first.Id]);
        graph.Version.RemoveRule(first, Now.AddMinutes(1));

        await Assert.That(second.Ordinal).IsEqualTo(1);
        await Assert.That(first.Ordinal).IsEqualTo(2);
        await Assert.That(first.IsDeleted).IsTrue();
        await Assert.That(first.DeletedAt).IsEqualTo(Now.AddMinutes(1));
    }

    [Test]
    public async Task PublishedVersion_RejectsRuleAddRemoveAndReorder()
    {
        FormGraph graph = Graph();
        RegistrationFormRule rule = Rule(graph.Version, Reference(graph.Target),
            new FormCondition.ExistsCondition(Reference(graph.Earlier)), 1);
        graph.Version.AddRule(rule);
        graph.Version.PinGeneratedSchemaBundle(SchemaBundle(graph.Version), Now.AddMinutes(1));

        await Assert.That(() => graph.Version.AddRule(Rule(graph.Version, Reference(graph.Target),
            new FormCondition.ExistsCondition(Reference(graph.Earlier)), 2))).Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.RemoveRule(rule, Now.AddMinutes(2))).Throws<InvalidOperationException>();
        await Assert.That(() => graph.Version.ReorderRules([rule.Id])).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task CloneToDraft_GivesRulesNewIdsAndPreservesStableReferences()
    {
        FormGraph graph = Graph();
        RegistrationFormRule sourceRule = Rule(graph.Version, Reference(graph.Target),
            new FormCondition.EqualsCondition(Reference(graph.Earlier), FormScalarValue.From("yes")), 1);
        graph.Version.AddRule(sourceRule);
        graph.Version.PinGeneratedSchemaBundle(SchemaBundle(graph.Version), Now.AddMinutes(1));

        RegistrationFormVersion clone = graph.Version.CloneToDraft(2, Now.AddMinutes(2));
        RegistrationFormRule clonedRule = clone.Rules.Single();

        await Assert.That(clonedRule.Id).IsNotEqualTo(sourceRule.Id);
        await Assert.That(clonedRule.Target).IsEqualTo(sourceRule.Target);
        await Assert.That(clonedRule.Condition).IsEqualTo(sourceRule.Condition);
    }

    private static RegistrationFormRule Rule(
        RegistrationFormVersion version,
        FormFieldReference target,
        FormCondition condition,
        int ordinal) => RegistrationFormRule.Create(
            Guid.CreateVersion7(), version, ordinal, target, RegistrationFormRuleEffect.Show, condition, Now);

    private static FormGraph Graph()
    {
        RegistrationForm form = RegistrationForm.Create(
            Guid.CreateVersion7(), Guid.CreateVersion7(), "platform.registration", "attendee", "Attendee", Now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, Now);
        RegistrationFormSection first = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "First", Now);
        RegistrationFormSection second = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 2, "Second", Now);
        version.AddSection(first);
        version.AddSection(second);
        RegistrationFormField earlier = Field(first, 1, "earlier");
        RegistrationFormField target = Field(second, 1, "target");
        version.AddField(first, earlier);
        version.AddField(second, target);
        return new FormGraph(version, earlier, target);
    }

    private static RegistrationFormField Field(RegistrationFormSection section, int ordinal, string key) =>
        RegistrationFormField.Create(Guid.CreateVersion7(), section, ordinal, "platform.registration", key, key,
            RegistrationFieldTypeEnum.ShortText, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, true, Now);

    private static FormFieldReference Reference(RegistrationFormField field) => new(field.Namespace, field.Key);

    private static string SchemaBundle(RegistrationFormVersion version) =>
        $"{{\"$schema\":\"https://json-schema.org/draft/2020-12/schema\",\"versionId\":\"{version.Id:D}\",\"version\":{version.Version},\"languageTag\":\"{version.LanguageTag}\",\"data\":{{\"type\":\"object\"}},\"ui\":{{\"sections\":[]}},\"logic\":{{\"rules\":[]}},\"mapping\":{{\"fields\":[],\"options\":[]}}}}";

    private static bool Matches(int comparison, FormComparisonKind kind) => kind switch
    {
        FormComparisonKind.LessThan => comparison < 0,
        FormComparisonKind.LessThanOrEqual => comparison <= 0,
        FormComparisonKind.GreaterThan => comparison > 0,
        FormComparisonKind.GreaterThanOrEqual => comparison >= 0,
        FormComparisonKind.Equal => comparison == 0,
        FormComparisonKind.NotEqual => comparison != 0,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private sealed record FormGraph(
        RegistrationFormVersion Version,
        RegistrationFormField Earlier,
        RegistrationFormField Target);
}
