using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Adapters
{
    public class NullBakabaseUpdater: IBakabaseUpdater
    {
        public Task StartUpdater(int pid, string processName, string appDir, string newFilesDir, string executable,
            AppVersionInfo.Installer? installer)
        {
            return Task.CompletedTask;
        }
    }
}
