using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using JetBrains.Annotations;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Adapters
{
    /// <summary>
    /// todo: temporary solution
    /// </summary>
    public interface IBakabaseUpdater
    {
        Task StartUpdater(int pid, string processName, string appDir, string newFilesDir, string executable,
            [CanBeNull] AppVersionInfo.Installer installer);
    }
}
