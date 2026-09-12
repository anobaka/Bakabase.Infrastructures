using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bakabase.Infrastructures.Components.App
{
    /// <summary>
    /// Pure resolver for the platform-default AppData anchor directory.
    /// Decoupled from <see cref="Environment"/> for testability across platforms.
    /// </summary>
    public static class DefaultAppDataPathResolver
    {
        /// <summary>
        /// The all-in-one build's names, kept as constants for the callers and tests that
        /// name them directly. Anything that has to work for either build reads
        /// <see cref="AppDataPathProfile"/> instead.
        /// </summary>
        public const string EnvVarName = "BAKABASE_DATA_DIR";

        public const string XdgDataHomeEnvVar = "XDG_DATA_HOME";
        public const string FolderName = "Bakabase";

        /// <summary>
        /// Windows-only: the AppData folder name is intentionally distinct from
        /// <see cref="FolderName"/> so the anchor lives outside Velopack's install root
        /// (which is named after <see cref="FolderName"/>). This way an uninstall — which
        /// removes the install root wholesale — leaves user data alone. macOS / Linux don't
        /// need this since their conventional AppData locations are already outside any
        /// install tree.
        /// </summary>
        public const string WindowsAppDataFolderName = "Bakabase.AppData";

        /// <summary>
        /// Resolves for the profile the app has always used. Kept so the many existing
        /// callers and tests read unchanged.
        /// </summary>
        public static string Resolve(
            OSPlatform platform,
            Func<string, string?> getEnv,
            Func<Environment.SpecialFolder, string> getFolder,
            string entryAssemblyName,
            bool isDebug = false) =>
            Resolve(AppDataPathProfile.AllInOne, platform, getEnv, getFolder, entryAssemblyName, isDebug);

        /// <summary>
        /// Resolves the anchor for one <paramref name="profile"/>. Every name that
        /// separates the builds — the env var, the folder, the Windows AppData folder —
        /// comes from the profile; the platform rules around them are shared, so the two
        /// builds cannot drift into different conventions.
        /// </summary>
        public static string Resolve(
            AppDataPathProfile profile,
            OSPlatform platform,
            Func<string, string?> getEnv,
            Func<Environment.SpecialFolder, string> getFolder,
            string entryAssemblyName,
            bool isDebug = false)
        {
            var envOverride = getEnv(profile.EnvVarName);
            if (!string.IsNullOrWhiteSpace(envOverride))
            {
                if (!Path.IsPathFullyQualified(envOverride))
                {
                    throw new InvalidOperationException(
                        $"{profile.EnvVarName} must be an absolute path; got '{envOverride}'.");
                }

                return Path.GetFullPath(envOverride);
            }

            if (isDebug)
            {
                return Path.Combine(
                    getFolder(Environment.SpecialFolder.ApplicationData),
                    $"{entryAssemblyName}.Debugging");
            }

            if (platform == OSPlatform.Windows)
            {
                return Path.Combine(
                    getFolder(Environment.SpecialFolder.LocalApplicationData),
                    profile.WindowsAppDataFolderName);
            }

            if (platform == OSPlatform.OSX)
            {
                return Path.Combine(
                    getFolder(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    profile.FolderName);
            }

            if (platform == OSPlatform.Linux)
            {
                var xdg = getEnv(XdgDataHomeEnvVar);
                var baseDir = !string.IsNullOrWhiteSpace(xdg)
                    ? xdg
                    : Path.Combine(
                        getFolder(Environment.SpecialFolder.UserProfile),
                        ".local",
                        "share");
                return Path.Combine(baseDir, profile.FolderName);
            }

            throw new PlatformNotSupportedException($"Unsupported platform: {platform}.");
        }

        internal static OSPlatform GetCurrentOsPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return OSPlatform.Windows;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return OSPlatform.OSX;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return OSPlatform.Linux;
            throw new PlatformNotSupportedException();
        }
    }
}
