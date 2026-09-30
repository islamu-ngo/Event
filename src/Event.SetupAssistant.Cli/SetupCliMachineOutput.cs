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
        throw new InvalidOperationException("machine-output-required");
    }

    internal static byte[] Fallback(SetupCliExitCode exit, string code, int maximumCharacters = 65_536) =>
        Serialize(new SetupCliCommand("doctor", "doctor", true, false, false, null, null, null, null, null, [], [], null),
            SetupCliResults.Failure(exit, code), maximumCharacters);

    private static byte[] Serialize(SetupCliCommand command, SetupCliCommandResult result, int maximumCharacters)
    {
        string family = SetupCliCommandRegistry.Families.Any(item => item.Name == command.Family)
            ? command.Family
            : "doctor";
        string operation = SetupCliCommandRegistry.TryResolve(family, command.Operation, out _)
            ? command.Operation
            : SetupCliCommandRegistry.Families.First(item => item.Name == family).Operations[0].Name;
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
