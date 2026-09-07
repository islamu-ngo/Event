using System.Text.Json;

namespace ISLAMU.Event.SetupAssistant.Cli;

public sealed class SetupCliApplication
{
    public SetupCliExitCode Run(SetupCliInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        SetupCliCommand command = SetupCliParser.Parse(invocation);
        SetupCliCommandResult result;
        try
        {
            result = command.Error is not null
                ? SetupCliResults.Failure(SetupCliExitCode.Usage, command.Error)
                : Dispatch(command, invocation);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            result = SetupCliResults.Failure(SetupCliExitCode.Io, "io-failed");
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException)
        {
            result = SetupCliResults.Failure(SetupCliExitCode.Internal, "internal-failed");
        }

        try
        {
            SetupCliMachineOutput.Emit(invocation, command, result);
            return result.Exit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return SetupCliExitCode.Io;
        }
    }

    private SetupCliCommandResult Dispatch(SetupCliCommand command, SetupCliInvocation invocation)
    {
        if (invocation.Environment.Names.Any(SetupCliParser.IsForbidden))
            return SetupCliResults.Failure(SetupCliExitCode.Blocked, "environment-name-blocked");
        if (command.Help) return SetupCliResults.Success();
        if (RequiresOutput(command) && command.Output is null && !command.DryRun)
            return SetupCliResults.Failure(SetupCliExitCode.Usage, "output-required");
        if (RequiresInput(command) && command.Input is null)
            return SetupCliResults.Failure(SetupCliExitCode.Usage, "input-required");

        return command.Family switch
        {
            "catalogue" => SetupCliCatalogueEnvironmentHandlers.Catalogue(command, invocation),
            "manifest" => SetupCliPortabilityHandlers.Portability(command, invocation, tenant: false),
            "tenant-package" => SetupCliPortabilityHandlers.Portability(command, invocation, tenant: true),
            "env" => SetupCliCatalogueEnvironmentHandlers.Environment(command, invocation),
            "legal" => SetupCliPortabilityHandlers.Legal(command, invocation),
            "doctor" => SetupCliPortabilityHandlers.Doctor(),
            _ => SetupCliResults.Failure(SetupCliExitCode.Usage, "command-unknown")
        };
    }

    private static bool RequiresInput(SetupCliCommand command) => command.Operation is
        "open" or "validate" or "format" or "diff" or "coverage" or "export" or "preview";
    private static bool RequiresOutput(SetupCliCommand command) => command.Operation is
        "create" or "format" or "export" or "render" or "list" or "show" or "describe";
}
