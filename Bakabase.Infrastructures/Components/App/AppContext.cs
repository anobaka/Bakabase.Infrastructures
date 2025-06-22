using JetBrains.Annotations;

namespace Bakabase.Infrastructures.Components.App;

public record AppContext
{
    public string[] ListeningAddresses { get; set; }
    public string[] ApiEndpoints { get; set; }
    [CanBeNull] public string ApiEndpoint { get; set; }
}