using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Semver;
using Velopack;
using Velopack.Sources;

namespace Bakabase.Infrastructures.Components.App.Upgrade
{
    public class AppUpdater
    {
        private readonly ILogger<AppUpdater> _logger;
        private readonly AppService _appService;
        private readonly IBOptionsManager<UpdaterOptions> _updaterOptionsManager;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private CancellationTokenSource? _cts;

        public UpdaterState State { get; private set; } = new() { Status = UpdaterStatus.Idle };
        public event Func<UpdaterState, Task>? OnStateChange;

        private UpdateInfo? _lastUpdateInfo;

        public AppUpdater(
            ILogger<AppUpdater> logger,
            AppService appService,
            IBOptionsManager<UpdaterOptions> updaterOptionsManager,
            IBOptionsManager<AppOptions> appOptionsManager)
        {
            _logger = logger;
            _appService = appService;
            _updaterOptionsManager = updaterOptionsManager;
            _appOptionsManager = appOptionsManager;
        }

        private UpdateManager CreateUpdateManager()
        {
            var updateUrl = _updaterOptionsManager.Value.VelopackUpdateUrl;

            if (string.IsNullOrEmpty(updateUrl))
            {
                throw new InvalidOperationException("VelopackUpdateUrl is not configured.");
            }

            var source = new SimpleWebSource(updateUrl);
            var mgr = new UpdateManager(source, new UpdateOptions
            {
                AllowVersionDowngrade = false,
                ExplicitChannel = _appOptionsManager.Value.EnablePreReleaseChannel ? "beta" : null
            });

            return mgr;
        }

        private static string GetRid()
        {
            var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
                : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
                : "unknown";
            var arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "x64",
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                _ => "x64"
            };
            return $"{os}-{arch}";
        }

        public async Task<AppVersionInfo?> CheckNewVersion()
        {
            try
            {
                var mgr = CreateUpdateManager();
                var updateInfo = await mgr.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    return null;
                }

                _lastUpdateInfo = updateInfo;

                var version = updateInfo.TargetFullRelease.Version.ToString();
                var rid = GetRid();
                var osParts = rid.Split('-');

                OSPlatform? platform = osParts[0] switch
                {
                    "win" => OSPlatform.Windows,
                    "osx" => OSPlatform.OSX,
                    "linux" => OSPlatform.Linux,
                    _ => null
                };

                Enum.TryParse<Architecture>(osParts.Length > 1 ? osParts[1] : "X64", true, out var arch);

                return new AppVersionInfo
                {
                    Version = version,
                    Installers =
                    [
                        new AppVersionInfo.Installer
                        {
                            OsPlatform = platform,
                            OsArchitecture = arch,
                            Name = updateInfo.TargetFullRelease.FileName,
                            Url = string.Empty,
                            Size = updateInfo.TargetFullRelease.Size
                        }
                    ]
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to check for new version via Velopack");
                return null;
            }
        }

        public async Task<Bootstrap.Models.ResponseModels.BaseResponse> StartUpdating()
        {
            if (_cts?.IsCancellationRequested == false)
            {
                return Bootstrap.Components.Miscellaneous.ResponseBuilders.BaseResponseBuilder.Ok;
            }

            _cts = new CancellationTokenSource();

            await UpdateState(s =>
            {
                s.Reset();
                s.StartDt = DateTime.Now;
                s.Status = UpdaterStatus.Running;
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    var mgr = CreateUpdateManager();

                    if (_lastUpdateInfo == null)
                    {
                        _lastUpdateInfo = await mgr.CheckForUpdatesAsync();
                    }

                    if (_lastUpdateInfo == null)
                    {
                        await UpdateState(s =>
                        {
                            s.Status = UpdaterStatus.UpToDate;
                        });
                        return;
                    }

                    await UpdateState(s => s.TotalFileCount = 1);

                    await mgr.DownloadUpdatesAsync(_lastUpdateInfo, progress =>
                    {
                        _ = UpdateState(s =>
                        {
                            s.DownloadedFileCount = progress >= 100 ? 1 : 0;
                            s.TotalFileCount = 1;
                        });
                    }, cancelToken: _cts.Token);

                    await UpdateState(s => s.Status = UpdaterStatus.PendingRestart);
                }
                catch (OperationCanceledException)
                {
                    await UpdateState(s =>
                    {
                        s.Reset();
                        s.Status = UpdaterStatus.Idle;
                    });
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to download updates via Velopack");
                    await UpdateState(s =>
                    {
                        s.Error = e.Message;
                        s.Status = UpdaterStatus.Failed;
                    });
                }
            }, _cts.Token);

            return Bootstrap.Components.Miscellaneous.ResponseBuilders.BaseResponseBuilder.Ok;
        }

        public void StopUpdating()
        {
            _cts?.Cancel();
        }

        public async Task ApplyUpdatesAndRestart()
        {
            if (_lastUpdateInfo == null)
            {
                throw new InvalidOperationException("No update has been downloaded.");
            }

            try
            {
                var mgr = CreateUpdateManager();
                mgr.ApplyUpdatesAndRestart(_lastUpdateInfo);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to apply updates and restart via Velopack");
                throw;
            }
        }

        public async Task UpdateState(Action<UpdaterState> update)
        {
            update(State);
            if (OnStateChange != null)
            {
                await OnStateChange(State);
            }
        }
    }
}
