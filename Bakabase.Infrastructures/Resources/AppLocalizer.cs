using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;

namespace Bakabase.Infrastructures.Resources
{
    public class AppLocalizer(IStringLocalizer<Infrastructures.AppSharedResource> localizer)
    {
        public LocalizedString this[string name] => localizer[name];

        public LocalizedString this[string name, params object[] arguments] => localizer[name, arguments];

        public string App_Initializing() => this[nameof(App_Initializing)];
        public string App_Cleaning() => this[nameof(App_Cleaning)];
        public string App_MakingBackups() => this[nameof(App_MakingBackups)];
        public string App_Migrating() => this[nameof(App_Migrating)];
        public string App_FinishingUp() => this[nameof(App_FinishingUp)];
        public string App_FatalError() => this[nameof(App_FatalError)];
        public string App_Warning() => this[nameof(App_Warning)];
        public string App_Exit() => this[nameof(App_Exit)];
        public string App_Minimize() => this[nameof(App_Minimize)];
        public string App_RememberMe() => this[nameof(App_RememberMe)];
        public string App_TipOnExit() => this[nameof(App_TipOnExit)];
        public string App_CriticalTasksRunningOnExit() => this[nameof(App_CriticalTasksRunningOnExit)];
        public string App_MissingWebView2Tip() => this[nameof(App_MissingWebView2Tip)];
        public string App_GoToDownload() => this[nameof(App_GoToDownload)];
    }
}