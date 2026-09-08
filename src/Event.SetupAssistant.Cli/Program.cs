using ISLAMU.Event.SetupAssistant.Cli;

return SetupCliProgram.Run(args);

internal static class SetupCliProgram
{
    internal static int Run(string[] args)
    {
        try
        {
            SetupCliMode mode = args.Contains("--machine", StringComparer.Ordinal) ? SetupCliMode.Machine : SetupCliMode.Text;
            using Stream standardOutput = Console.OpenStandardOutput();
            using Stream standardError = Console.OpenStandardError();
            var writer = new SystemWriter(standardOutput);
            var error = new SystemWriter(standardError);
            var io = new SetupCliIo(new SystemInput(), writer, error, 65_536, 4 * 1024 * 1024);
            var invocation = new SetupCliInvocation(args, mode, io, new SetupCliEnvironmentPresence([]));
            return (int)new SetupCliApplication().Run(invocation);
        }
        catch (ArgumentException)
        {
            return WriteFallback(args, SetupCliExitCode.Usage, "argument-shape-invalid");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return WriteFallback(args, SetupCliExitCode.Io, "io-failed");
        }
        catch (Exception)
        {
            return WriteFallback(args, SetupCliExitCode.Internal, "internal-failed");
        }
    }

    private static int WriteFallback(string[] args, SetupCliExitCode exit, string code)
    {
        bool machine = args.Contains("--machine", StringComparer.Ordinal);
        try
        {
            using Stream stream = machine ? Console.OpenStandardOutput() : Console.OpenStandardError();
            byte[] bytes = machine ? SetupCliMachineOutput.Fallback(exit, code)
                : System.Text.Encoding.UTF8.GetBytes($"{exit.ToString().ToLowerInvariant()}-error $.arguments\n");
            stream.Write(bytes);
            stream.Flush();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        return (int)exit;
    }

    private sealed class SystemInput : ISetupCliInput
    {
        public ReadOnlyMemory<byte> Read(string path, int maximumBytes)
        {
            using Stream stream = path == "-"
                ? Console.OpenStandardInput()
                : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int total = 0;
            while (true)
            {
                int read = stream.Read(chunk, 0, Math.Min(chunk.Length, maximumBytes + 1 - total));
                if (read == 0) break;
                total += read;
                if (total > maximumBytes) throw new IOException("input-bound");
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }

    private sealed class SystemWriter(Stream standardOutput) : ISetupCliWriter
    {
        public void Write(string path, ReadOnlyMemory<byte> bytes, int maximumBytes)
        {
            if (bytes.Length > maximumBytes) throw new IOException("output-bound");
            if (path == "-")
            {
                standardOutput.Write(bytes.Span);
                standardOutput.Flush();
                return;
            }
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            file.Write(bytes.Span);
            file.Flush(flushToDisk: true);
        }
    }
}
