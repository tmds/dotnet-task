using SimpleExec;

namespace DotnetTektonTasks.Tests;

public abstract class DotnetPublishImageTaskScriptTests : TaskScriptTests
{
    // Paths used by the script implementation.
    private const string ImageDigestPath = "/tmp/IMAGE_DIGEST";
    private const string UseDotnetImageStreamBaseImagesPath = "/tmp/UseDotnetImageStreamBaseImages.targets";

    private const string WriteImageDigest = $"echo 'sha256:deadbeef' >{ImageDigestPath}";

    public DotnetPublishImageTaskScriptTests(DotnetTektonTasks tektonTasks)
      : base(tektonTasks, DotnetTektonTasks.DotnetPublishImageTaskName)
    { }

    [InlineData("sha256:82xyza4f", "quay.io", "username/image-name", "latest")]
    [InlineData("sha256:82xyza4f", "quay.io", "username/image-name", null)]
    [Theory]
    public void Results(string sha, string registry, string repo, string? tag)
    {
        string resultsDirectory = CreateDirectory();
        var runResult = RunTask(
            envvars: new()
            {
                { "PARAM_IMAGE_NAME", $"{registry}/{repo}{(tag?.Length > 0 ? ':' : "")}{tag}"}
            },
            dotnetStubScript:
                $"""
                    echo '{sha}' >{ImageDigestPath}
                    exit 0
                """,
                tektonResultsDirectory: resultsDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);
        Assert.Empty(runResult.StandardOutput);

        string imageDigestPath = $"{resultsDirectory}/IMAGE_DIGEST";
        Assert.True(File.Exists(imageDigestPath));
        Assert.Equal(sha, File.ReadAllText(imageDigestPath));

        string imagePath = $"{resultsDirectory}/IMAGE";
        Assert.True(File.Exists(imagePath));
        Assert.Equal($"{registry}/{repo}@{sha}", File.ReadAllText(imagePath));

        Assert.Equal(2, Directory.GetFileSystemEntries(resultsDirectory).Length);
    }

    [MemberData(nameof(ParamBuildPropsData))]
    [Theory]
    public void ParamBuildProps(string[] buildprops)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(args: ["--build-props", ..buildprops]);

        string[] expectedStartArgs = [
            "publish",
            ..buildprops.Select(p => $"-p:{p}"),
            "--getProperty:GeneratedContainerDigest"
        ];

