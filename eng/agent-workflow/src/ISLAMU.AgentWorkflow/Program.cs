using System.Text;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace ISLAMU.AgentWorkflow;

public static class Program
{
    private const int Invalid = 2;
    private const int Usage = 64;
    private const int MaximumYamlBytes = 4 * 1024 * 1024;

    public static int Main(string[] args)
    {
        return args switch
        {
            ["validate-intents", string path] => ValidateIntents(path),
            ["validate-commit", "--", .. var command] => ValidateCommit(command),
            ["--help"] or ["-h"] or [] => WriteHelp(),
            _ => WriteFailure("invalid_command", Usage),
        };
    }

    private static int ValidateIntents(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length is <= 0 or > MaximumYamlBytes)
            {
                return WriteFailure("invalid_intents_yaml", Invalid);
            }

            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false);
            var yaml = new YamlStream();
            yaml.Load(reader);
            return yaml.Documents.Count == 1
                ? WriteSuccess("intents_yaml_valid")
                : WriteFailure("invalid_intents_yaml", Invalid);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or YamlException or DecoderFallbackException)
        {
            return WriteFailure("invalid_intents_yaml", Invalid);
        }
    }

    private static int ValidateCommit(string[] command)
    {
        int separator = Array.LastIndexOf(command, "--");
        if (command is not ["git", "commit", ..] || separator < 2 || separator == command.Length - 1 ||
            Array.IndexOf(command, "--") != separator ||
            command[2..separator].Any(option => option is "-a" or "--all" or "--pathspec-from-file" or "--pathspec-file-nul" ||
                option.StartsWith("--pathspec-from-file=", StringComparison.Ordinal)))
        {
            return WriteFailure("unsafe_commit_paths", Invalid);
        }

        string[] paths = command[(separator + 1)..];
        return paths.All(IsLiteralFilePath) && paths.Distinct(StringComparer.Ordinal).Count() == paths.Length
            ? WriteSuccess("commit_paths_literal")
            : WriteFailure("unsafe_commit_paths", Invalid);
    }

    private static bool IsLiteralFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "." or ".." || path[0] == ':' ||
            Path.IsPathRooted(path) || path.Contains('\\') || path.EndsWith('/') ||
            path.IndexOfAny(['*', '?', '[', ']', '\0']) >= 0 || path.Any(char.IsControl) || Directory.Exists(path))
        {
            return false;
        }

        return path.Split('/', StringSplitOptions.None)
            .All(segment => segment.Length > 0 && segment is not "." and not "..");
    }

    private static int WriteHelp()
    {
        Console.Out.WriteLine("usage: islamu-agent-workflow validate-intents <intents.yaml> | validate-commit -- git commit <options> -- <literal-file>...");
        return 0;
    }

    private static int WriteSuccess(string code)
    {
        Console.Out.WriteLine(code);
        return 0;
    }

    private static int WriteFailure(string code, int exitCode)
    {
        Console.Error.WriteLine(code);
        return exitCode;
    }
}
