using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Bakabase.Infrastructures.Components.Configurations.App;

public record EnvOptions
{
    [ConfigurationKeyName("API_LISTENING_PORTS")]
    private string ListeningPortsString { get; set; } = string.Empty;

    public IEnumerable<int> ListeningPorts
    {
        get
        {
            return ListeningPortsString.Split([',', ';'], System.StringSplitOptions.RemoveEmptyEntries)
                .Select(port => port.Trim())
                .Select(port => int.TryParse(port, out var p) ? p : 0)
                .Where(port => port > 0).Distinct().ToList();
        }
    }
}