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
        public const string EnvVarName = "BAKABASE_DATA_DIR";
        public const string XdgDataHomeEnvVar = "XDG_DATA_HOME";
        public const string FolderName = "Bakabase";

        public static string Resolve(
            OSPlatform platform,
            Func<string, string?> getEnv,
            Func<Environment.SpecialFolder, string> getFolder,
            string entryAssemblyName,
            bool isDebug = false)
        {
            var envOverride = getEnv(EnvVarName);
            if (!string.IsNullOrWhiteSpace(envOverride))
            {
                if (!Path.IsPathFullyQualified(envOverride))
                {
                    throw new InvalidOperationException(
                        $"{EnvVarName} must be an absolute path; got '{envOverride}'.");
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
                    FolderName);
            }

            if (platform == OSPlatform.OSX)
            {
                return Path.Combine(
                    getFolder(Environment.SpecialFolder.UserProfile),
                    "Library",
                    "Application Support",
                    FolderName);
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
                return Path.Combine(baseDir, FolderName);
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
