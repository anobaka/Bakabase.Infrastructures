using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    [Options]
    public class UpdaterOptions
    {
        /// <summary>
        /// Base URL for Velopack update source.
        /// Example: https://your-domain.com/app/bakabase/releases/win-x64/
        /// </summary>
        public string VelopackUpdateUrl { get; set; } = null!;
    }
}