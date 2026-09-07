using System.Text.Json.Serialization;

namespace Explore.Domain.Services.Registration;

public sealed record FormFieldReference
{
    public FormFieldReference(string @namespace, string key)
    {
        Namespace = FormVersionRules.NormalizeNamespace(@namespace);
        Key = FormVersionRules.NormalizeKey(key);
    }

    public string Namespace { get; }
    public string Key { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "operator")]
[JsonDerivedType(typeof(FormCondition.EqualsCondition), "equals")]
[JsonDerivedType(typeof(FormCondition.NotEqualsCondition), "notEquals")]
[JsonDerivedType(typeof(FormCondition.InCondition), "in")]
[JsonDerivedType(typeof(FormCondition.ContainsCondition), "contains")]
[JsonDerivedType(typeof(FormCondition.ExistsCondition), "exists")]
[JsonDerivedType(typeof(FormCondition.CompareCondition), "compare")]
[JsonDerivedType(typeof(FormCondition.AllCondition), "all")]
[JsonDerivedType(typeof(FormCondition.AnyCondition), "any")]
[JsonDerivedType(typeof(FormCondition.NotCondition), "not")]
public abstract record FormCondition
{
    private FormCondition()
    {
    }

    public sealed record EqualsCondition(FormFieldReference Field, FormScalarValue Value) : FormCondition;

    public sealed record NotEqualsCondition(FormFieldReference Field, FormScalarValue Value) : FormCondition;

    public sealed record InCondition : FormCondition
    {
        public InCondition(FormFieldReference field, IReadOnlyList<FormScalarValue> values)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(values);
            if (values.Count == 0 || values.Any(value => value is null))
            {
                throw new ArgumentException("In requires at least one typed value.", nameof(values));
            }

            Field = field;
            Values = Array.AsReadOnly([.. values]);
        }

        public FormFieldReference Field { get; }
        public IReadOnlyList<FormScalarValue> Values { get; }
    }

    public sealed record ContainsCondition(FormFieldReference Field, FormScalarValue Value) : FormCondition;

    public sealed record ExistsCondition(FormFieldReference Field) : FormCondition;

    public sealed record CompareCondition : FormCondition
    {
        public CompareCondition(FormFieldReference field, FormComparisonKind comparison, FormScalarValue value)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(value);
            if (value is not FormScalarValue.Number and not FormScalarValue.Date)
            {
                throw new ArgumentException("Compare accepts numeric or date values only.", nameof(value));
            }

            if (!Enum.IsDefined(comparison))
            {
                throw new ArgumentOutOfRangeException(nameof(comparison));
            }

            Field = field;
            Comparison = comparison;
            Value = value;
        }

        public FormFieldReference Field { get; }
        public FormComparisonKind Comparison { get; }
        public FormScalarValue Value { get; }
    }

    public sealed record AllCondition : FormCondition
    {
        public AllCondition(IReadOnlyList<FormCondition> conditions)
        {
            Conditions = RequireConditions(conditions, nameof(conditions));
        }

        public IReadOnlyList<FormCondition> Conditions { get; }
    }

    public sealed record AnyCondition : FormCondition
    {
        public AnyCondition(IReadOnlyList<FormCondition> conditions)
        {
            Conditions = RequireConditions(conditions, nameof(conditions));
        }

        public IReadOnlyList<FormCondition> Conditions { get; }
    }

    public sealed record NotCondition(FormCondition Condition) : FormCondition;

    private static IReadOnlyList<FormCondition> RequireConditions(
        IReadOnlyList<FormCondition> conditions,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        if (conditions.Count == 0 || conditions.Any(condition => condition is null))
        {
            throw new ArgumentException("Condition group cannot be empty.", parameterName);
        }

        return Array.AsReadOnly([.. conditions]);
    }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(FormScalarValue.Null), "null")]
[JsonDerivedType(typeof(FormScalarValue.Text), "text")]
[JsonDerivedType(typeof(FormScalarValue.Boolean), "boolean")]
[JsonDerivedType(typeof(FormScalarValue.Number), "number")]
[JsonDerivedType(typeof(FormScalarValue.Date), "date")]
public abstract record FormScalarValue
{
    private FormScalarValue()
    {
    }

    public static Null NullValue { get; } = new();

    public static Text From(string value) => new(value);
    public static Boolean From(bool value) => new(value);
    public static Number From(decimal value) => new(value);
    public static Date From(DateOnly value) => new(value);

    public sealed record Null : FormScalarValue;

    public sealed record Text : FormScalarValue
    {
        public Text(string value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }

        public string Value { get; }
    }

    public sealed record Boolean(bool Value) : FormScalarValue;
    public sealed record Number(decimal Value) : FormScalarValue;
    public sealed record Date(DateOnly Value) : FormScalarValue;
}

