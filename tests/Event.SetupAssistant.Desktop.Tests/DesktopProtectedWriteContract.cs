namespace Event.SetupAssistant.Desktop.Tests;

using System.Reflection;

internal sealed class DesktopProtectedWriteContract
{
    private const string ProductNamespace = "ISLAMU.Event.SetupAssistant.Desktop.Files";
    private readonly Assembly _assembly = Assembly.Load("Event.SetupAssistant.Desktop");

    internal object CreateUnixWriter() =>
        Activator.CreateInstance(RequireType("UnixProtectedFileWriter"))
        ?? throw new InvalidOperationException("desktop-unix-writer-construction-failed");

    internal object CreateWindowsWriter() =>
        Activator.CreateInstance(RequireType("WindowsProtectedFileWriter"))
        ?? throw new InvalidOperationException("desktop-windows-writer-construction-failed");

    internal object CreateRequest(
        string targetPath,
        ReadOnlyMemory<byte> bytes,
        bool allowOverwrite) =>
        Activator.CreateInstance(
            RequireType("ProtectedWriteRequest"),
            targetPath,
            bytes,
            allowOverwrite)
        ?? throw new InvalidOperationException("desktop-protected-request-construction-failed");

    internal async Task<ProtectedWritePreparationHandle> PrepareAsync(
        object writer,
        object request)
    {
        MethodInfo method = writer.GetType().GetMethod(
            "PrepareAsync",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                $"missing-approved-owner:{writer.GetType().Name}.PrepareAsync");
        object task = method.Invoke(writer, [request, CancellationToken.None])
            ?? throw new InvalidOperationException("desktop-prepare-returned-null");
        object preparation = await AwaitResultAsync(task);
        return new ProtectedWritePreparationHandle(preparation);
    }

    internal static bool IsAvailable(object writer) =>
        (bool)(writer.GetType().GetProperty("IsAvailable")?.GetValue(writer)
            ?? throw new InvalidOperationException(
                $"missing-approved-owner:{writer.GetType().Name}.IsAvailable"));

    internal static string PropertyName(object instance, string property) =>
        instance.GetType().GetProperty(property)?.GetValue(instance)?.ToString()
        ?? throw new InvalidOperationException(
            $"missing-approved-owner:{instance.GetType().Name}.{property}");

    internal Type ResultType => RequireType("ProtectedWriteResult");

    private Type RequireType(string name) =>
        _assembly.GetType($"{ProductNamespace}.{name}", throwOnError: false)
        ?? throw new InvalidOperationException(
            $"missing-approved-owner:{ProductNamespace}.{name}");

    private static async Task<object> AwaitResultAsync(object taskObject)
    {
        if (taskObject is not Task task)
            throw new InvalidOperationException("desktop-protected-operation-not-task");
        await task;
        return taskObject.GetType().GetProperty("Result")?.GetValue(taskObject)
            ?? throw new InvalidOperationException("desktop-protected-operation-result-null");
    }
}

internal sealed class ProtectedWritePreparationHandle(object preparation) : IDisposable
{
    internal async Task<object> CommitAsync()
    {
        MethodInfo method = preparation.GetType().GetMethod(
            "CommitAsync",
            BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "missing-approved-owner:ProtectedWritePreparation.CommitAsync");
        object taskObject = method.Invoke(preparation, [CancellationToken.None])
            ?? throw new InvalidOperationException("desktop-commit-returned-null");
        if (taskObject is not Task task)
            throw new InvalidOperationException("desktop-commit-not-task");
        await task;
        return taskObject.GetType().GetProperty("Result")?.GetValue(taskObject)
            ?? throw new InvalidOperationException("desktop-commit-result-null");
    }

    public void Dispose()
    {
        if (preparation is IDisposable disposable)
            disposable.Dispose();
    }
}
