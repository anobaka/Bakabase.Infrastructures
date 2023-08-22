using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.Gui;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Infrastructures.Components.Configurations.App
{
    /// <summary>
    /// General app options
    /// </summary>
    [Options]
    public sealed class AppOptions
    {
        public string Language { get; set; }
        public string Version { get; set; } = AppConstants.InitialVersion;
        public bool EnablePreReleaseChannel { get; set; }
        public bool EnableAnonymousDataTracking { get; set; } = true;
        public string WwwRootPath { get; set; }
        public string DataPath { get; set; }
        public string PrevDataPath { get; set; }
        public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Prompt;
        public UiTheme UiTheme { get; set; }

        public bool IsNotInitialized() => Version == AppConstants.InitialVersion;
    }
}