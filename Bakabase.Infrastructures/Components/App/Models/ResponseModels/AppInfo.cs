using Bakabase.Infrastructures.Components.App.Models.Constants;

namespace Bakabase.Infrastructures.Components.App.Models.ResponseModels
{
    public class AppInfo
    {
        /// <summary>
        /// Effective AppData path. When user has not customised, this equals <see cref="DefaultDataPath"/>.
        /// When env-var override is in effect, this equals the env-var path (which also equals the anchor).
        /// </summary>
        public string AppDataPath { get; set; } = null!;

        /// <summary>
        /// Where <c>app.json</c> lives — the platform-default location, or the env-var path when set.
        /// </summary>
        public string AnchorPath { get; set; } = null!;

        /// <summary>
        /// What <see cref="AppDataPath"/> would be without a user-configured DataPath.
        /// Equal to <see cref="AnchorPath"/> in all current scenarios but kept distinct for forward
        /// compatibility (e.g. if anchor and default ever diverge).
        /// </summary>
        public string DefaultDataPath { get; set; } = null!;

        /// <summary>
        /// Where the effective <see cref="AppDataPath"/> came from.
        /// </summary>
        public DataPathSource DataPathSource { get; set; }

        /// <summary>
        /// Name of the environment variable that overrides the AppData path
        /// (surface to UI for the env-var legend without hard-coding the name on the FE).
        /// </summary>
        public string EnvVarName { get; set; } = null!;

        public string CoreVersion { get; set; } = null!;
        public string? LogPath { get; set; }
        public string BackupPath { get; set; } = null!;
        public string TempFilesPath { get; set; } = null!;
        /// <summary>
        /// "data" folder under <see cref="AppDataPath"/>
        /// </summary>
        public string DataPath { get; set; } = null!;
        public bool NotAcceptTerms { get; set; }
        public bool NeedRestart { get; set; }
    }
}
