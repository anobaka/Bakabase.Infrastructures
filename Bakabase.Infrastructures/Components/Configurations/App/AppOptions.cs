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
        public string? DataPath { get; set; }
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