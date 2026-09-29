using System.Text;
using System.Text.Json;

namespace ISLAMU.Event.SetupAssistant.Cli;

internal static class SetupCliMachineOutput
{
    internal static void Emit(SetupCliInvocation invocation, SetupCliCommand command, SetupCliCommandResult result)
    {
        if (command.Machine)
        {
            invocation.Io.Output.Write("-", Serialize(command, result, invocation.Io.MaximumCharacters), invocation.Io.MaximumCharacters);
            return;
        }
        if (command.Help && result.Exit == SetupCliExitCode.Success)
        {
            string usage = "Usage: event-setup <command> [operation] [options]\n\nCommands:\n"
                + string.Join('\n', SetupCliParser.Operations.Select(entry => entry.Key == "doctor"
                    ? "  doctor"
                    : $"  {entry.Key} <{string.Join('|', entry.Value)}>"))
                + "\n\nExample: event-setup doctor --machine\n";
            invocation.Io.Output.Write("-", Encoding.UTF8.GetBytes(usage), invocation.Io.MaximumCharacters);
            return;
        }
        string? guidance = result.Exit == SetupCliExitCode.Usage
            ? (command.Family, command.Operation, result.Diagnostics.FirstOrDefault()?.Code) switch
            {
                ("catalogue", "list", "output-required") =>
                    "usage-error output-required: pass --output <file|->.\nTry: event-setup catalogue list --output -\n",
                ("catalogue", "show", "key-required") =>
                    "usage-error key-required: pass --key <CATALOGUE_KEY> and --output <file|->.\nTry: event-setup catalogue show --key API_HTTP_PORT --output -\n",
                ("manifest", "create", "output-required") =>
                    "usage-error output-required: pass --output <file|->.\nTry: event-setup manifest create --output instance-manifest.json\n",
                _ => null
            }
            : null;
        string line = result.Exit == SetupCliExitCode.Success ? "success\n"
            : guidance ?? $"{SetupCliResults.Lower(result.Exit)}-error $.arguments\n";
        byte[] text = Encoding.UTF8.GetBytes(line);
        ISetupCliWriter writer = command.Output == "-" && result.Exit == SetupCliExitCode.Success
            ? invocation.Io.Error : invocation.Io.Output;
        writer.Write("-", text, invocation.Io.MaximumCharacters);
    }

    internal static byte[] Fallback(SetupCliExitCode exit, string code, int maximumCharacters = 65_536) =>
        Serialize(new SetupCliCommand("doctor", "doctor", true, false, false, null, null, null, null, null, [], [], null),
            SetupCliResults.Failure(exit, code), maximumCharacters);

    private static byte[] Serialize(SetupCliCommand command, SetupCliCommandResult result, int maximumCharacters)
    {
        string family = SetupCliParser.Operations.ContainsKey(command.Family) ? command.Family : "doctor";
        string operation = SetupCliParser.Operations.TryGetValue(family, out string[]? operations)
            && operations.Contains(command.Operation, StringComparer.Ordinal) ? command.Operation : family;
        string category = SetupCliResults.Lower(result.Exit);
        var envelope = new SetupCliMachineEnvelope("event-setup-command/v1",
            new SetupCliMachineInvocation(family, operation, "machine"), category, category, (int)result.Exit,
            command.DryRun, result.Diagnostics, result.Artifacts, result.Coverage, result.Readiness);
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(envelope, SetupCliJsonContext.Default.SetupCliMachineEnvelope);
        if (body.Length + 1 > maximumCharacters) throw new IOException("output-bound");
        byte[] framed = new byte[body.Length + 1];
        body.CopyTo(framed, 0);
        framed[^1] = (byte)'\n';
        return framed;
    }
}
