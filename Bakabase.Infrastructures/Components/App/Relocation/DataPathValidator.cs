using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bakabase.Infrastructures.Components.App.Relocation
{
    /// <summary>
    /// Refusal-rule and target-state classifier used by the validate API. Pure logic — accepts
    /// IO callbacks for testability.
    ///
    /// Classification is deliberately marker-only: we look for <c>app.json</c>, the canonical
    /// sqlite DB, or a <c>configs/</c> folder, and never enumerate the target tree. This keeps
    /// validation O(few stat calls) regardless of how many files the user's chosen folder
    /// contains. With merge-overwrite semantics in <see cref="PendingRelocationRunner"/>, the
    /// distinction between "empty target" and "target with non-Bakabase files" no longer
    /// affects behaviour, so they collapse into a single <see cref="TargetState.NeedsCopy"/>.
    /// </summary>
    public static class DataPathValidator
    {
        /// <summary>
        /// Default minimum free space the target must have before we let the user commit.
        /// 1 GB is intentionally coarse — a precise "current AppData size × 1.5" check would
        /// require an O(N) recursive stat of the source. The accurate space check is deferred
        /// to the actual copy time, which fails fast on disk-full anyway.
        /// </summary>
        public const long DefaultMinFreeBytes = 1L * 1024 * 1024 * 1024;

        public enum RefusalReason
        {
            None = 0,
            RelativePath,
            InvalidChars,
            SameAsCurrent,
            InsideInstall,
            CircularContainment,
            SystemPath,
            NoWritePermission,
            InsufficientSpace,
        }

        /// <summary>
        /// Two-state target classification:
        /// <list type="bullet">
        /// <item><see cref="NeedsCopy"/> — target is missing, empty, or contains files we don't
        /// recognise. Either way, the data we're copying will land on top of (and possibly
        /// alongside) whatever's there.</item>
        /// <item><see cref="HasBakabaseData"/> — target already holds a Bakabase install
        /// (detected via <c>app.json</c> / canonical DB / <c>configs/</c>); the user gets to
        /// pick between using it as-is or merging current over it.</item>
        /// </list>
        /// </summary>
        public enum TargetState
        {
            NeedsCopy = 0,
            HasBakabaseData = 1,
        }

        public sealed class Input
        {
            public string TargetPath { get; set; } = null!;
            public string CurrentDataDir { get; set; } = null!;

            /// <summary>
            /// Heuristic floor for free-space pre-flight. Defaults to <see cref="DefaultMinFreeBytes"/>.
            /// </summary>
            public long MinFreeBytes { get; set; } = DefaultMinFreeBytes;

            public Func<string, long> GetFreeSpaceBytes { get; set; } = null!;
            public Func<string, bool> CanWrite { get; set; } = null!;
            public Func<string, string?> FindVelopackInstallRoot { get; set; } = null!;
            public OSPlatform Platform { get; set; }
        }

        public sealed class Output
        {
            public bool Valid { get; set; }
            public RefusalReason Reason { get; set; }
            public TargetState State { get; set; }
            public string? TargetAppVersion { get; set; }
            public long FreeSpaceBytes { get; set; }
            public long MinFreeBytes { get; set; }
        }

        public static Output Validate(Input input)
        {
            var output = new Output { MinFreeBytes = input.MinFreeBytes };

            // 1. relative / unqualified path. Platform-aware to support tests that pass
            // Windows-style paths on non-Windows hosts.
            if (string.IsNullOrWhiteSpace(input.TargetPath) ||
                !IsAbsolutePath(input.TargetPath, input.Platform))
            {
                return Refuse(output, RefusalReason.RelativePath);
            }

            string normalisedTarget;
            try
            {
                normalisedTarget = NormalisePath(input.TargetPath, input.Platform);
            }
            catch (ArgumentException)
            {
                return Refuse(output, RefusalReason.InvalidChars);
            }
            catch (NotSupportedException)
            {
                return Refuse(output, RefusalReason.InvalidChars);
            }

            var normalisedCurrent = NormalisePath(input.CurrentDataDir, input.Platform);

            // 2. same as current
            if (PathsEqual(normalisedTarget, normalisedCurrent, input.Platform))
            {
                return Refuse(output, RefusalReason.SameAsCurrent);
            }

            // 3. circular containment (one is ancestor of the other)
            if (IsAncestorOrEqual(normalisedTarget, normalisedCurrent, input.Platform) ||
                IsAncestorOrEqual(normalisedCurrent, normalisedTarget, input.Platform))
            {
                return Refuse(output, RefusalReason.CircularContainment);
            }

            // 4. inside Velopack-managed subdirs (current/ and packages/) — these get
            //    atomically replaced or pruned during upgrades so user data placed inside
            //    them WILL be destroyed. The install root itself and other siblings are
            //    safe from upgrades; the orthogonal uninstall / Repair risk is surfaced
            //    separately via AppInfo.DataInInstallRoot, not refused here, because
            //    refusing it would also block the recovery flow for users coming from
            //    2.3.0-beta.69~74 (whose data physically lives at the install root).
            var installRoot = input.FindVelopackInstallRoot(System.AppContext.BaseDirectory);
            if (installRoot != null)
            {
                var sep = SeparatorFor(input.Platform);
                var currentDir = installRoot + sep + "current";
                var packagesDir = installRoot + sep + "packages";
                if (IsAncestorOrEqual(currentDir, normalisedTarget, input.Platform) ||
                    IsAncestorOrEqual(packagesDir, normalisedTarget, input.Platform))
                {
                    return Refuse(output, RefusalReason.InsideInstall);
                }
            }

            // 5. system path
            if (IsSystemPath(normalisedTarget, input.Platform))
            {
                return Refuse(output, RefusalReason.SystemPath);
            }

            // 6. write permission — checked against the closest existing ancestor.
            if (!input.CanWrite(normalisedTarget))
            {
                return Refuse(output, RefusalReason.NoWritePermission);
            }

            // 7. classify target state — marker-only, no enumeration.
            output.State = ClassifyTarget(normalisedTarget, out var version);
            output.TargetAppVersion = version;

            // 8. coarse free-space pre-flight. The precise check happens at copy time.
            var free = input.GetFreeSpaceBytes(normalisedTarget);
            output.FreeSpaceBytes = free;
            if (input.MinFreeBytes > 0 && free < input.MinFreeBytes)
            {
                return Refuse(output, RefusalReason.InsufficientSpace);
            }

            output.Valid = true;
            output.Reason = RefusalReason.None;
            return output;
        }

        /// <summary>
        /// Marker-only classification: 3 cheap <c>File.Exists</c> / <c>Directory.Exists</c>
        /// calls. Does not enumerate the target tree, so cost is independent of how many
        /// unrelated files the user happens to have in that folder.
        /// </summary>
        public static TargetState ClassifyTarget(string target, out string? appVersion)
        {
            appVersion = null;
            if (!Directory.Exists(target))
            {
                return TargetState.NeedsCopy;
            }

            var appJsonPath = Path.Combine(target, "app.json");
            if (File.Exists(appJsonPath))
            {
                appVersion = TryReadAppVersion(appJsonPath);
                return TargetState.HasBakabaseData;
            }

            if (File.Exists(Path.Combine(target, "bakabase_insideworld.db")) ||
                Directory.Exists(Path.Combine(target, "configs")))
            {
                return TargetState.HasBakabaseData;
            }

            return TargetState.NeedsCopy;
        }

        private static string? TryReadAppVersion(string appJsonPath)
        {
            try
            {
                var json = File.ReadAllText(appJsonPath);
                // app.json wraps options under a top-level key whose name we cannot know without
                // touching configuration plumbing here. Find the first "version" property.
                var match = System.Text.RegularExpressions.Regex.Match(
                    json, "\"version\"\\s*:\\s*\"([^\"]+)\"",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value : null;
            }
            catch
            {
                return null;
            }
        }

        public static string? FindVelopackInstallRootDefault(string baseDirectory)
        {
            try
            {
                var dir = new DirectoryInfo(baseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Update.exe")))
                    {
                        return Path.TrimEndingDirectorySeparator(dir.FullName);
                    }

                    if (dir.Name.Equals("current", StringComparison.OrdinalIgnoreCase) &&
                        dir.Parent != null)
                    {
                        return Path.TrimEndingDirectorySeparator(dir.Parent.FullName);
                    }

                    dir = dir.Parent;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        public static bool CanWriteDefault(string target)
        {
            try
            {
                // Walk up to the closest existing ancestor; if even that is unwritable, refuse.
                var dir = target;
                while (!Directory.Exists(dir))
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(parent) || parent == dir) return false;
                    dir = parent;
                }

                var probe = Path.Combine(dir, $".bakabase_write_probe_{Guid.NewGuid():N}");
                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static long GetFreeSpaceBytesDefault(string target)
        {
            try
            {
                var dir = target;
                while (!Directory.Exists(dir))
                {
                    var parent = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(parent) || parent == dir) return 0L;
                    dir = parent;
                }

                var di = new DriveInfo(Path.GetPathRoot(dir) ?? "/");
                return di.AvailableFreeSpace;
            }
            catch
            {
                return long.MaxValue; // don't refuse on probe failure
            }
        }

        private static readonly string[] WindowsSystemPrefixes =
        {
            @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
        };

        private static readonly string[] MacOsSystemPrefixes =
        {
            "/System", "/Library", "/Applications", "/usr", "/bin", "/sbin",
        };

        private static readonly string[] LinuxSystemPrefixes =
        {
            "/etc", "/usr", "/var", "/bin", "/sbin", "/lib", "/lib64", "/proc", "/sys",
        };

        public static bool IsSystemPath(string normalisedTarget, OSPlatform platform)
        {
            string[] prefixes;

            if (platform == OSPlatform.Windows)
            {
                prefixes = WindowsSystemPrefixes;
            }
            else if (platform == OSPlatform.OSX)
            {
                prefixes = MacOsSystemPrefixes;
            }
            else if (platform == OSPlatform.Linux)
            {
                prefixes = LinuxSystemPrefixes;
            }
            else
            {
                return false;
            }

            foreach (var prefix in prefixes)
            {
                if (IsAncestorOrEqual(prefix, normalisedTarget, platform)) return true;
            }

            return false;
        }

        private static bool IsAncestorOrEqual(string ancestor, string descendant, OSPlatform platform)
        {
            ancestor = TrimSep(ancestor, platform);
            descendant = TrimSep(descendant, platform);
            var cmp = platform == OSPlatform.Linux
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (string.Equals(ancestor, descendant, cmp)) return true;

            var sep = SeparatorFor(platform);
            var ancestorWithSep = ancestor + sep;
            return descendant.StartsWith(ancestorWithSep, cmp);
        }

        private static bool IsAbsolutePath(string path, OSPlatform platform)
        {
            if (string.IsNullOrEmpty(path)) return false;
            if (platform == OSPlatform.Windows)
            {
                if (path.Length >= 2 && path.StartsWith(@"\\")) return true; // UNC
                if (path.Length >= 3 &&
                    char.IsLetter(path[0]) && path[1] == ':' &&
                    (path[2] == '\\' || path[2] == '/')) return true;
                return false;
            }

            return path.StartsWith('/');
        }

        /// <summary>
        /// Platform-aware path canonicalisation: collapses redundant separators / "." / ".."
        /// and trims trailing separator, all without consulting the runtime platform's
        /// <see cref="Path"/> semantics. Necessary so a Windows-platform validation on a
        /// non-Windows host doesn't get rewritten via <see cref="Path.GetFullPath(string)"/>.
        /// </summary>
        public static string NormalisePath(string path, OSPlatform platform)
        {
            var sep = SeparatorFor(platform);
            // Convert all separators to the platform's preferred form.
            var unified = platform == OSPlatform.Windows
                ? path.Replace('/', '\\')
                : path.Replace('\\', '/');

            string root;
            string tail;
            if (platform == OSPlatform.Windows)
            {
                if (unified.Length >= 3 && char.IsLetter(unified[0]) && unified[1] == ':')
                {
                    root = unified.Substring(0, 3); // "C:\"
                    tail = unified.Substring(3);
                }
                else if (unified.StartsWith(@"\\"))
                {
                    root = @"\\";
                    tail = unified.Substring(2);
                }
                else
                {
                    throw new ArgumentException("Path is not absolute on Windows.", nameof(path));
                }
            }
            else
            {
                root = "/";
                tail = unified.StartsWith('/') ? unified.Substring(1) : unified;
            }

            var stack = new System.Collections.Generic.List<string>();
            foreach (var segment in tail.Split(sep, StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".") continue;
                if (segment == "..")
                {
                    if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                stack.Add(segment);
            }

            var combined = root + string.Join(sep, stack);
            return TrimSep(combined, platform);
        }

        private static char SeparatorFor(OSPlatform platform) =>
            platform == OSPlatform.Windows ? '\\' : '/';

        private static string TrimSep(string p, OSPlatform platform)
        {
            var sep = SeparatorFor(platform);
            if (p.Length <= 1) return p;

            // Don't trim a root like "/" or "C:\".
            if (platform == OSPlatform.Windows)
            {
                if (p.Length == 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == sep) return p;
            }

            return p.EndsWith(sep) ? p.Substring(0, p.Length - 1) : p;
        }

        private static bool PathsEqual(string a, string b, OSPlatform platform)
        {
            var cmp = platform == OSPlatform.Linux
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            return string.Equals(
                Path.TrimEndingDirectorySeparator(a),
                Path.TrimEndingDirectorySeparator(b),
                cmp);
        }

        private static Output Refuse(Output output, RefusalReason reason)
        {
            output.Valid = false;
            output.Reason = reason;
            return output;
        }
    }
}
