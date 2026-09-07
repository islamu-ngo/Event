namespace ISLAMU.Event.SetupAssistant.Presentation;

public readonly record struct SetupWorkspaceId
{
    private SetupWorkspaceId(string value) => Value = value;

    public static int MaxLength => 128;

    public string Value { get; }

    public static bool TryCreate(string? value, out SetupWorkspaceId identifier)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            identifier = default;
            return false;
        }

        identifier = new SetupWorkspaceId(value);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
