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
// Velopack's own version type — what UpdateManager.CurrentVersion and VelopackAsset.Version
// are expressed in. It lived in NuGet.Versioning until Velopack 1.0 replaced that dependency
// with its own SemanticVersion, so it now comes from the Velopack namespace below.
using Velopack;
using Velopack.Sources;

namespace Bakabase.Infrastructures.Components.App.Upgrade
{
    public class AppUpdater
    {
        private readonly ILogger<AppUpdater> _logger;
        private readonly AppService _appService;
        private readonly IAppUpdateSource _updateSource;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private CancellationTokenSource? _cts;

        /// <summary>Last running/installed pair warned about; see CheckNewVersion.</summary>
        private string? _lastLoggedVersionMismatch;

        public UpdaterState State { get; private set; } = new() { Status = UpdaterStatus.Idle };
        public event Func<UpdaterState, Task>? OnStateChange;

        private UpdateInfo? _lastUpdateInfo;

        public AppUpdater(
            ILogger<AppUpdater> logger,
            AppService appService,
            IAppUpdateSource updateSource,
            IBOptionsManager<AppOptions> appOptionsManager)
        {
            _logger = logger;
            _appService = appService;
            _updateSource = updateSource;
            _appOptionsManager = appOptionsManager;
        }

        /// <summary>
        /// Channel the pre-release switch selects. Must match the <c>--channel</c> the release
        /// pipeline passes to <c>vpk pack</c> for beta/rc builds.
        /// </summary>
        private const string PreReleaseChannel = "beta";

        private static readonly SerilogVelopackLogger VelopackLogger = new();

        private SimpleWebSource CreateSource() =>
            new($"{_updateSource.GetBaseUrl().TrimEnd('/')}/{GetRid()}/");

        private UpdateManager CreateUpdateManager(IUpdateSource source, string channel) =>
            new(source, new UpdateOptions
            {
                AllowVersionDowngrade = false,
                // Always explicit, in both directions. Left null, Velopack falls back to the
                // channel recorded in the install manifest, which made the switch mean two
                // different things: it could not move an install off the channel it was
                // installed from, and an install whose manifest channel was not "beta" kept
                // querying the stable feed while running a beta build — where the newest
                // release is by definition never newer, so the check answered "up to date"
                // forever. Pinning the channel to the switch makes that case reachable and
                // visible instead (see ChannelBehindInstalled).
                ExplicitChannel = channel
            });

        /// <summary>
        /// Channel to query: the pre-release one when the user opted in, otherwise the stable
        /// channel for this OS. The stable channel name mirrors Velopack's per-OS default
        /// (<c>win</c> / <c>osx</c> / <c>linux</c>), which is what <c>vpk pack</c> stamps on a
        /// build packed without an explicit <c>--channel</c>.
        /// </summary>
        private string ResolveChannel() =>
            _appOptionsManager.Value.EnablePreReleaseChannel ? PreReleaseChannel : GetOs();

        /// <summary>
        /// Newest full release the given channel offers, or null when the feed cannot be read.
        /// Velopack does not report this when it decides there is no update, and it is exactly
        /// what distinguishes "you are on the latest" from "this channel can never offer you
        /// anything".
        /// </summary>
        private async Task<SemanticVersion?> ReadChannelLatestVersion(IUpdateSource source, string channel)
        {
            try
            {
                var feed = await source.GetReleaseFeed(VelopackLogger, null, channel);
                return feed.Assets
                    .Where(a => a.Type == VelopackAssetType.Full)
                    .Select(a => a.Version)
                    .OrderByDescending(v => v)
                    .FirstOrDefault();
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Failed to read the release feed of channel {Channel}", channel);
                return null;
            }
        }

        /// <summary>
        /// Short OS name, matching what Velopack uses both as a RID segment and as the default
        /// channel name for a build packed without an explicit <c>--channel</c>.
        /// </summary>
        private static string GetOs() =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
            : "unknown";

