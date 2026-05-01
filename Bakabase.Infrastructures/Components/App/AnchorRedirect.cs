using System;
using System.IO;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Anchor-side pointer file. When present at <c>{anchorDir}/.redirect</c>, its single
    /// line of UTF-8 content is the absolute path of the user's effective AppData directory
    /// (the directory that owns <c>app.json</c> and runtime data). When absent, the anchor
    /// itself is the AppData directory.
    ///
    /// Lives outside of <see cref="Bakabase.Infrastructures.Components.Configurations.App.AppOptions"/>
    /// because reading <c>app.json</c> requires knowing where it is, which is what the
    /// redirect tells us. Pure file IO, no DI, no logger — must be safe to call during
    /// static initialization.
    /// </summary>
    public static class AnchorRedirect
    {
        public const string FileName = ".redirect";

        public static string GetRedirectPath(string anchorDir) =>
            Path.Combine(anchorDir, FileName);

        /// <summary>
        /// Read the redirect target. Returns null when no redirect file exists or its
        /// content is empty / whitespace. Throws on IO error or when the target is not
        /// an absolute path — by design, a corrupt redirect halts startup so the user
        /// sees the failure instead of silently losing their data dir.
        /// </summary>
        public static string? TryRead(string anchorDir)
        {
            var path = GetRedirectPath(anchorDir);
            if (!File.Exists(path)) return null;

            var content = File.ReadAllText(path).Trim();
            if (string.IsNullOrWhiteSpace(content)) return null;

            if (!Path.IsPathFullyQualified(content))
            {
                throw new InvalidOperationException(
                    $"Anchor redirect at '{path}' must contain an absolute path; got '{content}'.");
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(content));
        }

        /// <summary>
        /// Write the redirect file. Refuses to point at the anchor itself (would be a no-op
        /// pointer) — caller should <see cref="Delete"/> instead. The anchor directory is
        /// created if missing.
        /// </summary>
        public static void Write(string anchorDir, string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new ArgumentException("Target path is required.", nameof(targetPath));
            if (!Path.IsPathFullyQualified(targetPath))
                throw new ArgumentException(
                    $"Target must be an absolute path; got '{targetPath}'.", nameof(targetPath));

            var normalisedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
            var normalisedAnchor = Path.TrimEndingDirectorySeparator(Path.GetFullPath(anchorDir));

            if (string.Equals(normalisedTarget, normalisedAnchor, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Anchor redirect cannot point at the anchor itself; delete the file instead.");
            }

            Directory.CreateDirectory(anchorDir);
            File.WriteAllText(GetRedirectPath(anchorDir), normalisedTarget);
        }

        public static void Delete(string anchorDir)
        {
            var path = GetRedirectPath(anchorDir);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static bool Exists(string anchorDir) =>
            File.Exists(GetRedirectPath(anchorDir));
    }
}
