using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Storage;
using Bootstrap.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Bakabase.Infrastructures.Components.App.Upgrade
{
    public class UpdaterUpdater : AbstractUpdater
    {
        protected override SemVersion CurrentVersion
        {
            get
            {
                var updaterFullname = Path.Combine(AppRootPath, "Bakabase.Updater.exe");
                if (File.Exists(updaterFullname))
                {
                    var vi = FileVersionInfo.GetVersionInfo(updaterFullname);
                    return SemVersion.Parse(vi.ProductVersion);
                }

                return SemVersion.Parse(AppConstants.InitialVersion, SemVersionStyles.Any);
            }
        }

        protected override string AppRootPath => RootPathPath;
        public string RootPathPath => Path.Combine(AppService.AppDataDirectory, "updater");
        protected override string UnpackedFilesOssPathAfterVersion => string.Empty;
        protected override string OssObjectPrefix => Options.Value.UpdaterUpdaterOssObjectPrefix;

        public UpdaterUpdater(OssDownloader downloader, ILogger<AbstractUpdater> logger, AppService appService,
            IBOptionsManager<UpdaterOptions> updaterOptionsManager, IBOptionsManager<AppOptions> appOptionsManager) :
            base(downloader, logger, appService, updaterOptionsManager, appOptionsManager)
        {
        }
    }
}