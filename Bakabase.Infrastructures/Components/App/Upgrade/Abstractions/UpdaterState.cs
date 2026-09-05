using System;
using System.Collections.Generic;

namespace Bakabase.Infrastructures.Components.App.Upgrade.Abstractions
{
    public enum UpdaterStatus
    {
        Idle = 1,
        Running = 2,
        PendingRestart = 3,
        UpToDate = 4,
        Failed = 5,

        /// <summary>
        /// No update check can run: the app was not installed by Velopack (a development
        /// build, or an unpacked copy), so there is no install manifest to compare against.
        /// Distinct from <see cref="UpToDate"/>, which used to be reported here and claimed
        /// the app was on the latest version without ever asking the server.
        /// </summary>
        Unavailable = 6
    }

    public class UpdaterState
    {
        public int FailedFileCount { get; set; }
        public int SkippedFileCount { get; set; }
        public int DownloadedFileCount { get; set; }
        public int TotalFileCount { get; set; }

        public int Percentage =>
            TotalFileCount == 0 ? 0 : (SkippedFileCount + DownloadedFileCount) * 100 / TotalFileCount;

        public DateTime StartDt { get; set; }

        public string? Error { get; set; }
        public UpdaterStatus Status { get; set; }

        public void Reset()
        {
            FailedFileCount = 0;
            SkippedFileCount = 0;
            DownloadedFileCount = 0;
            TotalFileCount = 0;
            StartDt = default;
            Error = null;
            Status = default;
        }
    }
}