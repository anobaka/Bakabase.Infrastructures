namespace Bakabase.Infrastructures.Components.App.Models.ResponseModels
{
    public class AppInfo
    {
        /// <summary>
        /// Root path for following paths
        /// </summary>
        public string AppDataPath { get; set; } = null!;
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