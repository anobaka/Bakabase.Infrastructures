using System;
using System.IO;
using System.Linq;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.Configurations.App;
using Microsoft.Extensions.Logging;
using Semver;

namespace Bakabase.Infrastructures.Components.App;

/// <summary>
/// Creates version snapshots before migration and limits their number on every startup.
/// A snapshot is published only after all its files have been copied successfully.
/// </summary>
internal static class AutomaticBackup
{
    internal static void Run(string appDataDirectory, SemVersion previousVersion, SemVersion currentVersion,
        AppOptions options, ILogger logger, Action<string, string>? copyFile = null)
    {
        if (!options.EnableAutomaticBackup) return;

        var backupDirectory = Path.Combine(appDataDirectory, "backups");
        string? protectedVersion = null;
        if (previousVersion != currentVersion &&
            previousVersion != SemVersion.Parse(AppConstants.InitialVersion, SemVersionStyles.Any))
        {
            logger.LogInformation("New version of app is starting, start making backups...");
            Directory.CreateDirectory(backupDirectory);
            EnsureNotLink(backupDirectory);
            protectedVersion = previousVersion.ToString();
            CreateSnapshot(appDataDirectory, backupDirectory, protectedVersion, logger,
                copyFile ?? ((source, target) => File.Copy(source, target)));
        }

        // Configurations loaded directly from app.json may predate validation. Never
        // interpret an invalid limit as permission to delete every recoverable snapshot.
        Prune(backupDirectory, options.MaxBackupVersions > 0 ? options.MaxBackupVersions : 7,
            protectedVersion, logger);
    }

    private static void CreateSnapshot(string source, string backupDirectory, string version, ILogger logger,
        Action<string, string> copyFile)
    {
        var target = Path.Combine(backupDirectory, version);
        var staging = Path.Combine(backupDirectory, $".automatic-backup-{Guid.NewGuid():N}");
        string? replaced = null;
        try
        {
            Directory.CreateDirectory(staging);
            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                var name = Path.GetFileName(directory);
                if (IsExcludedDirectory(name)) continue;
                logger.LogInformation("Making backups of {Directory}", directory);
                CopyDirectory(directory, Path.Combine(staging, name), copyFile);
            }

            foreach (var file in Directory.EnumerateFiles(source))
            {
                // The running process holds this file exclusively. It belongs to the
                // process, so it must never be restored with application data.
                if (string.Equals(Path.GetFileName(file), DataDirectoryLock.FileName,
                        StringComparison.OrdinalIgnoreCase)) continue;
                logger.LogInformation("Making backups of {File}", file);
                copyFile(file, Path.Combine(staging, Path.GetFileName(file)));
            }

            Directory.SetLastWriteTimeUtc(staging, DateTime.UtcNow);
            if (Directory.Exists(target))
            {
                // Revisiting a version (or retrying an old partial backup) must not
                // overwrite its recoverable files until the new copy is complete.
                EnsureTreeHasNoLinks(target);
                replaced = Path.Combine(backupDirectory, $".replaced-backup-{Guid.NewGuid():N}");
                Directory.Move(target, replaced);
            }

            try
            {
                Directory.Move(staging, target);
            }
            catch
            {
                if (replaced != null)
                {
                    // Preserve the original at the recovery path even if restoring
                    // its name fails; never delete it in the failure cleanup.
                    try
                    {
                        Directory.Move(replaced, target);
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        logger.LogError(ex, "Previous backup remains recoverable at {Path}", replaced);
                    }
                }

                throw;
            }

            if (replaced != null) TryDeleteOwnedDirectory(replaced, logger);
        }
        finally
        {
            // Temporary snapshots never use a version name and never participate in
            // retention. A failed copy cannot evict an older, complete backup.
            TryDeleteOwnedDirectory(staging, logger);
        }
    }

    private static bool IsExcludedDirectory(string name) =>
        new[] { "backups", "temp", "components", "data" }
            .Contains(name, StringComparer.OrdinalIgnoreCase);

    private static void CopyDirectory(string source, string target, Action<string, string> copyFile)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            copyFile(file, Path.Combine(target, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)), copyFile);
        }
    }

    private static void Prune(string backupDirectory, int limit, string? protectedVersion, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(backupDirectory)) return;
            EnsureNotLink(backupDirectory);
            var backups = new DirectoryInfo(backupDirectory).EnumerateDirectories()
                // Only direct, strictly version-named directories belong to this
                // feature. User-created directories, files and links are untouched.
                .Where(directory => !IsLink(directory.FullName) &&
                                    SemVersion.TryParse(directory.Name, SemVersionStyles.Strict, out _))
                .OrderByDescending(directory => directory.Name == protectedVersion)
                .ThenByDescending(directory => directory.LastWriteTimeUtc)
                .ThenByDescending(directory => directory.Name, StringComparer.Ordinal)
                .Skip(limit)
                .ToArray();

            foreach (var backup in backups)
            {
                try
                {
                    EnsureTreeHasNoLinks(backup.FullName);
                    backup.Delete(recursive: true);
                    logger.LogInformation("Removed old version backup {Version}", backup.Name);
                }
                catch (Exception ex) when (IsFileSystemError(ex))
                {
                    logger.LogWarning(ex, "Unable to remove old version backup {Path}", backup.FullName);
                }
            }
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            // Retention is housekeeping; an inaccessible or linked backup directory
            // should not prevent the application from starting.
            logger.LogWarning(ex, "Unable to clean up version backups in {Path}", backupDirectory);
        }
    }

    private static void EnsureTreeHasNoLinks(string directory)
    {
        EnsureNotLink(directory);
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            EnsureNotLink(entry.FullName);
            if (entry is DirectoryInfo) EnsureTreeHasNoLinks(entry.FullName);
        }
    }

    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static void EnsureNotLink(string path)
    {
        if (IsLink(path)) throw new IOException($"Automatic backups do not follow symbolic links: {path}");
    }

    private static bool IsFileSystemError(Exception exception) => exception is IOException or UnauthorizedAccessException;

    private static void TryDeleteOwnedDirectory(string path, ILogger logger)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            EnsureTreeHasNoLinks(path);
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (IsFileSystemError(ex))
        {
            logger.LogWarning(ex, "Unable to clean up temporary backup {Path}", path);
        }
    }
}
