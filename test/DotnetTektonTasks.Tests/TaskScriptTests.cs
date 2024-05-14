using SimpleExec;
using YamlDotNet.RepresentationModel;

namespace DotnetTektonTasks.Tests;

public abstract class TaskScriptTests : FileCleanupBase
{
    // SDK image and its version.
    protected abstract string SdkImage { get; }
    protected abstract string DotnetVersion { get; }

    private readonly DotnetTektonTasks _tektonTasks = new();
    private readonly string _taskName;

    protected string Script => _tektonTasks.GetTektonTask(_taskName).Script;
    protected YamlNode Yaml => _tektonTasks.GetTektonTask(_taskName).Yaml;

    protected const string TestCurrentNamespace ="test-namespace";
    protected const string OpenShiftInternalRegistry = "image-registry.openshift-image-registry.svc:5000";

    protected TaskScriptTests(DotnetTektonTasks tektonTasks, string taskName)
    {
        _tektonTasks = tektonTasks;
        _taskName = taskName;
    }

    [Fact]
    public void SuccessOnScriptSuccess()
    {
        var runResult = RunScript("exit 0");
        Assert.Empty(runResult.StandardError);
        Assert.Empty(runResult.StandardOutput);
        Assert.Equal(0, runResult.ExitCode);
    }

    [Fact]
    public void FailOnScriptFail()
    {
        var runResult = RunScript("exit 1");
        Assert.Empty(runResult.StandardError);
        Assert.Empty(runResult.StandardOutput);
        Assert.Equal(1, runResult.ExitCode);
    }

