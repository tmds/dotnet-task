using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace DotnetTektonTasks.Tests;

[Collection(nameof(TektonTaskTestCollection))]
public class TaskDefinitionTests
{
    private readonly DotnetTektonTasks _tektonTasks = new();

    public TaskDefinitionTests(DotnetTektonTasks tektonTasks)
        => _tektonTasks = tektonTasks;

    [Fact]
    public void DotnetPublishImageTask()
    {
        Verify(_tektonTasks.DotnetPublishImageTask.Document);
    }

    [Fact]
    public void DotnetSdkTask()
    {
        Verify(_tektonTasks.DotnetSdkTask.Document);
    }

    [Fact]
    public void Count()
    {
        Assert.Equal(2, _tektonTasks.TaskCount);
        Assert.Empty(_tektonTasks.UnidentifiedDocuments);
    }

    private void Verify(string actual, [CallerFilePath] string sourceFile = "", [CallerMemberName] string memberName = "")
    {
        string sourceFileDirectory = Path.GetDirectoryName(sourceFile)!;
        string expectedFilePath = Path.Combine(sourceFileDirectory, $"{memberName}.verified.txt");
        string receivedFilePath = Path.Combine(sourceFileDirectory, $"{memberName}.received.txt");
        string? expected = null;
        if (File.Exists(expectedFilePath))
        {
            expected = File.ReadAllText(expectedFilePath);
        }
        if (expected == actual)
        {
            return;
        }
        File.WriteAllText(Path.Combine(sourceFileDirectory, $"{memberName}.received.txt"), actual);
        if (File.Exists(expectedFilePath))
        {
            // Launch VS Code diff view
            if (IsProgramFound("code"))
            {
                Process.Start("code", [ "--diff", expectedFilePath, receivedFilePath]);
            }
        }
        Assert.Equal(expected, actual);
    }

    private string[]? _searchPaths;
    private string[] SearchPaths => _searchPaths ??= (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':');

    private bool IsProgramFound(string program)
    {
        bool found = false;
        foreach (var path in SearchPaths)
        {
            string filename = Path.Combine(path, program);
            if (File.Exists(filename))
            {
                found = true;
            }
        }
        return found;
    }
}