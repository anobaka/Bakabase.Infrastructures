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

        /// <summary>
        /// True iff <see cref="LegacyInstallAppDataDetector"/> found a populated
        /// <c>&lt;install&gt;/current/AppData</c> the user might want to migrate, AND the user
        /// has not dismissed the legacy notice. Used by the dashboard one-time hint to point
        /// users at the relocation UI on first 2.3 launch.
        /// </summary>
        public bool MayHaveLegacyData { get; set; }

        /// <summary>
        /// True iff <see cref="AppDataPath"/> resolves to anywhere inside the Velopack install
        /// root. Such data is destroyed when the user either uninstalls Bakabase or runs
        /// "Repair" from the installer (both wipe the install dir wholesale), so the UI
        /// surfaces a warning. With the default Windows path now sitting outside the install
        /// root (<c>%LocalAppData%\Bakabase.AppData</c> vs Velopack's <c>%LocalAppData%\Bakabase</c>),
        /// this flag is normally false in fresh installs; it can still flip to true for users
        /// inherited from the brief 2.3.0-beta.69~74 window where the anchor coincided with
        /// the install root.
        /// </summary>
        public bool DataInInstallRoot { get; set; }
    }
}
