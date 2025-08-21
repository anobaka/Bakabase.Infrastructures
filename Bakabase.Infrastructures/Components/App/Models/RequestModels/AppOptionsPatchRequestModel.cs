using System.Collections.Generic;
using Bakabase.Infrastructures.Components.Gui;

namespace Bakabase.Infrastructures.Components.App.Models.RequestModels
{
    public class AppOptionsPatchRequestModel
    {
        public string? Language { get; set; }
        public bool? EnablePreReleaseChannel { get; set; }
        public bool? EnableAnonymousDataTracking { get; set; }
        public CloseBehavior? CloseBehavior { get; set; }
        public UiTheme? UiTheme { get; set; }
        public int? AutoListeningPortCount { get; set; }
        public List<int>? ListeningPorts { get; set; } 
    }
}
