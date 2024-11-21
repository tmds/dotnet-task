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
public class DotnetSdkTaskScriptTestsRedHatNet9 : DotnetSdkTaskScriptTests
{
    protected override string SdkImage => "registry.access.redhat.com/ubi8/dotnet-90";
    protected override string DotnetVersion => "9.0";

    public DotnetSdkTaskScriptTestsRedHatNet9(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}

[Collection(nameof(TektonTaskTestCollection))]
public class DotnetSdkTaskScriptTestsMicrosoftNet9 : DotnetSdkTaskScriptTests
{
    protected override string SdkImage => "mcr.microsoft.com/dotnet/sdk:9.0";
    protected override string DotnetVersion => "9.0";

    public DotnetSdkTaskScriptTestsMicrosoftNet9(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}