using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    [Options]
    public class UpdaterOptions
    {
        /// <summary>
        /// Base URL for Velopack update source (without RID suffix).
        /// The runtime identifier (e.g. win-x64) is appended automatically.
        /// </summary>
        public string VelopackUpdateUrl { get; set; } = "https://cdn-public.anobaka.com/app/bakabase/releases/";
    }
}