        private static string GetRid()
        {
            var os = GetOs();
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
            var channel = ResolveChannel();
            var runningVersion = AppService.CoreVersion.ToString();

            try
            {
                var source = CreateSource();
                var mgr = CreateUpdateManager(source, channel);

                if (!mgr.IsInstalled)
                {
                    _logger.LogWarning(
                        "Update check unavailable: this copy was not installed by Velopack (a development build, or an unpacked copy), so there is nothing to update. Running version: {RunningVersion}",
                        runningVersion);
                    await UpdateState(s =>
                    {
                        s.Error = null;
                        s.Status = UpdaterStatus.Unavailable;
                    });
                    return new AppVersionInfo
                    {
                        RunningVersion = runningVersion,
                        Channel = channel,
                        UpdateCheckUnavailable = true
                    };
                }

                var installedVersion = mgr.CurrentVersion;
                if (installedVersion != null && installedVersion.ToString() != runningVersion)
                {
                    // What a build copied into an existing install directory looks like — the
                    // ordinary local dev loop. From here on every decision is made against the
                    // manifest, not against the version the app shows the user, so it is worth
                    // a warning — but only when the pair changes: the settings page re-checks
                    // on every mount and would otherwise repeat this all session.
                    var mismatch = $"{runningVersion}|{installedVersion}";
                    if (_lastLoggedVersionMismatch != mismatch)
                    {
                        _lastLoggedVersionMismatch = mismatch;
                        _logger.LogWarning(
                            "The running assembly is {RunningVersion} but the Velopack install manifest says {InstalledVersion}. Update checks compare the feed against the manifest.",
                            runningVersion, installedVersion);
                    }
                }

                var updateInfo = await mgr.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    var channelLatest = await ReadChannelLatestVersion(source, channel);
                    var channelBehindInstalled = channelLatest != null && installedVersion != null &&
                                                 channelLatest < installedVersion;

                    _logger.LogInformation(
                        "No update available. channel={Channel}, latest in channel={ChannelLatestVersion}, installed={InstalledVersion}, running={RunningVersion}",
                        channel, channelLatest, installedVersion, runningVersion);

                    await UpdateState(s => s.Status = UpdaterStatus.UpToDate);

                    return new AppVersionInfo
                    {
                        RunningVersion = runningVersion,
                        InstalledVersion = installedVersion?.ToString(),
                        Channel = channel,
                        ChannelLatestVersion = channelLatest?.ToString(),
                        ChannelBehindInstalled = channelBehindInstalled
                    };
                }

                _lastUpdateInfo = updateInfo;

                await UpdateState(s =>
                {
                    if (s.Status is UpdaterStatus.UpToDate or UpdaterStatus.Failed or UpdaterStatus.Unavailable)
                    {
                        s.Status = UpdaterStatus.Idle;
                    }
                });

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

                var packageId = updateInfo.TargetFullRelease.PackageId;
                // vpk names its assets "{packageId}-{channel}-Setup.exe" for every channel,
                // the per-OS default included (2.3.0 ships Bakabase-win-Setup.exe). The
                // suffix used to be dropped whenever the pre-release switch was off, which
                // pointed the "auto-update failed, download manually" links of every stable
                // user at a 404.
                var channelSuffix = $"-{channel}";
                var assetBaseUrl = $"{_updateSource.GetBaseUrl().TrimEnd('/')}/{rid}";

                var fileNames = new System.Collections.Generic.List<string>();
                if (platform == OSPlatform.Windows)
                {
                    fileNames.Add($"{packageId}{channelSuffix}-Setup.exe");
                    fileNames.Add($"{packageId}{channelSuffix}-Portable.zip");
                }
                else if (platform == OSPlatform.OSX)
                {
                    fileNames.Add($"{packageId}{channelSuffix}-Setup.pkg");
                }

                var installers = fileNames.Select(name => new AppVersionInfo.Installer
                {
                    OsPlatform = platform,
                    OsArchitecture = arch,
                    Name = name,
                    Url = $"{assetBaseUrl}/{name}"
                }).ToArray();

                _logger.LogInformation(
                    "Update available: {Version} (channel={Channel}, installed={InstalledVersion}, running={RunningVersion})",
                    version, channel, installedVersion, runningVersion);

                return new AppVersionInfo
                {
                    Version = version,
                    Installers = installers,
                    RunningVersion = runningVersion,
                    InstalledVersion = installedVersion?.ToString(),
                    Channel = channel
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to check for new version via Velopack (channel={Channel})", channel);
                await UpdateState(s =>
                {
                    s.Error = e.Message;
                    s.Status = UpdaterStatus.Failed;
                });
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
                    var mgr = CreateUpdateManager(CreateSource(), ResolveChannel());

                    if (!mgr.IsInstalled)
                    {
                        _logger.LogWarning("Velopack update skipped: application is not installed via Velopack");
                        await UpdateState(s => s.Status = UpdaterStatus.Unavailable);
                        return;
                    }

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
                var mgr = CreateUpdateManager(CreateSource(), ResolveChannel());

                if (!mgr.IsInstalled)
                {
                    throw new InvalidOperationException("Cannot apply updates: application is not installed via Velopack.");
                }

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