    [MemberData(nameof(ParamEnvvarsData))]
    [Theory]
    public void ParamEnvvars(string[] envvars)
    {
        var runResult = RunScript("env", args: ["--env-vars", ..envvars]);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        string[] lines = runResult.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var envvar in envvars)
        {
            Assert.Contains(envvar, lines);
        }
    }

    public static IEnumerable<object[]> ParamEnvvarsData =>
        new string[][]
        {
            [ "ENV1=VAL1" ],
            [ "ENV1=VAL1", "ENV2=VAL2"],
            [ "ENV1=VAL 1", "ENV2=VAL 2"]
        }.Select(envvars => new object[] { envvars });

    [Fact]
    public void WorkingDirectorySourceWorkspaceBound()
    {
        string homeDirectory = CreateDirectory();
        string sourcePath = CreateDirectory();
        var runResult = RunScript("pwd",
            envvars: new()
            {
                { "WORKSPACE_SOURCE_BOUND", "true"},
                { "WORKSPACE_SOURCE_PATH", sourcePath}
            });
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.Equal(sourcePath, runResult.StandardOutput.Trim());
    }

    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Theory]
    public void DockerConfigBound(bool addConfigJson, bool addDockerConfigJson)
    {
        string homeDirectory = CreateDirectory();
        string dockerConfigPath = CreateDirectory();
        if (addConfigJson)
        {
            File.WriteAllText(Path.Combine(dockerConfigPath, "config.json"), "");
        }
        if (addDockerConfigJson)
        {
            File.WriteAllText(Path.Combine(dockerConfigPath, ".dockerconfigjson"), "");
        }
        var runResult = RunScript("exit 0",
            envvars: new()
            {
                { "WORKSPACE_DOCKERCONFIG_BOUND", "true"},
                { "WORKSPACE_DOCKERCONFIG_PATH", dockerConfigPath}
            },
            homeDirectory: homeDirectory);
        if (addConfigJson && addDockerConfigJson)
        {
            Assert.Equal(1, runResult.ExitCode);
            Assert.Equal("error: 'dockerconfig' workspace provides multiple config files.", runResult.StandardError.Trim());
            Assert.Empty(runResult.StandardOutput);
        }
        else if (!addConfigJson && !addDockerConfigJson)
        {
            Assert.Empty(runResult.StandardError);
            Assert.Empty(runResult.StandardOutput);
            Assert.Equal(0, runResult.ExitCode);
        }
        else
        {
            string containersAuthConfigPath = Path.Combine(homeDirectory, ".config/containers/auth.json");
            FileInfo fi = new FileInfo(containersAuthConfigPath);
            
            Assert.True(fi.Exists);
            Assert.True((fi.Attributes & FileAttributes.ReparsePoint) != 0); // Check the file is a link.
            Assert.Equal(addConfigJson ? $"{dockerConfigPath}/config.json" : $"{dockerConfigPath}/.dockerconfigjson", fi.LinkTarget);

            Assert.Empty(runResult.StandardError);
            Assert.Equal(0, runResult.ExitCode);
        }
    }

    [Fact]
    public void WorkingDirectoryWorkspaceNotBound()
    {
        string homeDirectory = CreateDirectory();
        string sourcePath = CreateDirectory();
        var runResult = RunScript("pwd", homeDirectory: homeDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.Equal($"{homeDirectory}/src", runResult.StandardOutput.Trim());
    }

    protected abstract
        (string StandardOutput, string StandardError, int ExitCode)
        RunScript(string script, Dictionary<string, string?>? envvars = null, IEnumerable<string>? args = null, string? homeDirectory = null, string? tektonResultsDirectory = null);

    protected
        (string StandardOutput, string StandardError, int ExitCode)
        RunTask(Dictionary<string, string?> envvars, IEnumerable<string>? args = null, string? homeDirectory = null, string? tektonResultsDirectory = null, string? dotnetStubScript = null)
    {
        // Throw an exception on Windows so compiler doesn't emit warnings for the UnixFileMode usage.
        if (OperatingSystem.IsWindows())
        {
            throw new NotSupportedException("Running these tests on Windows is not supported");
        }

        string scriptFilePath = GenerateTempFilePath("script.sh");
        File.WriteAllText(scriptFilePath, Script);
        new FileInfo(scriptFilePath).UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        dotnetStubScript ??=
            """
            echo "No dotnet stub" >&2
            exit 1
            """;
        string dotnetStubFilePath = GenerateTempFilePath("dotnet");
        File.WriteAllText(dotnetStubFilePath, dotnetStubScript);
        new FileInfo(dotnetStubFilePath).UnixFileMode |= UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

        homeDirectory ??= CreateDirectory();
        new DirectoryInfo(homeDirectory).UnixFileMode |= UnixFileMode.GroupWrite;

        tektonResultsDirectory ??= CreateDirectory();
        new DirectoryInfo(tektonResultsDirectory).UnixFileMode |= UnixFileMode.GroupWrite;

        List<string> envvarArgs = new();

        // Add all envvars that are defined by the task to avoid unbound variable errors from bash.
        YamlSequenceNode taskEnvvars = (Yaml["spec"]["steps"][0]["env"] as YamlSequenceNode)!;
        foreach (var envvar in taskEnvvars)
        {
            envvarArgs.Add("-e");
            string envvarName = (string)envvar["name"]!;
            switch (envvarName)
            {
                case "OpenShiftInternalRegistry":
                    envvarArgs.Add($"{envvarName}={OpenShiftInternalRegistry}");
                    break;
                case "OpenShiftCurrentNamespace":
                    envvarArgs.Add($"{envvarName}={TestCurrentNamespace}");
                    break;
                default:
                    envvarArgs.Add($"{envvarName}=");
                    break;
            }
        }

        foreach (var envvar in envvars)
        {
            if (envvar.Value is not null)
            {
                envvarArgs.Add("-e");
                envvarArgs.Add($"{envvar.Key}={envvar.Value}");

                // Mount workspaces at the same location in the container.
                if (envvar.Key.StartsWith("WORKSPACE_") && envvar.Key.EndsWith("_PATH"))
                {
                    envvarArgs.Add($"-v");
                    envvarArgs.Add($"{envvar.Value}:{envvar.Value}:z");
                }
            }
        }

        args ??= [];
        List<string> podmanArgs =
        [
            "run",
            "-q",
            "--rm",
            ..envvarArgs,
            "-e", $"HOME={homeDirectory}", "-v", $"{homeDirectory}:/{homeDirectory}/:z",
            "-v", $"{tektonResultsDirectory}:/tekton/results:z",
            "-v", $"{Path.GetDirectoryName(scriptFilePath)}:/task-script/:z",
            "-v", $"{Path.GetDirectoryName(dotnetStubFilePath)}:/dotnet-stub/:z",
            "-e", "PATH=/dotnet-stub:/usr/bin:/bin",
            SdkImage,
            "/task-script/script.sh",
            "--",
            ..args
        ];

        int exitCode = 255;
        var readTask = Command.ReadAsync(
            "podman",
            podmanArgs,
            handleExitCode: (val) => { exitCode = val; return true; });
        var readResult = readTask.GetAwaiter().GetResult();

        return (readResult.StandardOutput, readResult.StandardError, exitCode);
    }
}