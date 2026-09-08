using Explore.Diagnostic.AiEvaluation;
using Explore.Diagnostic.AiReplay;
using Explore.Diagnostic.Doctor;
using Explore.Diagnostic.Doctor.Checks;
using Explore.Diagnostic.Doctor.Infrastructure;

if (args.Length > 0 && string.Equals(args[0], "ai-eval-report", StringComparison.OrdinalIgnoreCase))
{
    var evaluationOptions = AiEvaluationCommandOptions.Parse(args[1..]);
    if (evaluationOptions.ShowHelp)
    {
        AiEvaluationConsoleReporter.WriteHelp(Console.Out);
        return DoctorExitCodes.Success;
    }

    var evaluationRoot = evaluationOptions.RepositoryRoot ?? DoctorRepositoryLocator.LocateRepositoryRoot(Directory.GetCurrentDirectory());
    var outputDirectory = evaluationOptions.OutputDirectory ?? Path.Combine(evaluationRoot, "artifacts", "ai-evaluation");
    var evaluationReport = new AiEvaluationReportGenerator().Generate();
    var evaluationArtifact = AiEvaluationReportWriter.Write(evaluationReport, outputDirectory);

    AiEvaluationConsoleReporter.WriteReport(Console.Out, evaluationReport, evaluationArtifact);
    return DoctorExitCodes.Success;
}

if (args.Length > 0 && string.Equals(args[0], "ai-replay-report", StringComparison.OrdinalIgnoreCase))
{
    var replayOptions = AiReplayCommandOptions.Parse(args[1..]);
    if (replayOptions.ShowHelp)
    {
        AiReplayConsoleReporter.WriteHelp(Console.Out);
        return DoctorExitCodes.Success;
    }

    var replayRoot = replayOptions.RepositoryRoot ?? DoctorRepositoryLocator.LocateRepositoryRoot(Directory.GetCurrentDirectory());
    var outputDirectory = replayOptions.OutputDirectory ?? Path.Combine(replayRoot, "artifacts", "ai-replay");
    var replayReport = new AiReplayReportGenerator().Generate();
    var replayArtifact = AiReplayReportWriter.Write(replayReport, outputDirectory);

    AiReplayConsoleReporter.WriteReport(Console.Out, replayReport, replayArtifact);
    return replayReport.IsCiSafe ? DoctorExitCodes.Success : DoctorExitCodes.HardFailure;
}

var options = DoctorCommandOptions.Parse(args);

if (options.ShowHelp)
{
    DoctorConsoleReporter.WriteHelp(Console.Out);
    return DoctorExitCodes.Success;
}

var repositoryRoot = options.RepositoryRoot ?? DoctorRepositoryLocator.LocateRepositoryRoot(Directory.GetCurrentDirectory());
var fileSystem = new PhysicalDoctorFileSystem();
var processRunner = new DefaultDoctorProcessRunner();

var checks = new IDoctorCheck[]
{
    new GlobalJsonSdkDoctorCheck(fileSystem, processRunner, repositoryRoot),
    new DockerDoctorCheck(processRunner),
    new AspireDoctorCheck(processRunner),
    new DockerComposeTopologyDoctorCheck(fileSystem, repositoryRoot),
    new BootstrapConfigurationDoctorCheck(fileSystem, repositoryRoot),
    new OperationsDocumentationDoctorCheck(fileSystem, repositoryRoot),
    new AiToolReadinessDoctorCheck(fileSystem, repositoryRoot),
    new McpDebugReadinessDoctorCheck(fileSystem, repositoryRoot),
};

var runner = new DoctorRunner(checks);
var report = await runner.RunAsync(options.Timeout, CancellationToken.None);

DoctorConsoleReporter.WriteReport(Console.Out, report);

return DoctorExitCodes.FromReport(report);
