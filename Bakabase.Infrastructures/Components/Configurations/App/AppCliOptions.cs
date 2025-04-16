using CommandLine;

namespace Bakabase.Infrastructures.Components.Configurations.App;

public record AppCliOptions
{
    [Option("port", Required = false, HelpText = "Application will discover available ports from start port number.", Default = 34567)]
    public int? StartPort { get; set; }
}