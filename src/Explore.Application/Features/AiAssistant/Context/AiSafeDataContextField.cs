namespace Explore.Application.Features.AiAssistant.Context;

public sealed class AiSafeDataContextField
{
    public AiSafeDataContextField(string name, string description, bool isCitationField = false)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("AI data context field name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("AI data context field description is required.", nameof(description));
        }

        Name = name.Trim();
        Description = description.Trim();
        IsCitationField = isCitationField;
    }

    public string Name { get; }

    public string Description { get; }

    public bool IsCitationField { get; }
}
