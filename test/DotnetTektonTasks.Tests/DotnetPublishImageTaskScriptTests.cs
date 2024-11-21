using Newtonsoft.Json.Linq;
using SimpleExec;

namespace DotnetTektonTasks.Tests;

public abstract class DotnetPublishImageTaskScriptTests : TaskScriptTests
{
    // Paths used by the script implementation.
    private const string PrintVersion = """if [ "$1" == "--version" ]; then echo "9.0" && exit 0; fi""";
    private const string ImageDigestPath = "/tmp/IMAGE_DIGEST";
    private const string OverrideBaseImageTargetsPath = "/tmp/OverrideBaseImage.targets";

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
                    {PrintVersion}
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
    [InlineData("base-image")]
    [InlineData("")]
    public void ParamOverrideBaseImage(string value)
    {
        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_BASE_IMAGE", value}
            });
        
        bool expectCustomBeforeDirectoryBuildProps = !string.IsNullOrEmpty(value);

        Assert.Equal(expectCustomBeforeDirectoryBuildProps, publishCommandArgs.Contains($"-p:CustomBeforeDirectoryBuildProps={OverrideBaseImageTargetsPath}"));

        // ContainerFamily gets passed when a base image is specified.
        string? containerFamilyProp = publishCommandArgs.LastOrDefault(p => p.StartsWith("-p:ContainerFamily="))?.Substring("-p:ContainerFamily=".Length);
        string expectedContainerFamily = string.IsNullOrEmpty(value) ? null : "";
        Assert.Equal(expectedContainerFamily, containerFamilyProp);
    }

    [Theory]
    [InlineData("runtime-repo", $"{TestImageRegistry}/runtime-repo", null)]
    [InlineData("ns/runtime-repo", $"{TestImageRegistry}/ns/runtime-repo", null)]
    [InlineData("server.io/ns/runtime-repo", $"server.io/ns/runtime-repo", null)]
    [InlineData("runtime-repo:tag1", $"{TestImageRegistry}/runtime-repo:tag1", null)]
    [InlineData("ns/runtime-repo:tag1", $"{TestImageRegistry}/ns/runtime-repo:tag1", null)]
    [InlineData("server.io/ns/runtime-repo:tag1", $"server.io/ns/runtime-repo:tag1", null)]
    [InlineData("runtime-repo@sha256:deadbeef", $"{TestImageRegistry}/runtime-repo@sha256:deadbeef", null)]
    [InlineData("ns/runtime-repo@sha256:deadbeef", $"{TestImageRegistry}/ns/runtime-repo@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", $"server.io/ns/runtime-repo@sha256:deadbeef", null)]
    [InlineData("runtime-repo:tag1@sha256:deadbeef", $"{TestImageRegistry}/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("ns/runtime-repo:tag1@sha256:deadbeef", $"{TestImageRegistry}/ns/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", null)]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", "")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", $"server.io/ns/runtime-repo:tag1@sha256:deadbeef", "ubi8")]
    public void ParamBaseImage(string baseImageParam, string expectedBaseImage, string? containerFamily)
    {
        IEnumerable<string>? args =
            !string.IsNullOrEmpty(containerFamily)
            ? new[] { "--build-props", $"ContainerFamily={containerFamily}" }
            : null;

        string[] publishCommandArgs = GetPublishCommandArgs(
            envvars: new()
            {
                { "PARAM_BASE_IMAGE", baseImageParam}
            },
            args: args);

        string? baseImage = publishCommandArgs.FirstOrDefault(p => p.StartsWith("-p:BASE_IMAGE="))?.Substring("-p:BASE_IMAGE=".Length);
        Assert.NotNull(baseImage);

        Assert.Equal(expectedBaseImage, baseImage);

        string expectedContainerFamily = containerFamily ?? "";
        string? containerFamilyProp = publishCommandArgs.LastOrDefault(p => p.StartsWith("-p:ContainerFamily="))?.Substring("-p:ContainerFamily=".Length);
        Assert.NotNull(containerFamilyProp);

        Assert.Equal(expectedContainerFamily, containerFamilyProp);
    }

    [Theory]
    [InlineData("server.io/ns/runtime-repo", "", "server.io/ns/runtime-repo:<<version>>")]
    [InlineData("server.io/ns/runtime-repo:tag1", "", "server.io/ns/runtime-repo:tag1")]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", "", "server.io/ns/runtime-repo@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", "", "server.io/ns/runtime-repo:tag1@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo", "ubi8", "server.io/ns/runtime-repo:<<version>>-ubi8")]
    [InlineData("server.io/ns/runtime-repo:tag1", "ubi8", "server.io/ns/runtime-repo:tag1")]
    [InlineData("server.io/ns/runtime-repo@sha256:deadbeef", "ubi8", "server.io/ns/runtime-repo@sha256:deadbeef")]
    [InlineData("server.io/ns/runtime-repo:tag1@sha256:deadbeef", "ubi8", "server.io/ns/runtime-repo:tag1@sha256:deadbeef")]
    public void BaseImageToContainerBaseImage(string baseImage, string containerFamily, string expectedContainerBaseImage)
    {
        expectedContainerBaseImage = expectedContainerBaseImage.Replace("<<version>>", DotnetVersion);

        // Run a script that writes the ContainerBaseImage to a file.
        string homeDirectory = CreateDirectory();
        string baseImageFile = $"{homeDirectory}/base_image";
        var runResult = RunTask(
            new()
            {
                { "PARAM_BASE_IMAGE", "dummy"}
            },
            dotnetStubScript:
            $"""
            #!/bin/sh
            set -e
            {PrintVersion}
            alias dotnet="/usr/bin/dotnet"
            dotnet new web -o /tmp/web
            dotnet publish /t:ComputeContainerBaseImage -p:CustomBeforeDirectoryBuildProps={OverrideBaseImageTargetsPath} -p:BASE_IMAGE={baseImage} -p:ContainerFamily={containerFamily} --getProperty:ContainerBaseImage /tmp/web --getResultOutputFile:{baseImageFile}
            {WriteImageDigest}
            """,
            homeDirectory: homeDirectory);
        Assert.Empty(runResult.StandardError);
        Assert.Equal(0, runResult.ExitCode);

        Assert.True(File.Exists(baseImageFile));
        Assert.Equal(expectedContainerBaseImage, File.ReadAllText(baseImageFile).Trim());
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
                yield return new object[] { $"{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository=", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string name = "image-name";
                yield return new object[] { $"{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository={name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

                string @namespace = "other-namespace";
                yield return new object[] { $"{@namespace}/{name}{tagSuffix}", new string[] { $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository={@namespace}/{name}", "-p:ContainerImageTag=", $"-p:ContainerImageTags={expectedTags}" } };

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
            $"-p:ContainerRegistry={TestImageRegistry}", $"-p:ContainerRepository=", "-p:ContainerImageTag=", "-p:ContainerImageTags=latest",
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
            {PrintVersion}
            {WriteImageDigest}
            {script}
            """;
        return RunTask(envvars ?? new(), args, homeDirectory, tektonResultsDirectory, dotnetStubScript);
    }
}

[Collection(nameof(TektonTaskTestCollection))]
public class DotnetPublishImageTaskScriptTestsNet9 : DotnetPublishImageTaskScriptTests
{
    protected override string SdkImage => "registry.access.redhat.com/ubi8/dotnet-90";
    protected override string DotnetVersion => "9.0";

    public DotnetPublishImageTaskScriptTestsNet9(DotnetTektonTasks tektonTasks)
      : base(tektonTasks)
    { }
}