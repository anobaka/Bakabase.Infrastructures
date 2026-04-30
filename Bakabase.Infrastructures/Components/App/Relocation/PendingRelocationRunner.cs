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
    public static class PendingRelocationRunner
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
                    case RelocationMode.MergeOverwrite:
                        await RunMergeAsync(
                            anchorDir,
                            currentDataDir,
                            target,
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

        /// <summary>
        /// Merge-overwrite: copy current → target via a staging dir, with same-name target
        /// files overwritten and target-only files preserved (e.g. third-party caches under
        /// the same folder). Steps:
        ///
        /// <list type="number">
        /// <item>Pre-count source files (cheap dirent walk, no stat per file).</item>
        /// <item>Copy each source file to <c>target/.bakabase_relocate_staging/</c>, recording
        /// the copied list <c>(relPath, length)</c>.</item>
        /// <item>Validate the staged copy against that list.</item>
        /// <item>Move every staged file up into <c>target/</c>, overwriting any same-name files.
        /// Target-only files at any level are untouched.</item>
        /// <item>Final integrity check at the destination.</item>
        /// <item>Commit <c>AppOptions.DataPath</c>; best-effort delete the previous data dir if
        /// it's not the anchor or target; delete the marker.</item>
        /// </list>
        /// </summary>
        private static async Task RunMergeAsync(
            string anchorDir,
            string currentDataDir,
            string target,
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

            // Step 1: pre-count copyable files for progress UI. No per-file stat — the kernel
            // dirent walk is bounded by file count, not file size, so this stays fast even on
            // multi-GB AppData with many covers.
            ReportProgress(progress, RelocationPhase.Starting, 0, 0, 0, 0);
            var sources = EnumerateCopyableFiles(currentDataDir).ToList();
            long totalFiles = sources.Count;

            // Step 2: copy to staging, recording the list for integrity validation.
            var copied = new List<RelocationIntegrityValidator.ExpectedFile>(sources.Count);
            long processedFiles = 0;
            long processedBytes = 0;
            ReportProgress(
                progress,
                RelocationPhase.Copying,
                processedFiles,
                totalFiles,
                processedBytes,
                0);

            foreach (var src in sources)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var rel = Path.GetRelativePath(currentDataDir, src);
                var dest = Path.Combine(staging, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: false);

                long length;
                try { length = new FileInfo(src).Length; }
                catch { length = 0; }

                copied.Add(new RelocationIntegrityValidator.ExpectedFile(rel, length));
                processedFiles++;
                processedBytes += length;

                ReportProgress(
                    progress,
                    RelocationPhase.Copying,
                    processedFiles,
                    totalFiles,
                    processedBytes,
                    0,
                    rel);
            }

            // Step 3: validate staging matches what we recorded.
            ReportProgress(
                progress,
                RelocationPhase.Validating,
                processedFiles,
                totalFiles,
                processedBytes,
                0);

            var stagedValidation = RelocationIntegrityValidator.Validate(
                staging,
                copied,
                KnownSqliteRelativePaths);
            if (!stagedValidation.Ok)
            {
                throw new InvalidOperationException(
                    $"Integrity check failed before commit: {stagedValidation.FailureReason}");
            }

            // Step 4: merge staging → target. For each copied entry, move to its target path,
            // creating intermediate dirs and overwriting same-name files. Target-only files
            // (e.g. third-party caches at the same folder) are preserved at every level.
            ReportProgress(
                progress,
                RelocationPhase.Replacing,
                processedFiles,
                totalFiles,
                processedBytes,
                0);

            foreach (var entry in copied)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var src = Path.Combine(staging, entry.RelPath);
                var dest = Path.Combine(target, entry.RelPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (File.Exists(dest))
                {
                    File.Delete(dest);
                }
                File.Move(src, dest);
            }

            // Best-effort: prune now-empty staging subdirs. Stat order doesn't matter — we
            // always nuke the staging root at the end.
            try { Directory.Delete(staging, recursive: true); }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to delete staging dir at {Staging}.", staging);
            }

            // Step 5: final integrity check at the destination.
            var finalValidation = RelocationIntegrityValidator.Validate(
                target,
                copied,
                KnownSqliteRelativePaths);
            if (!finalValidation.Ok)
            {
                throw new InvalidOperationException(
                    $"Integrity check failed after merge: {finalValidation.FailureReason}");
            }

            // Step 6: commit the pointer change. Stash the source dir as previousDataDirIfMoved
            // so IAppDataPathRelocator can rebase any stored absolute paths that still reference
            // it.
            ReportProgress(
                progress,
                RelocationPhase.Finalizing,
                processedFiles,
                totalFiles,
                processedBytes,
                0);

            await saveDataPath(target, previousDataDirIfMoved: currentDataDir);

            // Step 7: best-effort cleanup of the previous data dir if it is now stranded
            // (i.e. neither the anchor nor the new target).
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
                totalFiles,
                processedBytes,
                0);
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
