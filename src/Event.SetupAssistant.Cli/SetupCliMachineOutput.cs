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
        string line = result.Exit == SetupCliExitCode.Success ? "success\n" : $"{SetupCliResults.Lower(result.Exit)}-error $.arguments\n";
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
