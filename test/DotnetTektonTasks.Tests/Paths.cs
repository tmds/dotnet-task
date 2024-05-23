using System.Reflection;

namespace DotnetTektonTasks.Tests;

static class Paths
{
    private static string? _helmChartDirectory;

    public static string HelmChartDirectory
    {
        get
        {
            if (_helmChartDirectory == null)
            {
                string assemblyPath = Assembly.GetExecutingAssembly().Location;
                string? directory = Path.GetDirectoryName(assemblyPath)!;
                while (!File.Exists(Path.Combine(directory!, "Chart.yaml")))
                {
                    directory = Path.GetDirectoryName(directory);
                    if (directory is null)
                    {
                        throw new InvalidOperationException("Could not find Helm Chart.");
                    }
                }
                _helmChartDirectory = directory;
            }
            return _helmChartDirectory;
        }
    }
}