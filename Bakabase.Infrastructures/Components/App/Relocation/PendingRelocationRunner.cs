using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Infrastructures.Components.App.Relocation
{
    /// <summary>
    /// Restart-time relocation runner. Reads the marker dropped by the settings UI, executes
    /// the requested mode against a real filesystem, validates the result, then updates
    /// <c>app.json</c> via injected callbacks. All mutations are constrained so a crash
    /// mid-run leaves the marker in place for the next launch to retry.
    /// </summary>
    public sealed class PendingRelocationRunner
    {
        public const string StagingDirName = ".bakabase_relocate_staging";

        /// <summary>
        /// Files at the root of the data dir that must NOT be copied. <c>app.json</c> lives at
        /// the anchor and stays there; the marker is bookkeeping, not user data.
        /// </summary>
        private static readonly HashSet<string> RootExcludes = new(StringComparer.OrdinalIgnoreCase)
        {
            "app.json",
            PendingRelocation.FileName,
        };

        /// <summary>
        /// SQLite databases the integrity check should examine post-copy. Paths are relative
        /// to the data root.
        /// </summary>
        private static readonly string[] KnownSqliteRelativePaths =
        {
            "bakabase_insideworld.db",
            "bootstrap_log.db",
        };

        /// <summary>
        /// Lookup callback returning the current effective data directory (may equal
        /// <paramref name="anchorDir"/> when the user has not customised <c>AppOptions.DataPath</c>).
        /// </summary>
        public delegate string GetCurrentDataDirCallback();

        /// <summary>
        /// Persists the new <c>AppOptions.DataPath</c> value to <c>app.json</c> at the anchor.
        /// <paramref name="previousDataDirIfMoved"/> is the directory data was copied <em>from</em>
        /// (so stored absolute paths can be rebased by <c>IAppDataPathRelocator</c>); pass
        /// <c>null</c> for <see cref="RelocationMode.UseTarget"/> where data did not move.
        /// </summary>
        public delegate Task SaveDataPathCallback(string? newDataPath, string? previousDataDirIfMoved);

        public static async Task<RelocationOutcome> TryRunAsync(
            string anchorDir,
            GetCurrentDataDirCallback getCurrentDataDir,
            SaveDataPathCallback saveDataPath,
            IProgress<RelocationProgress>? progress = null,
            ILogger? logger = null,
            CancellationToken cancellationToken = default)
        {
            logger ??= NullLogger.Instance;

            var currentDataDir = NormaliseDir(getCurrentDataDir());
            var marker = PendingRelocation.TryReadFrom(currentDataDir);
            if (marker == null)
            {
                return RelocationOutcome.NoOp();
            }

            if (marker.SchemaVersion != PendingRelocation.CurrentSchemaVersion)
            {
                logger.LogWarning(
                    "Pending relocation marker has unknown schemaVersion={Schema}; refusing to act.",
                    marker.SchemaVersion);
                return RelocationOutcome.UnknownSchemaVersion(marker.SchemaVersion);
            }

            var target = NormaliseDir(marker.Target);
            anchorDir = NormaliseDir(anchorDir);

            logger.LogInformation(
                "Pending relocation detected. mode={Mode} source={Source} target={Target} anchor={Anchor}",
                marker.Mode, currentDataDir, target, anchorDir);

            try
            {
                switch (marker.Mode)
                {
                    case RelocationMode.UseTarget:
                        await RunUseTargetAsync(target, saveDataPath, currentDataDir, progress);
                        break;
                    case RelocationMode.CopyToEmpty:
                    case RelocationMode.OverwriteTarget:
                        await RunCopyAsync(
                            anchorDir,
                            currentDataDir,
                            target,
                            marker,
                            saveDataPath,
                            progress,
                            logger,
                            cancellationToken);
                        break;
                    default:
                        return RelocationOutcome.Error(
                            $"Unsupported relocation mode {marker.Mode}.");
                }
                return RelocationOutcome.Success();
            }
            catch (OperationCanceledException)
            {
                logger.LogWarning("Pending relocation cancelled by request.");
                CleanupStaging(target);
                return RelocationOutcome.Error("Cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Pending relocation failed; marker preserved for retry.");
                CleanupStaging(target);
                return RelocationOutcome.Error(ex);
            }
        }

        private static async Task RunUseTargetAsync(
            string target,
            SaveDataPathCallback saveDataPath,
            string currentDataDir,
            IProgress<RelocationProgress>? progress)
        {
            ReportProgress(progress, RelocationPhase.Finalizing, 0, 0, 0, 0);
            await saveDataPath(target, previousDataDirIfMoved: null);
            PendingRelocation.Delete(currentDataDir);
            ReportProgress(progress, RelocationPhase.Done, 0, 0, 0, 0);
        }

        private static async Task RunCopyAsync(
            string anchorDir,
            string currentDataDir,
            string target,
            PendingRelocation marker,
            SaveDataPathCallback saveDataPath,
            IProgress<RelocationProgress>? progress,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(target);
            var staging = Path.Combine(target, StagingDirName);

            // Step 0: clean up any leftover staging from a prior failed attempt.
            if (Directory.Exists(staging))
            {
                logger.LogInformation("Removing stale staging dir at {Staging}.", staging);
                Directory.Delete(staging, recursive: true);
            }

            Directory.CreateDirectory(staging);

            // Step 1: enumerate source files (excluding excluded items at root).
            ReportProgress(progress, RelocationPhase.Starting, 0, 0, 0, marker.ExpectedTotalBytes);
            var filesToCopy = EnumerateCopyableFiles(currentDataDir).ToList();

            // Step 2: copy.
            long processedFiles = 0;
            long processedBytes = 0;
            ReportProgress(
                progress,
                RelocationPhase.Copying,
                processedFiles,
                marker.ExpectedFiles,
                processedBytes,
                marker.ExpectedTotalBytes);

            foreach (var src in filesToCopy)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(currentDataDir, src);
                var dest = Path.Combine(staging, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: false);

                processedFiles++;
                try { processedBytes += new FileInfo(src).Length; } catch { }

                ReportProgress(
                    progress,
                    RelocationPhase.Copying,
                    processedFiles,
                    marker.ExpectedFiles,
                    processedBytes,
                    marker.ExpectedTotalBytes,
                    rel);
            }

            // Step 3: validate staging.
            ReportProgress(
                progress,
                RelocationPhase.Validating,
                processedFiles,
                marker.ExpectedFiles,
                processedBytes,
                marker.ExpectedTotalBytes);

            var validation = RelocationIntegrityValidator.Validate(
                staging,
                marker.ExpectedFiles,
                marker.ExpectedTotalBytes,
                KnownSqliteRelativePaths);
            if (!validation.Ok)
            {
                throw new InvalidOperationException(
                    $"Integrity check failed before commit: {validation.FailureReason}");
            }

            // Step 4: if overwrite mode, clear target (excluding staging dir we live in).
            if (marker.Mode == RelocationMode.OverwriteTarget)
            {
                ReportProgress(
                    progress,
                    RelocationPhase.Replacing,
                    processedFiles,
                    marker.ExpectedFiles,
                    processedBytes,
                    marker.ExpectedTotalBytes);

                ClearTargetExcept(target, staging);
            }

            // Step 5: move staging contents up into target.
            foreach (var entry in Directory.GetFileSystemEntries(staging))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = Path.GetFileName(entry);
                var dest = Path.Combine(target, name);

                if (Directory.Exists(entry))
                {
                    Directory.Move(entry, dest);
                }
                else
                {
                    File.Move(entry, dest);
                }
            }

            Directory.Delete(staging, recursive: true);

            // Step 6: integrity check the FINAL location (sanity — should match staging since
            // we just renamed top-level entries).
            var finalValidation = RelocationIntegrityValidator.Validate(
                target,
                marker.ExpectedFiles,
                marker.ExpectedTotalBytes,
                KnownSqliteRelativePaths);
            if (!finalValidation.Ok)
            {
                throw new InvalidOperationException(
                    $"Integrity check failed after move: {finalValidation.FailureReason}");
            }

            // Step 7: commit pointer change. Stash the source dir as previousDataDirIfMoved so
            // IAppDataPathRelocator can rebase any stored absolute paths that still reference it.
            ReportProgress(
                progress,
                RelocationPhase.Finalizing,
                processedFiles,
                marker.ExpectedFiles,
                processedBytes,
                marker.ExpectedTotalBytes);

            await saveDataPath(target, previousDataDirIfMoved: currentDataDir);

            // Step 8: best-effort cleanup of the previous data dir if it is now stranded.
            if (!PathsEqual(currentDataDir, anchorDir) && !PathsEqual(currentDataDir, target))
            {
                TryDeleteDirectory(currentDataDir, logger);
            }

            PendingRelocation.Delete(currentDataDir);
            // Also delete in case marker was inside currentDataDir == target (unlikely but safe).
            PendingRelocation.Delete(target);

            ReportProgress(
                progress,
                RelocationPhase.Done,
                processedFiles,
                marker.ExpectedFiles,
                processedBytes,
                marker.ExpectedTotalBytes);
        }

        public static (long files, long bytes) ComputeExpectedSize(string dataDir)
        {
            long fileCount = 0;
            long totalBytes = 0;
            foreach (var src in EnumerateCopyableFiles(dataDir))
            {
                fileCount++;
                try { totalBytes += new FileInfo(src).Length; } catch { }
            }
            return (fileCount, totalBytes);
        }

        public static IEnumerable<string> EnumerateCopyableFiles(string root)
        {
            if (!Directory.Exists(root)) yield break;

            foreach (var entry in Directory.EnumerateFileSystemEntries(root))
            {
                var name = Path.GetFileName(entry);
                if (RootExcludes.Contains(name)) continue;
                if (name.Equals(StagingDirName, StringComparison.OrdinalIgnoreCase)) continue;

                if (Directory.Exists(entry))
                {
                    foreach (var f in Directory.EnumerateFiles(entry, "*", SearchOption.AllDirectories))
                    {
                        yield return f;
                    }
                }
                else
                {
                    yield return entry;
                }
            }
        }

        private static void ClearTargetExcept(string target, string keep)
        {
            keep = NormaliseDir(keep);
            foreach (var entry in Directory.GetFileSystemEntries(target))
            {
                if (PathsEqual(entry, keep)) continue;
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
        }

        private static void CleanupStaging(string target)
        {
            try
            {
                var staging = Path.Combine(target, StagingDirName);
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
            catch
            {
                // best-effort
            }
        }

        private static void TryDeleteDirectory(string dir, ILogger logger)
        {
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to delete previous data dir at {Dir}; user can clean it up manually.",
                    dir);
            }
        }

        private static void ReportProgress(
            IProgress<RelocationProgress>? progress,
            RelocationPhase phase,
            long processedFiles,
            long totalFiles,
            long processedBytes,
            long totalBytes,
            string? currentFile = null)
        {
            progress?.Report(new RelocationProgress
            {
                Phase = phase,
                ProcessedFiles = processedFiles,
                TotalFiles = totalFiles,
                ProcessedBytes = processedBytes,
                TotalBytes = totalBytes,
                CurrentFile = currentFile,
            });
        }

        private static string NormaliseDir(string path) =>
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool PathsEqual(string a, string b) =>
            string.Equals(NormaliseDir(a), NormaliseDir(b), StringComparison.OrdinalIgnoreCase);
    }
}
