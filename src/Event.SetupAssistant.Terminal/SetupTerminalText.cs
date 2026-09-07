namespace ISLAMU.Event.SetupAssistant.Terminal;

using System.Globalization;
using System.Resources;

internal static class SetupTerminalText
{
    private static readonly ResourceManager Resources = new(
        "Event.SetupAssistant.Terminal.Resources.SetupTerminalText",
        typeof(SetupTerminalText).Assembly);

    internal static string Get(string key) =>
        Resources.GetString(key, CultureInfo.CurrentUICulture) ?? key;

    internal static string FormatResult(SetupTerminalArtifactResult result)
    {
        string readiness = Get("Readiness" + result.Readiness);
        string digest = result.Digest is null
            ? string.Empty
            : Format(Get("DigestSuffix"), result.Digest);
        string key = result switch
        {
            { Written: true, Readiness: ISLAMU.Event.Setup.Core.Environment.DotenvReadinessState.Ready } => "OutcomeComplete",
            { Written: true } => "OutcomeIncomplete",
            _ => "OutcomeFailed"
        };
        return Format(Get(key), readiness, digest, result.DiagnosticCode);
    }

    private static string Format(string template, params string[] values)
    {
        string result = template;
        for (int index = 0; index < values.Length; index++)
            result = result.Replace($"{{{index}}}", values[index], StringComparison.Ordinal);
        return result;
    }
}
