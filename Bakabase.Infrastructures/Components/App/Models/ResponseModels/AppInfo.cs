namespace Bakabase.Infrastructures.Components.App.Models.ResponseModels
{
    public class AppInfo
    {
        public string AppDataPath { get; set; }
        public string CoreVersion { get; set; }
        public string LogPath { get; set; }
        public string BackupPath { get; set; }
        public string UpdaterPath { get; set; }
        public string WebRootPath { get; set; }
        public bool NotAcceptTerms { get; set; }
        public bool NeedRestart { get; set; }
    }
}