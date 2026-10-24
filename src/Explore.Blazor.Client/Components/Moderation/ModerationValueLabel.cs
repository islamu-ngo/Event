using System.Text;

namespace Explore.Blazor.Client.Components.Moderation;

internal static class ModerationValueLabel
{
    internal static string Format(Enum? value)
    {
        if (value is null) return string.Empty;
        string code = value.ToString();
        var label = new StringBuilder(code.Length);
        for (int index = 0; index < code.Length; index++)
        {
            if (index > 0 && char.IsUpper(code[index]) && char.IsLower(code[index - 1]))
                label.Append(' ');
            label.Append(code[index]);
        }
        return label.ToString();
    }
}
