using Explore.Blazor.Client.Clients;

namespace Explore.Blazor.Client.Pages.Studio.RegistrationForms;

public sealed class RegistrationFormRuleEditModel
{
    public RegistrationFormRuleDto? Source { get; private init; }
    public int Ordinal { get; set; }
    public string? TargetReference { get; set; }
    public int Effect { get; set; } = 1;
    public RegistrationFormConditionEditModel Condition { get; set; } = new();

    public bool IsValid => Ordinal > 0 && Split(TargetReference) is not null;

    public static RegistrationFormRuleEditModel Create(int ordinal) => new() { Ordinal = ordinal };

    public static RegistrationFormRuleEditModel From(RegistrationFormRuleDto rule) => new()
    {
        Source = rule,
        Ordinal = rule.Ordinal,
        TargetReference = $"{rule.TargetNamespace}:{rule.TargetKey}",
        Effect = rule.Effect,
        Condition = RegistrationFormConditionEditModel.From(rule.Condition)
    };

    public RegistrationFormRuleInput ToInput()
    {
        (string Namespace, string Key) target = Split(TargetReference)
            ?? throw new InvalidOperationException("A rule target is required.");
        return new RegistrationFormRuleInput
        {
            Ordinal = Ordinal,
            TargetNamespace = target.Namespace,
            TargetKey = target.Key,
            Effect = Effect,
            Condition = Condition.ToInput()
        };
    }

    private static (string Namespace, string Key)? Split(string? reference)
    {
        string[] parts = reference?.Split(':', 2) ?? [];
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part)) ? (parts[0], parts[1]) : null;
    }
}
