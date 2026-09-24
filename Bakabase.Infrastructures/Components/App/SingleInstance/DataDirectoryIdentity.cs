using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Bakabase.Infrastructures.Components.App.SingleInstance
{
    /// <summary>
    /// One spelling per data directory, so two launches that name the same directory
    /// differently still agree on who owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The lock itself does not depend on this: it is a lock on a file <i>inside</i> the
    /// directory, and the operating system knows two spellings of one directory reach one
    /// file. What depends on it is everything named <i>after</i> the directory — the
    /// activation channel a refused launch uses to bring the running window forward, and any
    /// "is this the same directory" comparison. Those are strings, so the spelling has to be
    /// canonical.
    /// </para>
    /// <para>
    /// Canonical means: an absolute path with no trailing separator; every symbolic link and
    /// (on Windows) junction along it resolved, as far as the path exists; Unicode composed
    /// on macOS, whose file systems treat composed and decomposed names as one; and folded to
    /// upper case on Windows and macOS, whose default file systems ignore case. Linux keeps
    /// the bytes it was given, as its file systems do.
    /// </para>
    /// </remarks>
    public static class DataDirectoryIdentity
    {
        /// <summary>How the file system this process runs on compares names.</summary>
        public enum PathRules
        {
            /// <summary>Linux and the other Unixes: names are bytes.</summary>
            CaseSensitive,

            /// <summary>Windows: names ignore case.</summary>
            Windows,

            /// <summary>macOS: names ignore case and Unicode normalization.</summary>
            MacOs,
        }

        /// <summary>Links followed before giving up on a cycle; the same bound the kernels use.</summary>
        private const int MaxLinkHops = 40;

        public static PathRules CurrentRules =>
            OperatingSystem.IsWindows() ? PathRules.Windows :
            OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? PathRules.MacOs :
            PathRules.CaseSensitive;

        /// <summary>
        /// The canonical spelling of <paramref name="path"/> on this machine.
        /// </summary>
        public static string Normalize(string path) => Normalize(path, CurrentRules, resolveLinks: true);

        /// <summary>
        /// The canonical spelling under <paramref name="rules"/>. Links are resolved against
        /// the real file system only when <paramref name="resolveLinks"/> is set, so the
        /// string rules can be tested for every platform on any one of them.
        /// </summary>
        public static string Normalize(string path, PathRules rules, bool resolveLinks)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A data directory path is required.", nameof(path));
            }

            var full = Trim(Path.GetFullPath(path));

            if (resolveLinks)
            {
                full = ResolveLinks(full, MaxLinkHops);
            }

            switch (rules)
            {
                case PathRules.Windows:
                    return full.ToUpperInvariant();
                case PathRules.MacOs:
                    return full.Normalize(NormalizationForm.FormC).ToUpperInvariant();
                default:
                    return full;
            }
        }

        /// <summary>
        /// A short, stable, file-name-safe digest of an already <see cref="Normalize(string)"/>d
        /// path: the first 64 bits of its SHA-256, as lower-case hex.
        /// </summary>
        public static string Hash(string normalizedPath) => Digest(normalizedPath, 16);

        internal static string Digest(string value, int hexLength)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes).Substring(0, hexLength).ToLowerInvariant();
        }

        /// <summary>
        /// Resolves links component by component, left to right, so a link in the middle of the
        /// path (<c>/tmp</c> → <c>/private/tmp</c> on macOS) is caught as well as one at the
        /// end. Stops resolving at the first component that does not exist yet — a data
        /// directory about to be created has nothing to resolve — and appends the rest as is.
        /// </summary>
        private static string ResolveLinks(string fullPath, int hopsLeft)
        {
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root))
            {
                return fullPath;
            }

            var parts = fullPath.Substring(root.Length)
                .Split(new[] {Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar},
                    StringSplitOptions.RemoveEmptyEntries);
            var current = root;

            for (var i = 0; i < parts.Length; i++)
            {
                var next = Path.Combine(current, parts[i]);

                FileSystemInfo? target;
                try
                {
                    var info = new DirectoryInfo(next);
                    if (!info.Exists && !File.Exists(next))
                    {
                        return Trim(Path.Combine(new[] {next}.Concat(parts.Skip(i + 1)).ToArray()));
                    }

                    target = info.LinkTarget == null ? null : info.ResolveLinkTarget(returnFinalTarget: true);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // Unreadable or a dangling link: keep the spelling we have. The lock is
                    // still right; only a second spelling of this directory would miss the
                    // channel.
                    target = null;
                }

                if (target == null)
                {
                    current = next;
                    continue;
                }

                if (hopsLeft <= 0)
                {
                    // A cycle, or a chain longer than the OS itself would follow.
                    return Trim(Path.Combine(new[] {next}.Concat(parts.Skip(i + 1)).ToArray()));
                }

                // The target's own parents may be links too.
                current = ResolveLinks(Trim(Path.GetFullPath(target.FullName)), hopsLeft - 1);
            }

            return Trim(current);
        }

        private static string Trim(string path)
        {
            var trimmed = Path.TrimEndingDirectorySeparator(path);
            return trimmed.Length == 0 ? path : trimmed;
        }
    }
}