public sealed record FormAnswerValue
{
    private FormAnswerValue(FormScalarValue? scalar, IReadOnlyList<FormScalarValue>? values)
    {
        Scalar = scalar;
        Values = values;
    }

    public FormScalarValue? Scalar { get; }
    public IReadOnlyList<FormScalarValue>? Values { get; }

    public static FormAnswerValue From(string value) => From(FormScalarValue.From(value));
    public static FormAnswerValue From(bool value) => From(FormScalarValue.From(value));
    public static FormAnswerValue From(decimal value) => From(FormScalarValue.From(value));
    public static FormAnswerValue From(DateOnly value) => From(FormScalarValue.From(value));
    public static FormAnswerValue From(FormScalarValue value) => new(
        value ?? throw new ArgumentNullException(nameof(value)), null);

    public static FormAnswerValue From(IReadOnlyList<FormScalarValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(value => value is null))
        {
            throw new ArgumentException("Answer lists cannot contain null references.", nameof(values));
        }

        return new FormAnswerValue(null, Array.AsReadOnly([.. values]));
    }
}

public enum FormComparisonKind
{
    LessThan = 1,
    LessThanOrEqual = 2,
    GreaterThan = 3,
    GreaterThanOrEqual = 4,
    Equal = 5,
    NotEqual = 6
}

public static class FormConditionEvaluator
{
    public static bool Evaluate(
        FormCondition condition,
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(answers);
        return condition switch
        {
            FormCondition.EqualsCondition equals => TryScalar(answers, equals.Field, out FormScalarValue? actual) &&
                                           actual == equals.Value,
            FormCondition.NotEqualsCondition notEquals => TryScalar(answers, notEquals.Field, out FormScalarValue? actual) &&
                                                 actual != notEquals.Value,
            FormCondition.InCondition @in => TryScalar(answers, @in.Field, out FormScalarValue? actual) &&
                                    @in.Values.Contains(actual),
            FormCondition.ContainsCondition contains => answers.TryGetValue(contains.Field, out FormAnswerValue? answer) &&
                                                answer.Values?.Contains(contains.Value) == true,
            FormCondition.ExistsCondition exists => answers.ContainsKey(exists.Field),
            FormCondition.CompareCondition compare => TryScalar(answers, compare.Field, out FormScalarValue? actual) &&
                                               Compare(actual!, compare.Value, compare.Comparison),
            FormCondition.AllCondition all => all.Conditions.All(child => Evaluate(child, answers)),
            FormCondition.AnyCondition any => any.Conditions.Any(child => Evaluate(child, answers)),
            FormCondition.NotCondition not => !Evaluate(not.Condition, answers),
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
    }

    public static IReadOnlyList<FormFieldReference> References(FormCondition condition)
    {
        ArgumentNullException.ThrowIfNull(condition);
        return condition switch
        {
            FormCondition.EqualsCondition equals => [equals.Field],
            FormCondition.NotEqualsCondition notEquals => [notEquals.Field],
            FormCondition.InCondition @in => [@in.Field],
            FormCondition.ContainsCondition contains => [contains.Field],
            FormCondition.ExistsCondition exists => [exists.Field],
            FormCondition.CompareCondition compare => [compare.Field],
            FormCondition.AllCondition all => [.. all.Conditions.SelectMany(References)],
            FormCondition.AnyCondition any => [.. any.Conditions.SelectMany(References)],
            FormCondition.NotCondition not => References(not.Condition),
            _ => throw new ArgumentOutOfRangeException(nameof(condition))
        };
    }

    private static bool TryScalar(
        IReadOnlyDictionary<FormFieldReference, FormAnswerValue> answers,
        FormFieldReference field,
        out FormScalarValue? value)
    {
        if (answers.TryGetValue(field, out FormAnswerValue? answer) && answer.Scalar is not null)
        {
            value = answer.Scalar;
            return true;
        }

        value = null;
        return false;
    }

    private static bool Compare(FormScalarValue actual, FormScalarValue expected, FormComparisonKind comparison)
    {
        int? result = (actual, expected) switch
        {
            (FormScalarValue.Number left, FormScalarValue.Number right) => left.Value.CompareTo(right.Value),
            (FormScalarValue.Date left, FormScalarValue.Date right) => left.Value.CompareTo(right.Value),
            _ => null
        };

        return result is not null && comparison switch
        {
            FormComparisonKind.LessThan => result < 0,
            FormComparisonKind.LessThanOrEqual => result <= 0,
            FormComparisonKind.GreaterThan => result > 0,
            FormComparisonKind.GreaterThanOrEqual => result >= 0,
            FormComparisonKind.Equal => result == 0,
            FormComparisonKind.NotEqual => result != 0,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison))
        };
    }
}
