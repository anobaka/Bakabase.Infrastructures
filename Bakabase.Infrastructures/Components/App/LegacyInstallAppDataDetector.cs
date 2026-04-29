using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Detects the historical <c>&lt;install&gt;/current/AppData</c> directory left behind
    /// after the AppData path migration, and writes the result into
    /// <see cref="LegacyInstallNoticeState"/> so it can be replayed to any future hub
    /// connection (not just the one that happens to be open at detection time).
    ///
    /// Detection itself is deferred to <see cref="IHostApplicationLifetime.ApplicationStarted"/>
    /// — the WebHost is fully up by then, so the optional fan-out push to currently-connected
    /// clients reaches the SignalR pipeline reliably. Late-connecting clients get the notice
    /// via <c>WebGuiHub.GetInitialData()</c> reading the state singleton.
    ///
    /// The user dismisses by setting <see cref="AppOptions.LegacyInstallNoticeDismissedAt"/>
    /// (via the dismiss endpoint, which also clears the state); the notice then never fires
    /// again on subsequent launches.
    /// </summary>
    public sealed class LegacyInstallAppDataDetector : IHostedService
    {
        private readonly AppService _appService;
        private readonly IBOptionsManager<AppOptions> _appOptionsManager;
        private readonly ILegacyInstallNotifier _notifier;
        private readonly LegacyInstallNoticeState _state;
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogger<LegacyInstallAppDataDetector> _logger;

        public LegacyInstallAppDataDetector(
            AppService appService,
            IBOptionsManager<AppOptions> appOptionsManager,
            ILegacyInstallNotifier notifier,
            LegacyInstallNoticeState state,
            IHostApplicationLifetime lifetime,
            ILogger<LegacyInstallAppDataDetector> logger)
        {
            _appService = appService;
            _appOptionsManager = appOptionsManager;
            _notifier = notifier;
            _state = state;
            _lifetime = lifetime;
            _logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Defer detection until the application is fully started: by the time
            // ApplicationStarted fires, all hosted services have completed StartAsync, the
            // WebHost is listening, and SignalR endpoint is mapped. The hub may still have
            // zero connected clients at this moment, but that's why we also persist the
            // result in LegacyInstallNoticeState — late connections replay it through
            // GetInitialData.
            _lifetime.ApplicationStarted.Register(() =>
                _ = Task.Run(() => DetectAsync(_lifetime.ApplicationStopping)));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task DetectAsync(CancellationToken cancellationToken)
        {
            try
            {
                var detection = Detect(
                    System.AppContext.BaseDirectory,
                    _appService.AppDataDirectory,
                    _appOptionsManager.Value.LegacyInstallNoticeDismissedAt);

                if (detection.ShouldNotify && detection.LegacyPath != null)
                {
                    _logger.LogInformation(
                        "Detected legacy AppData at {Path}; persisting + broadcasting.",
                        detection.LegacyPath);

                    // 1) Persist so newly-connected clients can pick it up via GetInitialData.
                    _state.PendingPath = detection.LegacyPath;
                    // 2) Push to whoever happens to be already connected.
                    await _notifier.NotifyAsync(detection.LegacyPath, cancellationToken);
                }
                else
                {
                    _logger.LogDebug(
                        "Legacy install detection: no notice. reason={Reason}",
                        detection.Reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Legacy install detection failed.");
            }
        }

        public sealed class Detection
        {
            public bool ShouldNotify { get; set; }
            public string? LegacyPath { get; set; }
            public string? Reason { get; set; }
        }

        public static Detection Detect(
            string baseDirectory,
            string currentDataDir,
            DateTime? dismissedAt,
            Func<string, bool>? directoryExists = null,
            Func<string, bool>? hasAnyEntries = null)
        {
            directoryExists ??= Directory.Exists;
            hasAnyEntries ??= path =>
                Directory.EnumerateFileSystemEntries(path).Any();

            if (dismissedAt.HasValue)
            {
                return new Detection { Reason = "Dismissed by user" };
            }

            var installRoot = DataPathValidator.FindVelopackInstallRootDefault(baseDirectory);
            if (installRoot == null)
            {
                return new Detection { Reason = "Not running inside a Velopack install" };
            }

            var legacy = Path.Combine(installRoot, "current", "AppData");
            if (!directoryExists(legacy))
            {
                return new Detection { Reason = "Legacy path absent" };
            }

            try
            {
                if (!hasAnyEntries(legacy))
                {
                    return new Detection { Reason = "Legacy path empty" };
                }
            }
            catch (Exception ex)
            {
                return new Detection { Reason = $"Could not enumerate legacy path: {ex.Message}" };
            }

            // If user explicitly kept their DataPath at the legacy location, do not nag.
            var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(currentDataDir));
            var legacyNorm = Path.TrimEndingDirectorySeparator(Path.GetFullPath(legacy));
            if (string.Equals(current, legacyNorm, StringComparison.OrdinalIgnoreCase))
            {
                return new Detection
                {
                    Reason = "Current data dir IS the legacy location (user choice)",
                };
            }

            return new Detection
            {
                ShouldNotify = true,
                LegacyPath = legacyNorm,
            };
        }
    }

    /// <summary>
    /// Indirection so the detector lives in <c>Bakabase.Infrastructures</c> without depending
    /// on the SignalR hub (which lives in the legacy business assembly). The hub-backed
    /// implementation registers in <c>BakabaseStartup</c>.
    /// </summary>
    public interface ILegacyInstallNotifier
    {
        Task NotifyAsync(string legacyPath, CancellationToken cancellationToken);
    }
}
