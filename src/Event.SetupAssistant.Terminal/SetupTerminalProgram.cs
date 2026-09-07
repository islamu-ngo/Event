namespace ISLAMU.Event.SetupAssistant.Terminal;

using CommunityToolkit.Mvvm.Messaging;
using ISLAMU.Event.SetupAssistant.Presentation;
using Microsoft.Extensions.DependencyInjection;

internal static class SetupTerminalProgram
{
    internal static int Run(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments is ["--help"])
        {
            Console.Out.WriteLine("event-setup-terminal");
            return 0;
        }

        if (arguments.Length != 0)
            return Fail(64, "terminal-arguments-rejected");
        if (Console.IsInputRedirected || Console.IsOutputRedirected || Console.IsErrorRedirected)
            return Fail(4, "interactive-terminal-required");

        try
        {
            var services = new ServiceCollection();
            services.AddScoped<IMessenger>(_ => new StrongReferenceMessenger());
            services.AddScoped<SetupPresentationSession>();
            services.AddScoped<SetupTerminalApplication>();
            using ServiceProvider provider = services.BuildServiceProvider(
                new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
            using IServiceScope scope = provider.CreateScope();
            return scope.ServiceProvider.GetRequiredService<SetupTerminalApplication>().Run();
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or NotSupportedException)
        {
            return Fail(70, "terminal-start-failed");
        }
    }

    private static int Fail(int exitCode, string diagnostic)
    {
        try
        {
            Console.Error.WriteLine(diagnostic);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

        return exitCode;
    }
}