        Assert.Equal(expectedStartArgs, publishCommandArgs.Take(expectedStartArgs.Length));
    }

    public static IEnumerable<object[]> ParamBuildPropsData =>
        new string[][]
        {
            [ "Prop1=Value1" ],
            [ "Prop1=Value1", "Prop2=Value2"],
            [ "Prop1=Value 1", "Prop2=Value 2"],
            [ "Prop1=\"Value 1;\""]
        }.Select(envvars => new object[] { envvars });

    [InlineData("Prop1=Value1;Value2")]
    [Theory]
    public void ParamBuildPropsDoesNotAcceptSemicolonWithoutQuotes(string prop)
    {
        var runResult = RunTask(envvars: [], args: ["--build-props", prop]);
        Assert.Equal(1, runResult.ExitCode);
        string expected =
        $"""
        error: Invalid BUILD_PROPS property: '{prop}'.
        To assign a list of values, the values must be enclosed with double quotes. For example: MyProperty="Value1;Value2".

        """;
        Assert.Equal(expected, runResult.StandardError);
    }

    [MemberData(nameof(ParamImageNameData))]
    [Theory]
    public void ParamImageName(string imageName, string[] expectedProperties)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_IMAGE_NAME", imageName}
            }
        );

        Assert.Superset(expectedProperties.ToHashSet(StringComparer.Ordinal), publishCommandArgs.ToHashSet(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("minimal")]
    [InlineData("some value")]
    public void ParamVerbosity(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_VERBOSITY", value}
            });

        Assert.Single(publishCommandArgs.Where(arg => arg == "-v"));

        int indexOfVerbosityValue = Array.IndexOf(publishCommandArgs, "-v") + 1;
        Assert.Equal(value, publishCommandArgs[indexOfVerbosityValue]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("project.csproj")]
    [InlineData("src/path to/web.fsproj")]
    public void ParamProject(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_PROJECT", value}
            });

        Assert.Equal(value, publishCommandArgs[^1]);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("")]
    [InlineData("other")]
    public void ParamUseDotnetImageStreamBaseImages(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_USE_DOTNET_IMAGESTREAM_BASE_IMAGES", value}
            });
        
        bool expectCustomBeforeDirectoryBuildProps = value == "true";

        Assert.Equal(expectCustomBeforeDirectoryBuildProps, publishCommandArgs.Contains($"-p:CustomBeforeDirectoryBuildProps={UseDotnetImageStreamBaseImagesPath}"));
    }

    // [Fact] // depends on SDK support for getResultOutputFile
    public void UseDotnetImageStreamBaseImagesSetsContainerBaseImage()
    {
        string dotnetNamespace = "dotnet-namespace";

        // Run a script that writes the ContainerBaseImage to a file.
        string homeDirectory = CreateDirectory();
        string baseImageFile = $"{homeDirectory}/base_image";
        var runResult = RunTask(
            new()
            {
                { "PARAM_USE_DOTNET_IMAGESTREAM_BASE_IMAGES", "true"},
                { "OpenShiftDotnetNamespace", dotnetNamespace}
            },
            dotnetStubScript:
            $"""
            #!/bin/sh
            set -e
            alias dotnet="/usr/bin/dotnet"
            dotnet new web -o /tmp/web
            dotnet publish /t:ComputeContainerBaseImage -p:CustomBeforeDirectoryBuildProps={UseDotnetImageStreamBaseImagesPath} --getProperty:ContainerBaseImage /tmp/web --getResultOutputFile:{baseImageFile}
            {WriteImageDigest}
            """,
            homeDirectory: homeDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.True(File.Exists(baseImageFile));
        Assert.Equal($"{OpenShiftInternalRegistry}/{dotnetNamespace}/dotnet-runtime:{DotnetVersion}", File.ReadAllText(baseImageFile).Trim());
    }

    public static IEnumerable<object[]> ParamImageNameData
    {
        get
        {
            foreach (var tag in new[] { "", "latest", "tag1", "\"tag1;tag2\""})
            {
                string tagSuffix = tag.Length > 0 ? $":{tag}" : "";
                string expectedTags = tag.Length > 0 ? tag : "latest";

                // no name specified.
                yield return new object[] { $"{tagSuffix}", new string[] { $"-p:ContainerRegistry={OpenShiftInternalRegistry}", $"-p:ContainerRepository={TestCurrentNamespace}/", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string name = "image-name";
                yield return new object[] { $"{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={OpenShiftInternalRegistry}", $"-p:ContainerRepository={TestCurrentNamespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string @namespace = "other-namespace";
                yield return new object[] { $"{@namespace}/{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={OpenShiftInternalRegistry}", $"-p:ContainerRepository={@namespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string registry = "my-registry.com";
                yield return new object[] { $"{registry}/{@namespace}/{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={registry}", $"-p:ContainerRepository={@namespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };
            }
        }
    }

    [Fact]
    public void PublishCommandArgsMinimalParams()
    {
        string[] publishCommandArgs = GetPublishCommandArgs();
        string[] expected =
        [
            "publish",
            "--getProperty:GeneratedContainerDigest", $"--getResultOutputFile:{ImageDigestPath}",
            "-v", "",
            $"-p:ContainerRegistry={OpenShiftInternalRegistry}", $"-p:ContainerRepository={TestCurrentNamespace}/", "-p:ContainerImageTag=", "-p:ContainerImageTags=latest",
            "/t:PublishContainer",
            ""
        ];
        Assert.Equal(expected, publishCommandArgs);
    }

    private string[] GetPublishCommandArgs(
        Dictionary<string, string?>? envvars = null,
        IEnumerable<string>? args = null
    )
    {
        var runResult = RunScript(
            $$"""
            set -e
            {{WriteImageDigest}}
            ARGS=( "$@" )
            printf '%s\n' "${ARGS[@]}"
            """,
            envvars, args);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        // Trim the last newline from stdout.
        string stdout = runResult.StandardOutput;
        Assert.Equal('\n', stdout[^1]);
        stdout = stdout[0..^1];

        return stdout.Split('\n');
    }

    protected override
        (string StandardOutput, string StandardError, int ExitCode)
        RunScript(
            string script,
            Dictionary<string, string?>? envvars = null,
            IEnumerable<string>? args = null,
            string? homeDirectory = null,
            string? tektonResultsDirectory = null)
    {
        string dotnetStubScript =
            $"""
            {WriteImageDigest}
            {script}
            """;
        return RunTask(envvars ?? new(), args, homeDirectory, tektonResultsDirectory, dotnetStubScript);
    }
}

[Collection(nameof(TektonTaskTestCollection))]
public class DotnetPublishImageTaskScriptTestsNet8 : DotnetPublishImageTaskScriptTests
{
    protected override string SdkImage => "registry.access.redhat.com/ubi8/dotnet-80:latest";
    protected override string DotnetVersion => "8.0";

    public DotnetPublishImageTaskScriptTestsNet8(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}