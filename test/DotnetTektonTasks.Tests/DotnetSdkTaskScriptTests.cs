namespace DotnetTektonTasks.Tests;

public abstract class DotnetSdkTaskScriptTests : TaskScriptTests
{
    public DotnetSdkTaskScriptTests(DotnetTektonTasks tektonTasks)
      : base(tektonTasks, DotnetTektonTasks.DotnetSdkImageTaskName)
    { }

    protected override
        (string StandardOutput, string StandardError, int ExitCode)
        RunScript(
            string script,
            Dictionary<string, string?>? envvars,
            IEnumerable<string>? args = null,
            string? homeDirectory = null,
            string? tektonResultsDirectory = null)
    {
        envvars ??= new();
        envvars["PARAM_SCRIPT"] = script;
        return RunTask(envvars, args, homeDirectory, tektonResultsDirectory);
    }
}

[Collection(nameof(TektonTaskTestCollection))]
public class DotnetSdkTaskScriptTestsNet8 : DotnetSdkTaskScriptTests
{
    protected override string SdkImage => "registry.access.redhat.com/ubi8/dotnet-80:latest";
    protected override string DotnetVersion => "8.0";

    public DotnetSdkTaskScriptTestsNet8(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}