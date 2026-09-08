namespace Explore.Blazor.Client.Components.Registration.FormRenderer;

public sealed class RegistrationFormAnswerState
{
    private readonly Dictionary<Guid, object?> _answers = [];
    private readonly Dictionary<Guid, IReadOnlyList<string>> _issues = [];

    public IReadOnlyDictionary<Guid, object?> Answers => _answers;

    public object? Get(Guid fieldId) => _answers.GetValueOrDefault(fieldId);

    public void Set(Guid fieldId, object? value)
    {
        if (RegistrationFormValue.IsAnswered(value)) _answers[fieldId] = value;
        else _answers.Remove(fieldId);
        _issues.Remove(fieldId);
    }

    public void Clear(Guid fieldId)
    {
        _answers.Remove(fieldId);
        _issues.Remove(fieldId);
    }

    public void SetIssues(IEnumerable<KeyValuePair<Guid, IReadOnlyList<string>>> issues)
    {
        _issues.Clear();
        foreach ((Guid fieldId, IReadOnlyList<string> messages) in issues)
            _issues[fieldId] = messages;
    }

    public IReadOnlyList<string> Issues(Guid fieldId) => _issues.GetValueOrDefault(fieldId) ?? [];
}

internal static class RegistrationFormValue
{
    public static bool IsAnswered(object? value) => value switch
    {
        null => false,
        string text => !string.IsNullOrWhiteSpace(text),
        IEnumerable<string> values => values.Any(),
        _ => true
    };
}
