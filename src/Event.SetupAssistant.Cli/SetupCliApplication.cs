using System.Text.Json;
using System.Text;
using ISLAMU.Event.SetupAssistant.Cli.Commands;
using Spectre.Console;

namespace ISLAMU.Event.SetupAssistant.Cli;

public sealed class SetupCliApplication
{
    public SetupCliExitCode Run(SetupCliInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        SetupCliPreflight preflight = SetupCliArgumentPreflight.Inspect(invocation);
        using var text = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(text)
        });

        if (preflight.Error is not null)
            return Emit(invocation, preflight.Command, SetupCliResults.Failure(SetupCliExitCode.Usage, preflight.Error), console, text);
        if (invocation.Environment.Names.Any(SetupCliArgumentPreflight.IsForbiddenName))
            return Emit(invocation, preflight.Command,
                SetupCliResults.Failure(SetupCliExitCode.Blocked, "environment-name-blocked"), console, text);
        if (preflight.Command.Help && preflight.Command.Machine)
            return Emit(invocation, preflight.Command, SetupCliResults.Success(), console, text);

        try
        {
            var runtime = new SetupCliCommandRuntime(this, invocation, console);
            int exit = SetupCliCommandRegistry.Create(runtime, console).Run(preflight.Arguments);
            FlushHuman(invocation, runtime.Command ?? preflight.Command, text);
            return runtime.Exit ?? (SetupCliExitCode)exit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Emit(invocation, preflight.Command, SetupCliResults.Failure(SetupCliExitCode.Io, "io-failed"), console, text);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return Emit(invocation, preflight.Command, SetupCliResults.Failure(SetupCliExitCode.Internal, "internal-failed"), console, text);
        }
    }

    internal SetupCliCommandResult Execute(SetupCliCommand command, SetupCliInvocation invocation)
    {
        try
        {
            return Dispatch(command, invocation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SetupCliResults.Failure(SetupCliExitCode.Io, "io-failed");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            return SetupCliResults.Failure(SetupCliExitCode.Internal, "internal-failed");
        }
    }

    private SetupCliCommandResult Dispatch(SetupCliCommand command, SetupCliInvocation invocation)
    {
        return command.Family switch
        {
            "catalogue" => SetupCliCatalogueEnvironmentHandlers.Catalogue(command, invocation),
            "manifest" => SetupCliPortabilityHandlers.Portability(command, invocation, tenant: false),
            "tenant-package" => SetupCliPortabilityHandlers.Portability(command, invocation, tenant: true),
            "portability" => SetupCliPortabilityHandlers.OperatorIdentity(command, invocation),
            "env" => SetupCliCatalogueEnvironmentHandlers.Environment(command, invocation),
            "legal" => SetupCliPortabilityHandlers.Legal(command, invocation),
            "doctor" => SetupCliPortabilityHandlers.Doctor(),
            _ => SetupCliResults.Failure(SetupCliExitCode.Usage, "command-unknown")
        };
    }

    private static SetupCliExitCode Emit(
        SetupCliInvocation invocation,
        SetupCliCommand command,
        SetupCliCommandResult result,
        IAnsiConsole console,
        StringWriter text)
    {
        if (command.Machine)
            SetupCliMachineOutput.Emit(invocation, command, result);
        else
            SetupCliHumanOutput.Render(console, result);
        FlushHuman(invocation, command, text);
        return result.Exit;
    }

    private static void FlushHuman(SetupCliInvocation invocation, SetupCliCommand command, StringWriter text)
    {
        if (command.Machine || text.GetStringBuilder().Length == 0)
            return;
        byte[] bytes = Encoding.UTF8.GetBytes(text.ToString());
        ISetupCliWriter writer = command.Output == "-" ? invocation.Io.Error : invocation.Io.Output;
        writer.Write("-", bytes, invocation.Io.MaximumCharacters);
    }
}

internal sealed class SetupCliCommandRuntime(
    SetupCliApplication application,
    SetupCliInvocation invocation,
    IAnsiConsole console)
{
    internal SetupCliExitCode? Exit { get; private set; }
    internal SetupCliCommand? Command { get; private set; }

    internal int Execute(string family, string operation, ISetupCliCommandSettings settings)
    {
        Command = settings.Bind(family, operation, invocation.Mode);
        SetupCliCommandResult result = application.Execute(Command, invocation);
        if (Command.Machine)
            SetupCliMachineOutput.Emit(invocation, Command, result);
        else
            SetupCliHumanOutput.Render(console, result);
        Exit = result.Exit;
        return (int)result.Exit;
    }
}
