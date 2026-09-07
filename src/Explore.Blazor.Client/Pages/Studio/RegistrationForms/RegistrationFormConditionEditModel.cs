using System.Text.Json;
using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio.RegistrationForms;

public sealed class RegistrationFormConditionEditModel
{
    public string Operator { get; set; } = "exists";
    public string? FieldReference { get; set; }
    public string Comparison { get; set; } = "equal";
    public RegistrationFormScalarEditModel Value { get; set; } = new();
    public List<RegistrationFormScalarEditModel> Values { get; } = [];
    public List<RegistrationFormConditionEditModel> Conditions { get; } = [];
    public RegistrationFormConditionEditModel? Condition { get; set; }

    public static RegistrationFormConditionEditModel From(Condition condition)
    {
        RegistrationFormConditionInputDto input = JsonSerializer.Deserialize<RegistrationFormConditionInputDto>(
            JsonSerializer.Serialize(condition)) ?? new RegistrationFormConditionInputDto { Operator = "exists" };
        return From(input);
    }

    public static RegistrationFormConditionEditModel From(RegistrationFormConditionInputDto input)
    {
        var result = new RegistrationFormConditionEditModel
        {
            Operator = input.Operator,
            FieldReference = Reference(input.FieldNamespace, input.FieldKey),
            Comparison = input.Comparison ?? "equal",
            Value = RegistrationFormScalarEditModel.From(input.Value)
        };
        result.Values.AddRange(input.Values?.Select(RegistrationFormScalarEditModel.From) ?? []);
        result.Conditions.AddRange(input.Conditions?.Select(From) ?? []);
        result.Condition = input.Condition is null ? null : From(input.Condition);
        return result;
    }

    public void SetOperator(string value)
    {
        Operator = value;
        if (value == "compare" && Value.Type is not ("number" or "date"))
        {
            Value.Type = "number";
        }
        if (value is "all" or "any" && Conditions.Count == 0)
        {
            Conditions.Add(new RegistrationFormConditionEditModel());
        }
        else if (value == "not")
        {
            Condition ??= new RegistrationFormConditionEditModel();
        }
        else if (value == "in" && Values.Count == 0)
        {
            Values.Add(new RegistrationFormScalarEditModel());
        }
    }

    public RegistrationFormConditionInputDto ToInput()
    {
        (string? fieldNamespace, string? fieldKey) = Split(FieldReference);
        return new RegistrationFormConditionInputDto
        {
            Operator = Operator,
            FieldNamespace = Operator is "all" or "any" or "not" ? null : fieldNamespace,
            FieldKey = Operator is "all" or "any" or "not" ? null : fieldKey,
            Comparison = Operator == "compare" ? Comparison : null,
            Value = Operator is "equals" or "notEquals" or "contains" or "compare" ? Value.ToInput() : null,
            Values = Operator == "in" ? Values.Select(item => item.ToInput()).ToArray() : null,
            Conditions = Operator is "all" or "any" ? Conditions.Select(item => item.ToInput()).ToArray() : null,
            Condition = Operator == "not" ? Condition?.ToInput() : null
        };
    }

    private static string? Reference(string? fieldNamespace, string? fieldKey) =>
        string.IsNullOrWhiteSpace(fieldNamespace) || string.IsNullOrWhiteSpace(fieldKey) ? null : $"{fieldNamespace}:{fieldKey}";

    private static (string?, string?) Split(string? reference)
    {
        string[] parts = reference?.Split(':', 2) ?? [];
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, null);
    }
}

public sealed class RegistrationFormScalarEditModel
{
    public string Type { get; set; } = "text";
    public string? TextValue { get; set; }
    public bool BooleanValue { get; set; }
    public double? NumberValue { get; set; }
    public DateTime? DateValue { get; set; }

    public static RegistrationFormScalarEditModel From(RegistrationFormScalarValueInputDto? input) => input is null
        ? new RegistrationFormScalarEditModel()
        : new RegistrationFormScalarEditModel
        {
            Type = input.Type,
            TextValue = input.TextValue,
            BooleanValue = input.BooleanValue == true,
            NumberValue = input.NumberValue,
            DateValue = input.DateValue?.UtcDateTime
        };

    public RegistrationFormScalarValueInputDto ToInput() => new()
    {
        Type = Type,
        TextValue = Type == "text" ? TextValue?.Trim() : null,
        BooleanValue = Type == "boolean" ? BooleanValue : null,
        NumberValue = Type == "number" ? NumberValue : null,
        DateValue = Type == "date" && DateValue is not null
            ? new DateTimeOffset(DateTime.SpecifyKind(DateValue.Value, DateTimeKind.Utc))
            : null
    };
}
