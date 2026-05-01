using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Default max parallelism is half of the processor count
        /// </summary>
        public static int DefaultMaxParallelism => Math.Max(1, Environment.ProcessorCount / 2);

        public string Language { get; set; } = null!;
        public string Version { get; set; } = AppConstants.InitialVersion;
        public bool EnablePreReleaseChannel { get; set; }
        public bool EnableAnonymousDataTracking { get; set; } = true;
        public string WwwRootPath { get; set; } = null!;

        /// <summary>
        /// Safety net for legacy absolute-path data left in the DB. Set to the previous
        /// data dir at the moment a relocation completes; consumed by
        /// <see cref="Bakabase.Abstractions.Components.FileSystem.IAppDataPathRelocator"/>
        /// to rebase any stored absolute paths under the old root → new root, then cleared
        /// by the one-shot relocation migrator (V230 <c>PathsRelocationMigrator</c> and its
        /// successors) after rewrites complete.
        ///
        /// Why this is "safety net" rather than "core mechanism": all path-bearing DB
        /// columns are supposed to be written via <c>AppDataPaths.RelativizeAll</c> /
        /// <c>Relativize</c>, so values are stored AppData-relative. A relative path
        /// follows the data automatically when <c>AnchorRedirect</c> flips the effective
        /// data dir — no rebase needed. <see cref="PrevDataPath"/> only matters when
        /// (a) older builds wrote absolute paths into the DB before that convention took
        /// hold, or (b) some write path forgets to relativize. If you ever audit and
        /// confirm every writer is relative-clean, this field plus the corresponding
        /// branch in <c>AppDataPathRelocator.CollectOldRoots</c> can be removed.
        ///
        /// The current data dir itself is identified by <see cref="AnchorRedirect"/> at
        /// the platform anchor — never stored here.
        /// </summary>
        public string PrevDataPath { get; set; } = null!;
        public CloseBehavior CloseBehavior { get; set; } = CloseBehavior.Prompt;
        public UiTheme UiTheme { get; set; }
        public int? AutoListeningPortCount { get; set; }
        public List<int>? ListeningPorts { get; set; }

        /// <summary>
        /// Maximum degree of parallelism for CPU-intensive operations.
        /// Default is half of the processor count. Set to 1 to disable parallelism.
        /// </summary>
        public int? MaxParallelism { get; set; }

        /// <summary>
        /// Gets the effective max parallelism value, using default if not set.
        /// </summary>
        public int EffectiveMaxParallelism => MaxParallelism ?? DefaultMaxParallelism;

        /// <summary>
        /// IANA timezone ID (e.g. "Asia/Tokyo", "America/New_York").
        /// When null, the system's local timezone is used.
        /// </summary>
        public string? TimeZoneId { get; set; }

        /// <summary>
        /// Set when the user dismisses the "old install AppData still exists" notice. We never
        /// auto-delete the legacy directory; this just suppresses re-firing the banner.
        /// </summary>
        public DateTime? LegacyInstallNoticeDismissedAt { get; set; }

        /// <summary>
        /// Gets the effective TimeZoneInfo, resolving from <see cref="TimeZoneId"/> or falling back to system local.
        /// </summary>
        public TimeZoneInfo EffectiveTimeZone
        {
            get
            {
                if (string.IsNullOrEmpty(TimeZoneId))
                {
                    return TimeZoneInfo.Local;
                }

                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
                }
                catch
                {
                    return TimeZoneInfo.Local;
                }
            }
        }

        public bool IsNotInitialized() => Version == AppConstants.InitialVersion;
    }
}