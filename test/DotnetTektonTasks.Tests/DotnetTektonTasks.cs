using SimpleExec;
using YamlDotNet.RepresentationModel;

namespace DotnetTektonTasks.Tests;

[CollectionDefinition(nameof(TektonTaskTestCollection))]
public class TektonTaskTestCollection : ICollectionFixture<DotnetTektonTasks>
{ }

public sealed class TektonTask
{
    private string? _script;

    public required string Document { get; init; }
    public required YamlNode Yaml { get; init; }
    public string Script
        => _script ??= Yaml["spec"]?["steps"]?[0]?["script"]?.ToString() ?? throw new KeyNotFoundException("script");
}

public sealed class DotnetTektonTasks
{
    public const string DotnetPublishImageTaskName = "dotnet-publish-image";
    public const string DotnetSdkImageTaskName = "dotnet-sdk";

    private Dictionary<string, TektonTask> _tasks = new();
    public IReadOnlyList<string> UnidentifiedDocuments { get; private set; } = Array.Empty<string>();

    public DotnetTektonTasks()
    {
        RenderTasks();
    }

    public int TaskCount => _tasks.Count;

    public TektonTask GetTektonTask(string taskName)
        => _tasks[taskName];

    public TektonTask DotnetPublishImageTask => GetTektonTask(DotnetPublishImageTaskName);

    public TektonTask DotnetSdkTask => GetTektonTask(DotnetSdkImageTaskName);

    private void RenderTasks()
    {
        var output = Command.ReadAsync("helm", [ "template", "dotnet-tasks", "--debug", Paths.HelmChartDirectory]).GetAwaiter().GetResult();

        string[] documents = output.StandardOutput.Split("---", StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < documents.Length; i++)
        {
            documents[i] = documents[i].TrimStart();
        }

        List<string> unidentifiedDocuments = new();

        foreach (var document in documents)
        {
            var input = new StringReader(document);
            YamlNode node;
            try
            {
                var yamlStream = new YamlStream();
                yamlStream.Load(input);
                node = yamlStream.Documents[0].RootNode;
                YamlNode metadata = node["metadata"];
                string? name = (string?)metadata["name"];
                _tasks.Add(name!, new TektonTask() { Document = document, Yaml = node });
            }
            catch
            {
                unidentifiedDocuments.Add(document);
            }
        }

        UnidentifiedDocuments = unidentifiedDocuments;
    }
}