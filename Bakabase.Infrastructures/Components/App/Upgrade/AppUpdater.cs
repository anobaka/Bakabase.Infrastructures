using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.App.Upgrade.Adapters;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Components.Tasks.Progressor.Abstractions.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Semver;

namespace Bakabase.Infrastructures.Components.App.Upgrade
{
    public class AppUpdater : AbstractUpdater
    {
        protected override SemVersion CurrentVersion => AppService.CoreVersion;
        private readonly IHostApplicationLifetime _lifetime;

        protected override string AppRootPath => Path.GetDirectoryName(Assembly.GetEntryAssembly()!.Location)!;

        protected override string UnpackedFilesOssPathAfterVersion => "unpacked/win/";
        protected override string OssObjectPrefix => Options.Value.AppUpdaterOssObjectPrefix;
        private static Process _mainProcess;

        public static Process MainProcess => _mainProcess ??= Process.GetCurrentProcess();
        private readonly IBakabaseUpdater _updater;

        public AppUpdater(OssDownloader downloader, ILogger<AbstractUpdater> logger, AppService appService,
            IBOptionsManager<UpdaterOptions> updaterOptionsManager, IBOptionsManager<AppOptions> appOptionsManager,
            IHostApplicationLifetime lifetime, IBakabaseUpdater updater) : base(downloader, logger, appService,
            updaterOptionsManager,
            appOptionsManager)
        {
            _lifetime = lifetime;
            _updater = updater;
        }

        protected override async Task<bool> GetEnablePreReleaseChannel() =>
            (AppOptionsManager).Value.EnablePreReleaseChannel;

        protected override async Task UpdateWithDownloadedFiles(AppVersionInfo version)
        {
            await UpdateState(s => s.Status = UpdaterStatus.PendingRestart);
        }

        public async Task StartUpdater()
        {
            var newVersion = await CheckNewVersion();
            newVersion.Installers[0].OsPlatform = OSPlatform.Windows;
            newVersion.Installers[0].OsArchitecture = Architecture.X64;

            var installer = newVersion.Installers?.FirstOrDefault(a =>
                a.OsPlatform != null && RuntimeInformation.IsOSPlatform(a.OsPlatform.Value) &&
                RuntimeInformation.OSArchitecture == a.OsArchitecture);

            await _updater.StartUpdater(MainProcess.Id, MainProcess.ProcessName,
                Path.GetDirectoryName(MainProcess.MainModule!.FileName), DownloadDir, MainProcess.MainModule.FileName,
                installer);
        }
    }
}